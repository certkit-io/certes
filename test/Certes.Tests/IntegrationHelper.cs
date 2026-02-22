using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Newtonsoft.Json;
using Xunit;

namespace Certes
{
    public static class IntegrationHelper
    {
        public static readonly List<byte[]> TestCertificates = new();

        // Pebble ACME directory (mapped from container port 14000 to host port 15000)
        private const string PebbleDirectoryUrl = "https://127.0.0.1:15000/dir";

        // Pebble management endpoint (mapped from container port 15000 to host port 15002)
        private const string PebbleManagementUrl = "https://127.0.0.1:15002";

        // pebble-challtestsrv management API (mapped from container port 8055 to host port 18055)
        private const string ChallTestSrvUrl = "http://127.0.0.1:18055";

        public static readonly Lazy<HttpClient> http = new Lazy<HttpClient>(() =>
        {

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            var client = new HttpClient(handler);

            client.DefaultRequestHeaders.UserAgent.ParseAdd("CertKit-Agent/1.2.3 (+https://certkit.io)");

            return client;
        });

        private static Uri stagingServerV2;

        public static IAcmeHttpClient GetAcmeHttpClient(Uri uri) => Helper.CreateHttp(uri, http.Value);

        public static async Task<Uri> GetAcmeUriV2()
        {
            if (stagingServerV2 != null)
            {
                return stagingServerV2;
            }

            var servers = new[]
            {
                new Uri(PebbleDirectoryUrl)
            };

            var exceptions = new List<Exception>();
            foreach (var uri in servers)
            {
                try
                {
                    await http.Value.GetStringAsync(uri);

                    foreach (var algo in new[] { KeyAlgorithm.ES256, KeyAlgorithm.ES384, KeyAlgorithm.RS256 })
                    {
                        try
                        {
                            var ctx = new AcmeContext(uri, Helper.GetKeyV2(algo), GetAcmeHttpClient(uri));
                            await ctx.NewAccount(new[] { "mailto:ci@certes.app" }, true);
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        var certUri = new Uri($"{PebbleManagementUrl}/roots/0");
                        var certData = await http.Value.GetByteArrayAsync(certUri);
                        TestCertificates.Add(certData);
                    }
                    catch
                    {
                    }

                    return stagingServerV2 = uri;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            throw new AggregateException("No staging server available.", exceptions);
        }

        /// <summary>
        /// Deploy DNS-01 challenge responses via pebble-challtestsrv.
        /// Keys are hostnames, values are the computed DnsTxt content.
        /// </summary>
        public static async Task DeployDns01(KeyAlgorithm algo, Dictionary<string, string> tokens)
        {
            foreach (var (host, token) in tokens)
            {
                var payload = new { host = $"_acme-challenge.{host}.", value = token };
                using var resp = await http.Value.PostAsync(
                    $"{ChallTestSrvUrl}/set-txt",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                resp.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// Deploy an HTTP-01 challenge response via pebble-challtestsrv.
        /// </summary>
        public static async Task DeployHttp01(string token, string keyAuthz)
        {
            var payload = new { token, content = keyAuthz };
            using var resp = await http.Value.PostAsync(
                $"{ChallTestSrvUrl}/add-http01",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Clear an HTTP-01 challenge response from pebble-challtestsrv.
        /// </summary>
        public static async Task ClearHttp01(string token)
        {
            var payload = new { token };
            using var resp = await http.Value.PostAsync(
                $"{ChallTestSrvUrl}/del-http01",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Set a default IPv4 address for A record lookups via pebble-challtestsrv.
        /// </summary>
        public static async Task SetDefaultIpv4(string ip)
        {
            var payload = new { ip };
            using var resp = await http.Value.PostAsync(
                $"{ChallTestSrvUrl}/set-default-ipv4",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
        }

        public static void AddTestCerts(this PfxBuilder pfx)
        {
            foreach (var cert in TestCertificates)
            {
                pfx.AddIssuers(cert);
            }
        }

        public static async Task<IOrderContext> AuthorizeHttp(AcmeContext ctx, IList<string> hosts)
        {
            for (var i = 0; i < 10; ++i)
            {
                var orderCtx = await ctx.NewOrder(hosts);
                var order = await orderCtx.Resource();
                Assert.NotNull(order);
                Assert.Equal(hosts.Count, order.Authorizations?.Count);
                Assert.True(OrderStatus.Pending == order.Status || OrderStatus.Ready == order.Status || OrderStatus.Processing == order.Status);

                var authrizations = await orderCtx.Authorizations();

                foreach (var authz in authrizations)
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
                    foreach (var authz in authrizations)
                    {
                        var a = await authz.Resource();
                        statuses.Add(a?.Status ?? AuthorizationStatus.Pending);
                    }

                    if (statuses.All(s => s == AuthorizationStatus.Valid))
                    {
                        return orderCtx;
                    }


                    if (statuses.Any(s => s == AuthorizationStatus.Invalid))
                    {
                        break;
                    }
                }
            }

            Assert.True(false, "Authorization failed.");
            return null;
        }
    }
}
