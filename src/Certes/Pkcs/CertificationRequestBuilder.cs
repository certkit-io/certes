using System;
using System.Collections.Generic;
using System.Linq;
using Certes.Crypto;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Pkcs;
using SystemX509Extension = System.Security.Cryptography.X509Certificates.X509Extension;

namespace Certes.Pkcs
{
    /// <summary>
    /// Represents a CSR builder.
    /// </summary>
    /// <seealso cref="Certes.Pkcs.ICertificationRequestBuilder" />
    public class CertificationRequestBuilder : ICertificationRequestBuilder
    {
        private static readonly KeyAlgorithmProvider keyAlgorithmProvider = new KeyAlgorithmProvider();
        private string commonName;
        private readonly List<(DerObjectIdentifier Id, string Value)> attributes = new List<(DerObjectIdentifier, string)>();
        private IList<string> subjectAlternativeNames = new List<string>();
        private IList<SystemX509Extension> extensions = new List<SystemX509Extension>();

        private string pkcsObjectId;
        private AsymmetricCipherKeyPair keyPair;

        /// <summary>
        /// Gets the key.
        /// </summary>
        /// <value>
        /// The key.
        /// </value>
        public IKey Key { get; }

        /// <summary>
        /// Gets the subject alternative names.
        /// </summary>
        /// <value>
        /// The subject alternative names.
        /// </value>
        public IList<string> SubjectAlternativeNames
        {
            get
            {
                return subjectAlternativeNames;
            }
            set
            {
                this.subjectAlternativeNames = value ??
                    throw new ArgumentNullException(nameof(SubjectAlternativeNames));
            }
        }

        /// <summary>
        /// Gets or sets extensions to add to the CSR, such as an
        /// <see cref="System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension"/> or a
        /// <see cref="System.Security.Cryptography.X509Certificates.X509KeyUsageExtension"/>.
        /// </summary>
        /// <value>
        /// The extensions. Empty by default, which leaves everything but the subject alternative names to the CA.
        /// An extension with the same OID as the subject alternative names replaces the one built from
        /// <see cref="SubjectAlternativeNames"/>.
        /// </value>
        /// <remarks>
        /// Most CAs set key usage and extended key usage from their own profile, but GlobalSign rejects a CSR without
        /// an extended key usage: badCSR "extended_key_usages: has to have between 1 and 3 items".
        /// </remarks>
        public IList<SystemX509Extension> Extensions
        {
            get
            {
                return extensions;
            }
            set
            {
                this.extensions = value ??
                    throw new ArgumentNullException(nameof(Extensions));
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificationRequestBuilder"/> class.
        /// </summary>
        public CertificationRequestBuilder()
            : this(KeyFactory.NewKey(KeyAlgorithm.RS256))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificationRequestBuilder"/> class.
        /// </summary>
        /// <param name="key">The key.</param>
        public CertificationRequestBuilder(IKey key)
        {
            Key = key;
        }
        /// <summary>
        /// Adds the distinguished name as certificate subject.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        public void AddName(string distinguishedName)
        {
            X509Name name;
            try
            {
                name = new X509Name(distinguishedName);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentOutOfRangeException(
                    $"{distinguishedName} contains an ivalid X509 name.", ex);
            }

            var oidList = name.GetOidList();
            var valueList = name.GetValueList();
            var len = oidList.Count;
            for (var i = 0; i < len; ++i)
            {
                var id = (DerObjectIdentifier)oidList[i];
                var value = valueList[i].ToString();
                attributes.Add((id, value));

                if (id == X509Name.CN)
                {
                    this.commonName = value;
                }
            }
        }

        /// <summary>
        /// Adds the name.
        /// </summary>
        /// <param name="keyOrCommonName">Name of the key or common.</param>
        /// <param name="value">The value.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// If <paramref name="keyOrCommonName"/> is not a valid X509 name.
        /// </exception>
        public void AddName(string keyOrCommonName, string value)
            => AddName($"{keyOrCommonName}={value}");

        /// <summary>
        /// Generates the CSR.
        /// </summary>
        /// <returns>
        /// The CSR data.
        /// </returns>
        public byte[] Generate()
        {
            var csr = GeneratePkcs10();
            return csr.GetDerEncoded();
        }

        private Pkcs10CertificationRequest GeneratePkcs10()
        {
            var x509 = new X509Name(attributes.Select(p => p.Id).ToArray(), attributes.Select(p => p.Value).ToArray());

            if (this.SubjectAlternativeNames.Count == 0)
            {
                this.SubjectAlternativeNames.Add(commonName);
            }

            var altNames = this.SubjectAlternativeNames
                .Distinct()
                .Select(n => new GeneralName(GeneralName.DnsName, n))
                .ToArray();

            // Only the SAN is requested, the same as certbot, plus any extensions the caller adds. The CA sets key usage
            // and basic constraints from its own profile, and some reject a CSR that asks for a key usage they won't grant:
            // SSL.com has answered badCSR "Requested key usage will not be granted" to NonRepudiation, and to
            // KeyEncipherment on an EC key.
            var requestedExtensions = new Dictionary<DerObjectIdentifier, X509Extension>
            {
                { X509Extensions.SubjectAlternativeName, new X509Extension(false, new DerOctetString(new GeneralNames(altNames))) }
            };

            foreach (var extension in this.Extensions)
            {
                requestedExtensions[new DerObjectIdentifier(extension.Oid.Value)] = new X509Extension(extension.Critical, new DerOctetString(extension.RawData));
            }

            var attribute = new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest, new DerSet(new X509Extensions(requestedExtensions)));

            LoadKeyPair();
            var signatureFactory = new Asn1SignatureFactory(pkcsObjectId, keyPair.Private);
            return new Pkcs10CertificationRequest(signatureFactory, x509, keyPair.Public, new DerSet(attribute));
        }

        private void LoadKeyPair()
        {
            var (algo, keyPair) = keyAlgorithmProvider.GetKeyPair(Key.ToDer());
            pkcsObjectId = algo.ToPkcsObjectId();
            this.keyPair = keyPair;
        }
    }
}
