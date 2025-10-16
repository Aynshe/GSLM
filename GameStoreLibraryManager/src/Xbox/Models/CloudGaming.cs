using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class CloudGamingTitle
    {
        [JsonProperty("titleId")]
        public string TitleId { get; set; }

        [JsonProperty("details")]
        public CloudGamingTitleDetails Details { get; set; }
    }

    public class CloudGamingTitleDetails
    {
        [JsonProperty("productId")]
        public string ProductId { get; set; }

        [JsonProperty("productFamilyName")]
        public string ProductFamilyName { get; set; }
    }

    public class CloudGamingResponse
    {
        [JsonProperty("results")]
        public List<CloudGamingTitle> Results { get; set; }
    }
}