using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Xunit;
using Xunit.Abstractions;

using static Certes.Helper;
using static Certes.IntegrationHelper;

namespace Certes
{
    public partial class AcmeContextIntegration
    {
        public class CertificateByDnsPersistTests : AcmeContextIntegration
        {
            public CertificateByDnsPersistTests(ITestOutputHelper output)
                : base(output)
            {
            }

            [Fact]
            public async Task CanGenerateCertificateDnsPersist()
            {
                var dirUri = await GetAcmeUriV2();

                var hosts = new[] { $"www-dnspersist-es256.certes.test", $"mail-dnspersist-es256.certes.test" };
                var ctx = new AcmeContext(dirUri, GetKeyV2(), http: GetAcmeHttpClient(dirUri), badNonceRetryCount: 5);
                var orderCtx = await AuthzDnsPersist(ctx, hosts);
                while (orderCtx == null)
                {
                    Output.WriteLine("DNS persist authz failed, retrying...");
                    orderCtx = await AuthzDnsPersist(ctx, hosts);
                }

                var csr = new CertificationRequestBuilder();
                csr.AddName($"C=CA, ST=Ontario, L=Toronto, O=Certes, OU=Dev, CN={hosts[0]}");
                foreach (var h in hosts)
                {
                    csr.SubjectAlternativeNames.Add(h);
                }

                var der = csr.Generate();

                var finalizedOrder = await orderCtx.Finalize(der);
                var certificate = await orderCtx.Download(null);

                await ClearAuthorizations(orderCtx);
            }
        }

        protected async Task<Acme.IOrderContext> AuthzDnsPersist(AcmeContext ctx, string[] hosts)
        {
            var orderCtx = await ctx.NewOrder(hosts);
            var order = await orderCtx.Resource();
            Assert.NotNull(order);
            Assert.Equal(hosts.Length, order.Authorizations?.Count);
            Assert.True(
                OrderStatus.Pending == order.Status || OrderStatus.Processing == order.Status || OrderStatus.Ready == order.Status,
                $"actual: {order.Status}");

            var authorizations = await orderCtx.Authorizations();

            var accountUrl = await ctx.Account().Location();

            foreach (var authz in authorizations)
            {
                var res = await authz.Resource();

                // Get issuerDomainNames from the authorization's challenge list,
                // as Pebble includes it in the authorization response but not in
                // individual challenge resource fetches.
                var dnsPersistChallengeData = res.Challenges
                    .FirstOrDefault(c => c.Type == ChallengeTypes.DnsPersist01);
                Assert.NotNull(dnsPersistChallengeData);
                Assert.NotNull(dnsPersistChallengeData.IssuerDomainNames);
                Assert.NotEmpty(dnsPersistChallengeData.IssuerDomainNames);

                var issuerDomain = dnsPersistChallengeData.IssuerDomainNames[0];
                await DeployDnsPersist01(res.Identifier.Value, issuerDomain, accountUrl);
            }

            await Task.Delay(1000);

            foreach (var authz in authorizations)
            {
                var res = await authz.Resource();
                if (res.Status == AuthorizationStatus.Pending)
                {
                    var dnsPersistChallenge = await authz.DnsPersist();
                    await dnsPersistChallenge.Validate();
                }
            }

            while (true)
            {
                await Task.Delay(100);

                var statuses = new List<AuthorizationStatus>();
                foreach (var authz in authorizations)
                {
                    var a = await authz.Resource();
                    if (AuthorizationStatus.Invalid == a?.Status)
                    {
                        return null;
                    }
                    else
                    {
                        statuses.Add(a?.Status ?? AuthorizationStatus.Pending);
                    }
                }

                if (statuses.All(s => s == AuthorizationStatus.Valid))
                {
                    break;
                }
            }

            return orderCtx;
        }
    }
}
