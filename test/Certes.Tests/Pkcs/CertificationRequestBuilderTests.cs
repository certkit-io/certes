using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Pkcs;
using Xunit;

namespace Certes.Pkcs
{
    public class CertificationRequestBuilderTests
    {
        [Fact]
        public void CanCreateCsrWithSignatureKey()
        {
            var key = KeyFactory.NewKey(KeyAlgorithm.RS256);
            new CertificationRequestBuilder(key);
        }

        [Fact]
        public void CanSetSubjectAlternativeNames()
        {
            var san = new[]
            {
                "www.example.com",
                "www1.example.com"
            };

            var csr = new CertificationRequestBuilder()
            {
                SubjectAlternativeNames = san
            };

            Assert.Equal(san, csr.SubjectAlternativeNames);

            Assert.Throws<ArgumentNullException>(() => csr.SubjectAlternativeNames = null);
        }

        [Fact]
        public void CanAddAttributes()
        {
            var csr = new CertificationRequestBuilder();
            csr.AddName("st", "yonge street");
            csr.AddName("cn", "www.certes.com");

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                csr.AddName("invalid-name", "omg"));
        }

        [Fact]
        public void CanBuildCsrWithoutSubjectAlternativeName()
        {
            var csr = new CertificationRequestBuilder();
            csr.AddName("cn", "www.example.com");
            var csrData = csr.Generate();
            Assert.NotNull(csrData);
        }

        [Theory]
        [InlineData(KeyAlgorithm.RS256)]
        [InlineData(KeyAlgorithm.ES256)]
        [InlineData(KeyAlgorithm.ES384)]
        public void CsrOnlyRequestsSubjectAlternativeNames(KeyAlgorithm algorithm)
        {
            var csr = new CertificationRequestBuilder(KeyFactory.NewKey(algorithm));
            csr.AddName("cn", "www.example.com");
            csr.SubjectAlternativeNames.Add("www.example.com");
            csr.SubjectAlternativeNames.Add("example.com");

            var request = new Pkcs10CertificationRequest(csr.Generate());
            var requested = request.GetRequestedExtensions();

            // No key usage or basic constraints: the CA decides those, and SSL.com rejects a CSR that asks
            // for a key usage it will not grant.
            Assert.Equal(new[] { X509Extensions.SubjectAlternativeName }, requested.GetExtensionOids());

            var names = GeneralNames.GetInstance(requested.GetExtension(X509Extensions.SubjectAlternativeName).GetParsedValue());
            Assert.Equal(new[] { "www.example.com", "example.com" }, names.GetNames().Select(n => n.Name.ToString()));
        }

        [Fact]
        public void CsrIncludesAddedExtensions()
        {
            var csr = new CertificationRequestBuilder(KeyFactory.NewKey(KeyAlgorithm.ES256));
            csr.AddName("cn", "www.example.com");
            csr.Extensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            csr.Extensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

            var request = new Pkcs10CertificationRequest(csr.Generate());
            var requested = request.GetRequestedExtensions();

            Assert.Equal(new[] { X509Extensions.SubjectAlternativeName, X509Extensions.ExtendedKeyUsage, X509Extensions.KeyUsage }, requested.GetExtensionOids());

            var usages = ExtendedKeyUsage.GetInstance(requested.GetExtension(X509Extensions.ExtendedKeyUsage).GetParsedValue());
            Assert.Equal(new[] { "1.3.6.1.5.5.7.3.1" }, usages.GetAllUsages().Select(u => u.Id));

            var keyUsage = requested.GetExtension(X509Extensions.KeyUsage);
            Assert.True(keyUsage.IsCritical);
            Assert.Equal(KeyUsage.DigitalSignature, KeyUsage.GetInstance(keyUsage.GetParsedValue()).IntValue);

            Assert.Throws<ArgumentNullException>(() => csr.Extensions = null);
        }

        [Fact]
        public void AddedSubjectAlternativeNameReplacesTheBuiltOne()
        {
            var csr = new CertificationRequestBuilder(KeyFactory.NewKey(KeyAlgorithm.ES256));
            csr.AddName("cn", "www.example.com");
            csr.SubjectAlternativeNames.Add("www.example.com");

            var sans = new SubjectAlternativeNameBuilder();
            sans.AddDnsName("other.example.com");
            csr.Extensions.Add(sans.Build(true));

            var request = new Pkcs10CertificationRequest(csr.Generate());
            var san = request.GetRequestedExtensions().GetExtension(X509Extensions.SubjectAlternativeName);

            Assert.True(san.IsCritical);
            var names = GeneralNames.GetInstance(san.GetParsedValue());
            Assert.Equal(new[] { "other.example.com" }, names.GetNames().Select(n => n.Name.ToString()));
        }
    }
}
