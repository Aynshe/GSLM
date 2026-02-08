using Newtonsoft.Json;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Steam
{
    public class SteamAppDetails
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public SteamAppData Data { get; set; }
    }

    public class SteamAppData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("short_description")]
        public string ShortDescription { get; set; }

        [JsonProperty("detailed_description")]
        public string DetailedDescription { get; set; }

        [JsonProperty("header_image")]
        public string HeaderImage { get; set; }

        [JsonProperty("background")]
        public string Background { get; set; }

        [JsonProperty("release_date")]
        public ReleaseDate ReleaseDate { get; set; }

        [JsonProperty("developers")]
        public List<string> Developers { get; set; }

        [JsonProperty("publishers")]
        public List<string> Publishers { get; set; }

        [JsonProperty("movies")]
        public List<Movie> Movies { get; set; }

        [JsonProperty("screenshots")]
        public List<Screenshot> Screenshots { get; set; }
    }

    public class ReleaseDate
    {
        [JsonProperty("date")]
        public string Date { get; set; }
    }

    public class Movie
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonProperty("webm")]
        public WebMFiles Webm { get; set; }

        [JsonProperty("mp4")]
        public Mp4Files Mp4 { get; set; }

        [JsonProperty("dash_h264")]
        public string DashH264 { get; set; }

        [JsonProperty("dash_av1")]
        public string DashAv1 { get; set; }

        [JsonProperty("hls_h264")]
        public string HlsH264 { get; set; }
    }

    public class WebMFiles
    {
        [JsonProperty("480")]
        public string Low { get; set; }

        [JsonProperty("max")]
        public string High { get; set; }
    }

    public class Mp4Files
    {
        [JsonProperty("480")]
        public string Low { get; set; }

        [JsonProperty("max")]
        public string High { get; set; }
    }

    public class Screenshot
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("path_thumbnail")]
        public string PathThumbnail { get; set; }

        [JsonProperty("path_full")]
        public string PathFull { get; set; }
    }

    public class SteamSearchResult
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("items")]
        public List<SteamSearchItem> Items { get; set; }
    }

    public class SteamSearchItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
