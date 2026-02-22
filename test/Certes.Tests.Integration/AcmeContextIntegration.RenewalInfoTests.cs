using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certes.Acme;
using Certes.Acme.Resource;
using Xunit;
using Xunit.Abstractions;

using static Certes.Helper;
using static Certes.IntegrationHelper;

namespace Certes
{
    public partial class AcmeContextIntegration
    {
        public class RenewalInfoTests : AcmeContextIntegration
        {
            public RenewalInfoTests(ITestOutputHelper output)
                : base(output)
            {
            }

            [Fact]
            public async Task CanGetRenewalInfoAndReplace()
            {
                var dirUri = await GetAcmeUriV2();
                var hosts = new[] { "www-ari.certes.test" };
                var ctx = new AcmeContext(dirUri, GetKeyV2(), http: GetAcmeHttpClient(dirUri), badNonceRetryCount: 5);

                // 1. Issue a certificate via HTTP-01
                var orderCtx = await AuthorizeHttp(ctx, hosts);

                var certKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
                await orderCtx.Finalize(new CsrInfo
                {
                    CountryName = "CA",
                    State = "Ontario",
                    Locality = "Toronto",
                    Organization = "Certes",
                    OrganizationUnit = "Dev",
                    CommonName = hosts[0],
                }, certKey);
                var certChain = await orderCtx.Download(null);

                // 2. Compute ARI CertID
                var certDer = certChain.Certificate.ToDer();
                var certId = CertificateChainExtensions.GetAriCertId(certDer);
                Assert.NotNull(certId);
                Assert.Contains(".", certId);
                Output.WriteLine($"ARI CertID: {certId}");

                // 3. Query renewal info
                var renewalInfo = await ctx.GetRenewalInfo(certId);
                if (renewalInfo == null)
                {
                    Output.WriteLine("Server does not support ARI (renewalInfo not in directory). Skipping.");
                    return;
                }

                Assert.NotNull(renewalInfo.RenewalInfo);
                Assert.NotNull(renewalInfo.RenewalInfo.SuggestedWindow);
                Output.WriteLine($"Suggested window: {renewalInfo.RenewalInfo.SuggestedWindow.Start} - {renewalInfo.RenewalInfo.SuggestedWindow.End}");

                // 4. Place a replacement order with replaces: certId
                await ClearAuthorizations(orderCtx);

                var replaceOrderCtx = await ctx.NewOrder(hosts, replaces: certId);
                var replaceOrder = await replaceOrderCtx.Resource();
                Assert.NotNull(replaceOrder);

                // Authorize the replacement order
                var authzList = await replaceOrderCtx.Authorizations();
                foreach (var authz in authzList)
                {
                    var a = await authz.Resource();
                    if (a.Status == AuthorizationStatus.Pending)
                    {
                        var httpChallenge = await authz.Http();
                        await DeployHttp01(httpChallenge.Token, httpChallenge.KeyAuthz);
                        await httpChallenge.Validate();
                    }
                }

                while (true)
                {
                    await Task.Delay(100);
                    var statuses = new List<AuthorizationStatus>();
                    foreach (var authz in authzList)
                    {
                        var a = await authz.Resource();
                        statuses.Add(a?.Status ?? AuthorizationStatus.Pending);
                    }
                    if (statuses.All(s => s == AuthorizationStatus.Valid))
                    {
                        break;
                    }
                    Assert.DoesNotContain(AuthorizationStatus.Invalid, statuses);
                }

                var replaceKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
                await replaceOrderCtx.Finalize(new CsrInfo
                {
                    CountryName = "CA",
                    State = "Ontario",
                    Locality = "Toronto",
                    Organization = "Certes",
                    OrganizationUnit = "Dev",
                    CommonName = hosts[0],
                }, replaceKey);
                var replacementCert = await replaceOrderCtx.Download(null);
                Assert.NotNull(replacementCert);

                await ClearAuthorizations(replaceOrderCtx);
            }
        }
    }
}
