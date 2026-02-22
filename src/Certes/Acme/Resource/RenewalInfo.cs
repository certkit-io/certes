using System;
using Newtonsoft.Json;

namespace Certes.Acme.Resource
{
    /// <summary>
    /// Represents the ACME Renewal Information (ARI) resource per RFC 9773.
    /// </summary>
    public class RenewalInfo
    {
        /// <summary>
        /// Gets or sets the suggested renewal window.
        /// </summary>
        [JsonProperty("suggestedWindow")]
        public RenewalWindow SuggestedWindow { get; set; }

        /// <summary>
        /// Gets or sets the explanation URL.
        /// </summary>
        [JsonProperty("explanationURL")]
        public string ExplanationUrl { get; set; }
    }

    /// <summary>
    /// Represents the suggested renewal window within a <see cref="RenewalInfo"/> resource.
    /// </summary>
    public class RenewalWindow
    {
        /// <summary>
        /// Gets or sets the start of the renewal window.
        /// </summary>
        [JsonProperty("start")]
        public DateTimeOffset Start { get; set; }

        /// <summary>
        /// Gets or sets the end of the renewal window.
        /// </summary>
        [JsonProperty("end")]
        public DateTimeOffset End { get; set; }
    }
}
