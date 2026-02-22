namespace Certes.Acme.Resource
{
    /// <summary>
    /// Wraps a <see cref="RenewalInfo"/> resource together with the Retry-After value
    /// returned by the server.
    /// </summary>
    public class RenewalInfoResponse
    {
        /// <summary>
        /// Gets the renewal information resource.
        /// </summary>
        public RenewalInfo RenewalInfo { get; }

        /// <summary>
        /// Gets the Retry-After value in seconds. Zero if the header was not present.
        /// </summary>
        public int RetryAfterSeconds { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RenewalInfoResponse"/> class.
        /// </summary>
        /// <param name="renewalInfo">The renewal information resource.</param>
        /// <param name="retryAfterSeconds">The Retry-After value in seconds.</param>
        public RenewalInfoResponse(RenewalInfo renewalInfo, int retryAfterSeconds)
        {
            RenewalInfo = renewalInfo;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }
}
