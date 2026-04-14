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
            Assert.Single(chain.IssuersWithoutRoot);
            Assert.False(chain.IssuersWithoutRoot[0].IsSelfSigned());
        }

        [Fact]
        public void IssuersWithoutRoot_KeepsAllIssuersWhenLastIsNotCa()
        {
            var pem =
                string.Join(Environment.NewLine,
                File.ReadAllText("./Data/leaf-cert.pem").Trim(),
                File.ReadAllText("./Data/alternateLeaf.pem").Trim(),
                File.ReadAllText("./Data/defaultLeaf.pem").Trim());

            var chain = new CertificateChain(pem);

            Assert.Equal(2, chain.Issuers.Count);
            Assert.Equal(2, chain.IssuersWithoutRoot.Count);
            Assert.Equal(
                chain.Issuers[0].ToPem().Replace("\r", "").Trim(),
                chain.IssuersWithoutRoot[0].ToPem().Replace("\r", "").Trim());
            Assert.Equal(
                chain.Issuers[1].ToPem().Replace("\r", "").Trim(),
                chain.IssuersWithoutRoot[1].ToPem().Replace("\r", "").Trim());
        }

        [Fact]
        public void IssuersWithoutRoot_FiltersCrossSignedRoot()
        {
            var pem =
                string.Join(Environment.NewLine,
                File.ReadAllText("./Data/leaf-cert.pem").Trim(),
                File.ReadAllText("./Data/test-ca2.pem").Trim(),
                File.ReadAllText("./Data/gts-root-crosssigned.pem").Trim());

            var chain = new CertificateChain(pem);

            Assert.Equal(2, chain.Issuers.Count);
            Assert.Single(chain.IssuersWithoutRoot);
            Assert.False(chain.IssuersWithoutRoot[0].IsSelfSigned());
            Assert.Equal(
                File.ReadAllText("./Data/test-ca2.pem").Replace("\r", "").Trim(),
                chain.IssuersWithoutRoot[0].ToPem().Replace("\r", "").Trim());
        }
    }
}
