using Newtonsoft.Json;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Epic
{
    public class GraphQLResponse
    {
        [JsonProperty("data")]
        public ResponseData Data { get; set; }
    }

    public class ResponseData
    {
        [JsonProperty("Catalog")]
        public Catalog Catalog { get; set; }
    }

    public class Catalog
    {
        [JsonProperty("catalogItem")]
        public CatalogItemDetails CatalogItem { get; set; }
    }

    public class CatalogItemDetails
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("releaseDate")]
        public string ReleaseDate { get; set; }

        [JsonProperty("keyImages")]
        public List<KeyImage> KeyImages { get; set; }

        [JsonProperty("customAttributes")]
        public List<CustomAttribute> CustomAttributes { get; set; }
    }

    public class KeyImage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class CustomAttribute
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
