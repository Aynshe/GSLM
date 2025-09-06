using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Epic
{
    [DataContract]
    public class LauncherInstalled
    {
        [DataContract]
        public class InstalledApp
        {
            [DataMember] public string InstallLocation { get; set; }
            [DataMember] public string AppName { get; set; }
            [DataMember] public long AppID { get; set; }
            [DataMember] public string AppVersion { get; set; }
        }
        [DataMember] public List<InstalledApp> InstallationList { get; set; }
    }

    [DataContract]
    public class EpicGame
    {
        [DataMember] public string AppName { get; set; }
        [DataMember] public string CatalogNamespace { get; set; }
        [DataMember] public string LaunchExecutable { get; set; }
        [DataMember] public string InstallLocation;
        [DataMember] public string MainGameAppName;
        [DataMember] public string DisplayName;
        [DataMember] public List<string> AppCategories { get; set; }
    }

    public class EpicToken
    {
        [JsonProperty("access_token")] public string AccessToken { get; set; }
        [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
        [JsonProperty("expires_at")] public DateTime ExpiresAt { get; set; }
        [JsonProperty("token_type")] public string TokenType { get; set; }
        [JsonProperty("refresh_token")] public string RefreshToken { get; set; }
        [JsonProperty("refresh_expires")] public int RefreshExpires { get; set; }
        [JsonProperty("refresh_expires_at")] public DateTime RefreshExpiresAt { get; set; }
        [JsonProperty("account_id")] public string AccountId { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("internal_client")] public bool InternalClient { get; set; }
        [JsonProperty("client_service")] public string ClientService { get; set; }
    }

    public class EpicLibraryItem
    {
        [JsonProperty("appName")] public string AppName { get; set; }
        [JsonProperty("catalogItemId")] public string CatalogItemId { get; set; }
        [JsonProperty("namespace")] public string Namespace { get; set; }
        [JsonProperty("metadata")] public EpicGameMetadata Metadata { get; set; }
    }

    public class EpicGameMetadata
    {
        [JsonProperty("displayName")] public string DisplayName { get; set; }
    }

    public class Asset
    {
        [JsonProperty("appName")]
        public string appName { get; set; }

        [JsonProperty("catalogItemId")]
        public string catalogItemId { get; set; }

        [JsonProperty("namespace")]
        public string @namespace { get; set; }

        [JsonProperty("buildVersion")]
        public string buildVersion { get; set; }
    }

    public class CatalogItem
    {
        [JsonProperty("title")]
        public string title { get; set; }

        [JsonProperty("categories")]
        public List<Category> categories { get; set; }

        [JsonProperty("mainGameItem")]
        public MainGameItem mainGameItem { get; set; }
    }

    public class Category
    {
        [JsonProperty("path")]
        public string path { get; set; }
    }

    public class MainGameItem
    {
        [JsonProperty("id")]
        public string id { get; set; }
    }
}
