using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Gog
{
    public class GogToken
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonIgnore]
        public bool IsExpired => CreatedAt.AddSeconds(ExpiresIn - 60) < System.DateTime.Now;

        [JsonProperty("created_at")]
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
    }

    // Used as the final, generic return type from the API part of the library
    public class GogOwnedGame
    {
        public string Title { get; set; }
        public long Id { get; set; }
    }

    // Data structure for https://embed.gog.com/user/data/games
    public class GogOwnedIdsResponse
    {
        [JsonProperty("owned")]
        public List<long> Owned { get; set; }
    }

    // Data structure for https://embed.gog.com/account/gameDetails/{id}.json
    public class GogGameDetails
    {
        [JsonProperty("title")]
        public string Title { get; set; }
    }

    public class GogInstalledGame
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string Exe { get; set; }
    }
}
