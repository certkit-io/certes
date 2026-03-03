using System;
using System.Text;

namespace Certes
{
    /// <summary>
    /// Helper methods for constructing dns-persist-01 DNS TXT records
    /// per draft-ietf-acme-dns-persist-00.
    /// </summary>
    public static class DnsPersist
    {
        /// <summary>
        /// The DNS label prefix for dns-persist-01 validation records.
        /// </summary>
        public const string TxtRecordPrefix = "_validation-persist";

        /// <summary>
        /// Gets the fully qualified DNS record name for a dns-persist-01 challenge.
        /// </summary>
        /// <param name="domain">The domain being validated.</param>
        /// <returns>The DNS record name, e.g. <c>_validation-persist.example.com</c>.</returns>
        public static string TxtRecordName(string domain) => $"{TxtRecordPrefix}.{domain}";

        /// <summary>
        /// Builds the TXT record value for a dns-persist-01 challenge.
        /// </summary>
        /// <param name="issuerDomain">
        /// The issuer domain name selected from the challenge's
        /// <c>issuer-domain-names</c> array.
        /// </param>
        /// <param name="accountUri">The ACME account URL.</param>
        /// <param name="wildcard">
        /// When <c>true</c>, includes <c>policy=wildcard</c> to authorize
        /// wildcard and subdomain certificates.
        /// </param>
        /// <param name="persistUntil">
        /// Optional expiration for the validation record as a Unix timestamp.
        /// </param>
        /// <returns>
        /// The TXT record value, e.g.
        /// <c>authority.example;accounturi=https://ca.example/acct/123</c>.
        /// </returns>
        public static string TxtRecordValue(
            string issuerDomain,
            Uri accountUri,
            bool wildcard = false,
            DateTimeOffset? persistUntil = null)
        {
            var sb = new StringBuilder();
            sb.Append(issuerDomain);
            sb.Append(";accounturi=");
            sb.Append(accountUri);

            if (wildcard)
            {
                sb.Append(";policy=wildcard");
            }

            if (persistUntil.HasValue)
            {
                sb.Append(";persistUntil=");
                sb.Append(persistUntil.Value.ToUnixTimeSeconds());
            }

            return sb.ToString();
        }
    }
}
