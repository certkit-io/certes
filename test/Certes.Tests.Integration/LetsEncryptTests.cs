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
    /// On-demand tests against the Let's Encrypt production ACME endpoint.
    /// These are NOT part of the automated test suite -- run them manually
    /// when you need to verify LE production integration.
    ///
    /// Workflow:
    ///   1. Run <see cref="RegisterAccount"/> once to create an account key PEM.
    ///      The PEM is written to <c>le-account.pem</c> and auto-loaded by the
    ///      other tests; alternatively paste it into <see cref="AccountPemFallback"/>.
    ///   2. Run <see cref="Can_Get_LE_Cert"/> to issue a cert (requires manual DNS TXT setup).
    ///   3. Run <see cref="CanDownloadExistingOrder"/> to re-download from a known order URI.
    /// </summary>
    public class LetsEncrypt_Tests
    {
        private readonly ITestOutputHelper _output;

        // ---------------------------------------------------------------
        // Configuration -- fill these in before running
        // ---------------------------------------------------------------

        static readonly Uri LeDirectory =
            new("https://acme-v02.api.letsencrypt.org/directory");

        const string AccountPemFile = "le-account.pem";

        /// <summary>
        /// Inline fallback for the account PEM. Used only when
        /// <see cref="AccountPemFile"/> is not present in the current directory.
        /// <see cref="RegisterAccount"/> writes the file automatically; otherwise
        /// paste the PEM here. Do not commit real key material.
        /// </summary>
        const string AccountPemFallback = "";

        /// <summary>
        /// Resolves the account PEM: reads <see cref="AccountPemFile"/> if
        /// present, otherwise returns <see cref="AccountPemFallback"/>.
        /// </summary>
        static string AccountPem =>
            File.Exists(AccountPemFile)
                ? File.ReadAllText(AccountPemFile)
                : AccountPemFallback;

        const string Domain = "le.certloop.dev";
        const string AccountEmail = "ames.olson.music@gmail.com";

        /// <summary>
        /// An existing order URI for <see cref="CanDownloadExistingOrder"/>.
        /// Update this after a successful <see cref="Can_Get_LE_Cert"/> run.
        /// </summary>
        const string ExistingOrderUri = "";

        // ---------------------------------------------------------------

        public LetsEncrypt_Tests(ITestOutputHelper output)
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
        /// One-time setup: registers a new ACME account with Let's Encrypt
        /// and writes the resulting account key PEM to <see cref="AccountPemFile"/>,
        /// where it is auto-loaded by the other tests.
        /// </summary>
        // [Fact]
        public async Task RegisterAccount()
        {
            var acme = new AcmeContext(LeDirectory);

            var account = await acme.NewAccount(
                email: AccountEmail,
                termsOfServiceAgreed: true);

            Assert.NotNull(account);

            var accountPem = acme.AccountKey.ToPem();

            await File.WriteAllTextAsync(AccountPemFile, accountPem);

            _output.WriteLine("Account registered successfully.");
            _output.WriteLine($"Key saved to {AccountPemFile} (auto-loaded by other tests).");
            _output.WriteLine("");
            _output.WriteLine(accountPem);
        }

        /// <summary>
        /// Full cert issuance flow: new order, DNS-01 challenge, finalize, download.
        /// Requires you to manually create the DNS TXT record when prompted (check test output).
        /// </summary>
        //[Fact]
        public async Task Can_Get_LE_Cert()
        {
            AssertConfiguredAccount();

            var acme = new AcmeContext(LeDirectory, KeyFactory.FromPem(AccountPem));

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
            await File.WriteAllTextAsync("le-test.pem", cert.ToPem());
            await File.WriteAllTextAsync("le-test.key", certKey.ToPem());

            _output.WriteLine("Done. Wrote le-test.pem, le-test.key");
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

            var acme = new AcmeContext(LeDirectory, KeyFactory.FromPem(AccountPem));
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
