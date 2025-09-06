using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Amazon
{
    // Data structures for Authentication
    public class AmazonToken
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonIgnore]
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;

        [JsonIgnore]
        public bool IsExpired => CreatedAt.AddSeconds(ExpiresIn - 60) < System.DateTime.Now;
    }

    // Data structures for Game Library (Entitlements)
    public class Entitlement
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("product")]
        public Product Product { get; set; }
    }

    public class Product
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("title")]
        public string Title { get; set; }
    }

    public class EntitlementsRequest
    {
        [JsonProperty("Operation")]
        public string Operation { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("syncPoint")]
        public int SyncPoint { get; set; }

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("keyId")]
        public string KeyId { get; set; }

        [JsonProperty("hardwareHash")]
        public string HardwareHash { get; set; }

        [JsonProperty("disableStateFilter")]
        public bool DisableStateFilter { get; set; }

        [JsonProperty("nextToken")]
        public string NextToken { get; set; }
    }

    public class EntitlementsResponse
    {
        [JsonProperty("entitlements")]
        public List<Entitlement> Entitlements { get; set; }
        [JsonProperty("nextToken")]
        public string NextToken { get; set; }
    }

    // Final generic game object
    public class AmazonOwnedGame
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }

    // Data structure for GameInstallInfo.sqlite
    public class InstallGameInfo
    {
        public string Id { get; set; }
        public string ProductTitle { get; set; }
        public string InstallDirectory { get; set; }
        public bool Installed { get; set; }
    }
}
