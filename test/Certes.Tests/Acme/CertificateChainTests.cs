using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Certes.Acme
{
    public class CertificateChainTests
    {
        [Fact]
        public void CanGenerateFullChainPem()
        {
            var pem =
                string.Join(Environment.NewLine,
                File.ReadAllText("./Data/leaf-cert.pem").Trim(),
                File.ReadAllText("./Data/test-ca2.pem").Trim(),
                File.ReadAllText("./Data/test-root.pem").Trim());

            var chain = new CertificateChain(pem);
            var result = chain.ToPem();
            Assert.Equal(pem.Replace("\r", "").Trim(), result.Replace("\r", "").Trim());
        }

        [Fact]
        public void CanGenerateFullChainPemWithKey()
        {
            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);

            var pem =
                string.Join(Environment.NewLine,
                File.ReadAllText("./Data/cert.pem").Trim());

            var expectedPem =
                key.ToPem().Trim() +
                "\n" + 
                pem;

            var chain = new CertificateChain(pem);
            var result = chain.ToPem(key);
            Assert.Equal(expectedPem.Replace("\r", "").Trim(), result.Replace("\r", "").Trim());
        }

        [Fact]
        public void IsSelfSigned_ReturnsTrueForRootCert()
        {
            var pem = File.ReadAllText("./Data/test-root.pem");
            var chain = new CertificateChain(pem);
            Assert.True(chain.Certificate.IsSelfSigned());
        }

        [Fact]
        public void IsSelfSigned_ReturnsFalseForIntermediateCert()
        {
            var pem = File.ReadAllText("./Data/test-ca2.pem");
            var chain = new CertificateChain(pem);
            Assert.False(chain.Certificate.IsSelfSigned());
        }

        [Fact]
        public void IsSelfSigned_ReturnsFalseForLeafCert()
        {
            var pem = File.ReadAllText("./Data/leaf-cert.pem");
            var chain = new CertificateChain(pem);
            Assert.False(chain.Certificate.IsSelfSigned());
        }

        [Fact]
        public void IsSelfSigned_FiltersRootFromIssuers()
        {
            var pem =
                string.Join(Environment.NewLine,
                File.ReadAllText("./Data/leaf-cert.pem").Trim(),
                File.ReadAllText("./Data/test-ca2.pem").Trim(),
                File.ReadAllText("./Data/test-root.pem").Trim());

            var chain = new CertificateChain(pem);

            Assert.Equal(2, chain.Issuers.Count);
            var nonRoots = chain.Issuers.Where(x => !x.IsSelfSigned()).ToList();
            Assert.Single(nonRoots);
            Assert.False(nonRoots[0].IsSelfSigned());
        }
    }
}
