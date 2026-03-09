using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certes.Acme.Resource;
using Certes.Jws;
using Moq;
using Xunit;

namespace Certes.Acme
{
    public class OrderContextTests
    {
        private readonly Uri location = new Uri("http://acme.d/order/101");
        private readonly Mock<IAcmeContext> contextMock = new Mock<IAcmeContext>(MockBehavior.Strict);
        private readonly Mock<IAcmeHttpClient> httpClientMock = new Mock<IAcmeHttpClient>(MockBehavior.Strict);

        [Fact]
        public async Task CanLoadAuthorizations()
        {
            var order = new Order
            {
                Authorizations = new[]
                {
                    new Uri("http://acme.d/acct/1/authz/1"),
                    new Uri("http://acme.d/acct/1/authz/2"),
                }
            };

            var expectedPayload = new JwsSigner(Helper.GetKeyV2())
                .Sign("", null, location, "nonce");

            contextMock.Reset();
            httpClientMock.Reset();

            contextMock
                .Setup(c => c.GetDirectory())
                .ReturnsAsync(Helper.MockDirectoryV2);
            contextMock
                .SetupGet(c => c.AccountKey)
                .Returns(Helper.GetKeyV2());
            contextMock
                .SetupGet(c => c.BadNonceRetryCount)
                .Returns(1);
            contextMock.SetupGet(c => c.HttpClient).Returns(httpClientMock.Object);
            contextMock
                .Setup(c => c.Sign(It.IsAny<object>(), It.IsAny<Uri>()))
                .Callback((object payload, Uri loc) =>
                {
                    Assert.Null(payload);
                    Assert.Equal(location, loc);
                })
                .ReturnsAsync(expectedPayload);
            httpClientMock
                .Setup(m => m.Post<Order>(location, It.IsAny<JwsPayload>()))
                .Callback((Uri _, object o) =>
                {
                    var p = (JwsPayload)o;
                    Assert.Equal(expectedPayload.Payload, p.Payload);
                    Assert.Equal(expectedPayload.Protected, p.Protected);
                })
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, order, default, default));

            var ctx = new OrderContext(contextMock.Object, location);
            var authzs = await ctx.Authorizations();
            Assert.Equal(order.Authorizations, authzs.Select(a => a.Location));

            // check the context returns empty list instead of null
            httpClientMock
                .Setup(m => m.Post<Order>(location, It.IsAny<JwsPayload>()))
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, new Order(), default, default));
            authzs = await ctx.Authorizations();
            Assert.Empty(authzs);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, 1)]
        [InlineData(5, 5)]
        [InlineData(15, 10)]
        public void RetryDelayIsClamped(int retryAfter, int expectedDelay)
        {
            Assert.Equal(expectedDelay, OrderContext.GetRetryDelaySeconds(retryAfter));
        }

        [Fact]
        public async Task ResourceUpdatesRetryAfter()
        {
            SetupCommonContext();
            var order = new Order();

            httpClientMock
                .Setup(m => m.Post<Order>(location, It.IsAny<JwsPayload>()))
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, order, default, default, retryAfter: 7));

            var ctx = new OrderContext(contextMock.Object, location);
            var actual = await ctx.Resource();

            Assert.Same(order, actual);
            Assert.Equal(7, ctx.RetryAfter);
        }

        [Fact]
        public async Task DownloadRetriesOrderUntilCertificateUriIsAvailable()
        {
            SetupCommonContext();

            var certUri = new Uri("http://acme.d/cert/101");
            var pem = File.ReadAllText("./Data/leaf-cert.pem").Trim();
            var ctx = new TestOrderContext(contextMock.Object, location);

            httpClientMock
                .SetupSequence(m => m.Post<Order>(location, It.IsAny<JwsPayload>()))
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, new Order(), default, default, retryAfter: 0))
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, new Order { Certificate = certUri }, default, default, retryAfter: 0));
            httpClientMock
                .Setup(m => m.Post<string>(certUri, It.IsAny<JwsPayload>()))
                .ReturnsAsync(new AcmeHttpResponse<string>(certUri, pem, default, default));

            var cert = await ctx.Download(numRetries: 1);

            Assert.NotNull(cert.Certificate);
            Assert.Empty(cert.Issuers);
            Assert.Single(ctx.DelayArguments);
            Assert.Equal(0, ctx.DelayArguments[0]);
            httpClientMock.Verify(m => m.Post<Order>(location, It.IsAny<JwsPayload>()), Times.Exactly(2));
            httpClientMock.Verify(m => m.Post<string>(certUri, It.IsAny<JwsPayload>()), Times.Once);
        }

        [Fact]
        public async Task DownloadFailsWhenCertificateUriIsStillMissingAfterRetries()
        {
            SetupCommonContext();

            var ctx = new TestOrderContext(contextMock.Object, location);

            httpClientMock
                .Setup(m => m.Post<Order>(location, It.IsAny<JwsPayload>()))
                .ReturnsAsync(new AcmeHttpResponse<Order>(location, new Order(), default, default, retryAfter: 12));

            var ex = await Assert.ThrowsAsync<AcmeException>(() => ctx.Download(numRetries: 0));

            Assert.Contains(location.ToString(), ex.Message);
            Assert.Empty(ctx.DelayArguments);
            httpClientMock.Verify(m => m.Post<Order>(location, It.IsAny<JwsPayload>()), Times.Once);
        }

        private void SetupCommonContext()
        {
            contextMock.Reset();
            httpClientMock.Reset();

            contextMock
                .SetupGet(c => c.BadNonceRetryCount)
                .Returns(1);
            contextMock
                .SetupGet(c => c.HttpClient)
                .Returns(httpClientMock.Object);
            contextMock
                .Setup(c => c.Sign(It.IsAny<object>(), It.IsAny<Uri>()))
                .ReturnsAsync(new JwsPayload
                {
                    Payload = "payload",
                    Protected = "protected",
                    Signature = "signature"
                });
        }

        private sealed class TestOrderContext : OrderContext
        {
            public TestOrderContext(IAcmeContext context, Uri location)
                : base(context, location)
            {
            }

            public IList<int> DelayArguments { get; } = new List<int>();

            protected override Task DelayBeforeRetry(int retryAfterSeconds)
            {
                DelayArguments.Add(retryAfterSeconds);
                return Task.CompletedTask;
            }
        }
    }
}
