using System;
using System.IO;
using Certes.Acme;
using Certes.Jws;
using Certes.Pkcs;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;

namespace Certes
{
    /// <summary>
    /// Extension methods for <see cref="CertificateChain"/>.
    /// </summary>
    public static class CertificateChainExtensions
    {
        /// <summary>
        /// Converts the certificate to PFX with the key.
        /// </summary>
        /// <param name="certificateChain">The certificate chain.</param>
        /// <param name="certKey">The certificate private key.</param>
        /// <returns>The PFX.</returns>
        public static PfxBuilder ToPfx(this CertificateChain certificateChain, IKey certKey)
        {
            var pfx = new PfxBuilder(certificateChain.Certificate.ToDer(), certKey);
            if (certificateChain.Issuers != null)
            {
                foreach (var issuer in certificateChain.Issuers)
                {
                    pfx.AddIssuer(issuer.ToDer());
                }
            }

            return pfx;
        }

        /// <summary>
        /// Encodes the full certificate chain in PEM.
        /// </summary>
        /// <param name="certificateChain">The certificate chain.</param>
        /// <param name="certKey">The certificate key.</param>
        /// <returns>The encoded certificate chain.</returns>
        public static string ToPem(this CertificateChain certificateChain, IKey certKey = null)
        {
            var certStore = new CertificateStore();
            foreach (var issuer in certificateChain.Issuers)
            {
                certStore.Add(issuer.ToDer());
            }

            var issuers = certStore.GetIssuers(certificateChain.Certificate.ToDer());

            using (var writer = new StringWriter())
            {
                if (certKey != null)
                {
                    writer.WriteLine(certKey.ToPem().TrimEnd());
                }

                writer.WriteLine(certificateChain.Certificate.ToPem().TrimEnd());

                var certParser = new X509CertificateParser();
                var pemWriter = new PemWriter(writer);
                foreach (var issuer in issuers)
                {
                    var cert = certParser.ReadCertificate(issuer);
                    pemWriter.WriteObject(cert);
                }

                return writer.ToString();
            }
        }

        /// <summary>
        /// Computes the ARI CertID for a certificate per RFC 9773.
        /// The result is <c>base64url(AKI) + "." + base64url(serial)</c>.
        /// </summary>
        /// <param name="certDer">The DER-encoded certificate bytes.</param>
        /// <returns>The ARI CertID string.</returns>
        public static string GetAriCertId(byte[] certDer)
        {
            var parser = new X509CertificateParser();
            var cert = parser.ReadCertificate(certDer);

            // Extract the Authority Key Identifier (OID 2.5.29.35) keyIdentifier bytes
            var akiExtValue = cert.GetExtensionValue(X509Extensions.AuthorityKeyIdentifier);
            if (akiExtValue == null)
            {
                throw new InvalidOperationException("Certificate does not contain an Authority Key Identifier extension.");
            }

            var akiOctets = Asn1OctetString.GetInstance(akiExtValue).GetOctets();
            var aki = AuthorityKeyIdentifier.GetInstance(Asn1Sequence.GetInstance(akiOctets));
            var akiBytes = aki.GetKeyIdentifier();

            // Extract serial number as unsigned big-endian bytes
            var serialBigInt = cert.SerialNumber;
            var serialBytes = serialBigInt.ToByteArrayUnsigned();

            return JwsConvert.ToBase64String(akiBytes) + "." + JwsConvert.ToBase64String(serialBytes);
        }
    }
}
