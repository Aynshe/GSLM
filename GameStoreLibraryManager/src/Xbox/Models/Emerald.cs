using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class EmeraldResponse
    {
        [JsonProperty("productSummaries")]
        public List<EmeraldProductSummary> ProductSummaries { get; set; }
    }

    public class EmeraldProductSummary
    {
        [JsonProperty("productId")]
        public string ProductId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("xCloudPropertiesV2")]
        public EmeraldXCloudProperties XCloudProperties { get; set; }

        [JsonProperty("availableOn")]
        public List<string> AvailableOn { get; set; }

        [JsonProperty("attributes")]
        public List<ProductAttribute> Attributes { get; set; }
    }

    public class EmeraldXCloudProperties
    {
        [JsonProperty("programs")]
        public List<string> Programs { get; set; }
    }
}