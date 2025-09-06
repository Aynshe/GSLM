using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.HfsPlay
{
    public class HfsPlayGameResult
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("system")]
        public string System { get; set; }
    }

    public class HfsPlayGameResultList
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("results")]
        public List<HfsPlayGameResult> Results { get; set; }
    }

    public class HfsPlaySearchResults
    {
        [JsonProperty("games")]
        public HfsPlayGameResultList Games { get; set; }
    }

    public class HfsPlaySearchResult
    {
        [JsonProperty("search")]
        public string Search { get; set; }

        [JsonProperty("results")]
        public HfsPlaySearchResults Results { get; set; }
    }

    public class GameDetails
    {
        public string Name { get; set; } // For overriding the display name
        public string Description { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public string ReleaseDate { get; set; }
        public Dictionary<string, string> MediaUrls { get; set; }
    }
}
