using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certes.Acme.Resource;
using Xunit;
using Xunit.Abstractions;

namespace Certes
{
    /// <summary>
    /// On-demand tests against the Google Trust Services (GTS) ACME endpoint.
    /// These are NOT part of the automated test suite -- run them manually
    /// when you need to verify GTS integration.
    ///
    /// Workflow:
    ///   1. Run <see cref="RegisterAccountWithEAB"/> once to turn EAB creds into an account key PEM.
    ///   2. Paste that PEM into <see cref="AccountPem"/> below.
    ///   3. Run <see cref="Can_Get_GTS_Cert"/> to issue a cert (requires manual DNS TXT setup).
    ///   4. Run <see cref="CanDownloadExistingOrder"/> to re-download from a known order URI.
    /// </summary>
    public class GTS_Tests
    {
        private readonly ITestOutputHelper _output;

        // ---------------------------------------------------------------
        // Configuration -- fill these in before running
        // ---------------------------------------------------------------

        static readonly Uri GtsDirectory =
            new("https://dv.acme-v02.api.pki.goog/directory");

        /// <summary>
        /// The account PEM produced by <see cref="RegisterAccountWithEAB"/>.
        /// Paste it here locally so the other tests can reuse the same account.
        /// Do not commit real key material.
        /// </summary>
        const string AccountPem = "";

        const string Domain = "gts.certkit.dev";
        const string AccountEmail = "you@example.com";

        /// <summary>
        /// An existing order URI for <see cref="CanDownloadExistingOrder"/>.
        /// Update this after a successful <see cref="Can_Get_GTS_Cert"/> run.
        /// </summary>
        const string ExistingOrderUri = "https://dv.acme-v02.api.pki.goog/order/rtCSO8zLts1wMMUTAw7E3Q";

        // EAB credentials from `gcloud publicca external-account-keys create`.
        // Only needed for RegisterAccountWithEAB; blank them out after use.
        const string EabKeyId = "";
        const string EabHmacKey = ""; // base64url, as returned by gcloud

        // ---------------------------------------------------------------

        public GTS_Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static void AssertConfiguredAccount()
        {
            Assert.False(string.IsNullOrWhiteSpace(AccountPem),
                "Set AccountPem locally before running this test.");
        }

        private static void AssertConfiguredExistingOrder()
        {
            Assert.False(string.IsNullOrWhiteSpace(ExistingOrderUri),
                "Set ExistingOrderUri locally before running this test.");
        }

        /// <summary>
        /// One-time setup: registers a new ACME account with GTS using EAB
        /// credentials and writes the resulting account key PEM to test output
        /// and to a local file. Paste the PEM into <see cref="AccountPem"/>.
        /// </summary>
        //[Fact]
        public async Task RegisterAccountWithEAB()
        {
            Assert.False(string.IsNullOrWhiteSpace(EabKeyId),
                "Set EabKeyId before running this test.");
            Assert.False(string.IsNullOrWhiteSpace(EabHmacKey),
                "Set EabHmacKey before running this test.");

            var acme = new AcmeContext(GtsDirectory);

            var account = await acme.NewAccount(
                email: AccountEmail,
                termsOfServiceAgreed: true,
                eabKeyId: EabKeyId,
                eabKey: EabHmacKey);

            Assert.NotNull(account);

            var accountPem = acme.AccountKey.ToPem();

            await File.WriteAllTextAsync("gts-account.pem", accountPem);

            _output.WriteLine("Account registered successfully.");
            _output.WriteLine("Key saved to gts-account.pem");
            _output.WriteLine("");
            _output.WriteLine("Paste this into the AccountPem constant:");
            _output.WriteLine(accountPem);
        }

        /// <summary>
        /// Full cert issuance flow: new order, DNS-01 challenge, finalize, download.
        /// Requires you to manually create the DNS TXT record when prompted (check test output).
        /// </summary>
        //[Fact]
        public async Task Can_Get_GTS_Cert()
        {
            AssertConfiguredAccount();

            var acme = new AcmeContext(GtsDirectory, KeyFactory.FromPem(AccountPem));

            // Place a new order
            var order = await acme.NewOrder(new[] { Domain });
            var orderResource = await order.Resource();
            _output.WriteLine($"Order created: {orderResource.Status}");

            // Get the DNS-01 challenge
            var authz = (await order.Authorizations()).First();
            var dnsChallenge = await authz.Dns();
            var dnsTxtValue = acme.AccountKey.DnsTxt(dnsChallenge.Token);

            _output.WriteLine("");
            _output.WriteLine("Create this DNS TXT record, then re-run with the challenge validation step:");
            _output.WriteLine($"  _acme-challenge.{Domain}  IN  TXT  \"{dnsTxtValue}\"");

            // Only validate if still pending
            var challengeResource = await dnsChallenge.Resource();
            if (challengeResource.Status == ChallengeStatus.Pending)
            {
                await dnsChallenge.Validate();
            }

            // Poll until authorization resolves
            var authzResource = await PollAuthorization(authz);
            Assert.Equal(AuthorizationStatus.Valid, authzResource.Status);

            _output.WriteLine("Authorization valid. Finalizing order...");

            // Finalize and download
            var certKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var cert = await order.Generate(
                new CsrInfo { CommonName = Domain }, certKey);

            // Write outputs
            await File.WriteAllTextAsync("gts-test.pem", cert.ToPem());
            await File.WriteAllTextAsync("gts-test.key", certKey.ToPem());

            _output.WriteLine("Done. Wrote gts-test.pfx, gts-test.pem, gts-test.key");
        }

        /// <summary>
        /// Re-downloads a certificate from a previously completed order.
        /// Update <see cref="ExistingOrderUri"/> to point at a valid order.
        /// </summary>
        //[Fact]
        public async Task CanDownloadExistingOrder()
        {
            AssertConfiguredAccount();
            AssertConfiguredExistingOrder();

            var acme = new AcmeContext(GtsDirectory, KeyFactory.FromPem(AccountPem));
            await acme.Account();

            var order = acme.Order(new Uri(ExistingOrderUri));
            var cert = await order.Download();

            Assert.NotNull(cert);

            var pem = cert.ToPem();
            Assert.False(string.IsNullOrWhiteSpace(pem));

            _output.WriteLine($"Downloaded cert from {ExistingOrderUri}");
            _output.WriteLine(pem);
        }

        private async Task<Authorization> PollAuthorization(
            Acme.IAuthorizationContext authz, int maxAttempts = 30, int delayMs = 2000)
        {
            for (var i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(delayMs);
                var resource = await authz.Resource();

                _output.WriteLine($"  Poll {i + 1}/{maxAttempts}: {resource.Status}");

                if (resource.Status != AuthorizationStatus.Pending)
                    return resource;
            }

            throw new TimeoutException(
                $"Authorization did not resolve after {maxAttempts} attempts.");
        }
    }
}
