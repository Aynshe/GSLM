using System.Threading.Tasks;
using GameStoreLibraryManager.HfsPlay;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using GameStoreLibraryManager.Common;
using System;
using GameDetails = GameStoreLibraryManager.Common.GameDetails;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;

namespace GameStoreLibraryManager.Steam
{
    public class SteamStoreScraper
    {
        private readonly HttpClient _httpClient;
        private const string DetailsApiUrl = "https://store.steampowered.com/api/appdetails?l=english&cc=us&appids=";
        private const string SearchApiUrl = "https://store.steampowered.com/api/storesearch/?term={0}&l=english&cc=us";

        public SteamStoreScraper()
        {
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            // Bypass Steam age gate for Mature content
            var baseUri = new Uri("https://store.steampowered.com");
            handler.CookieContainer.Add(baseUri, new Cookie("wants_mature_content", "1") { Domain = "store.steampowered.com" });
            handler.CookieContainer.Add(baseUri, new Cookie("birthtime", "189345601") { Domain = "store.steampowered.com" });
            handler.CookieContainer.Add(baseUri, new Cookie("lastagecheckage", "1-January-1900") { Domain = "store.steampowered.com" });
        }


        public async Task<string> FindGameByName(string gameName, SimpleLogger logger)
        {
            try
            {
                var formattedName = Uri.EscapeDataString(gameName);
                var searchUrl = string.Format(SearchApiUrl, formattedName);
                
                logger.Log($"  [Steam Scraper] Searching for '{gameName}' on Steam Store...");
                var response = await _httpClient.GetStringAsync(searchUrl);
                var searchResult = JsonConvert.DeserializeObject<SteamSearchResult>(response);

                if (searchResult != null && searchResult.Total > 0 && searchResult.Items != null)
                {
                    // Find the best match among results (usually the first one is best)
                    var bestMatch = searchResult.Items
                        .Select(item => new { Item = item, Distance = LevenshteinDistance(gameName.ToLower(), item.Name.ToLower()) })
                        .OrderBy(x => x.Distance)
                        .FirstOrDefault();

                    if (bestMatch != null && bestMatch.Distance < 10) // Relaxed distance slightly due to targeted search
                    {
                        logger.Log($"  [Steam Scraper] Found match for '{gameName}': '{bestMatch.Item.Name}' (AppID: {bestMatch.Item.Id})");
                        return bestMatch.Item.Id.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"  [Steam Scraper] Search failed for '{gameName}': {ex.Message}");
            }

        logger.Log($"  [Steam Scraper] No results found for '{gameName}' on Steam.");
            return null;
        }

        private async Task<List<string>> ScrapeSteamStorePageForVideos(string appId, SimpleLogger logger)
        {
            var videos = new List<string>();
            try
            {
                var url = $"https://store.steampowered.com/app/{appId}/?l=english";
                var html = await _httpClient.GetStringAsync(url);
                
                // Optimized regex to find the extra assets map in the HTML
                // Handles both double and single quotes around the attribute
                var regex = new Regex(@"data-store_page_extra_assets_map\s*=\s*[""'](.*?)[""']");
                var match = regex.Match(html);
                if (match.Success)
                {
                    var jsonEncoded = match.Groups[1].Value;
                    var json = WebUtility.HtmlDecode(jsonEncoded);
                    var extraAssets = JObject.Parse(json);
                    
                    foreach (var property in extraAssets.Properties())
                    {
                        var assets = property.Value as JArray;
                        if (assets != null)
                        {
                            foreach (var asset in assets)
                            {
                                var ext = asset["extension"]?.ToString();
                                var urlPart = asset["urlPart"]?.ToString();
                                if (ext == "mp4" && !string.IsNullOrEmpty(urlPart))
                                {
                                    // Base URL for Steam store assets
                                    var fullUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/{urlPart}";
                                    videos.Add(fullUrl);
                                }
                            }
                        }
                    }
                }

                // If still no videos, try a broad regex for any .mp4 URL in the page source 
                // (sometimes trailers are in a different structure)
                if (videos.Count == 0)
                {
                    var mp4Regex = new Regex(@"https://[^""' ]+?\.mp4");
                    var mp4Matches = mp4Regex.Matches(html);
                    foreach (Match m in mp4Matches)
                    {
                        if (m.Value.Contains("/apps/" + appId + "/"))
                        {
                            videos.Add(m.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"  [Steam Scraper] HTML fallback failed for AppID {appId}: {ex.Message}");
            }
            return videos.Distinct().ToList();
        }

        private async Task<string> GetHeaviestVideoUrl(List<string> urls, SimpleLogger logger)
        {
            if (urls == null || urls.Count == 0) return null;
            if (urls.Count == 1) return urls[0];

            logger.Log($"  [Steam Scraper] Comparing {urls.Count} videos to find the best quality/gameplay...");
            string heaviestUrl = urls[0];
            long maxContentLength = -1;

            foreach (var url in urls)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                    {
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            var contentLength = response.Content.Headers.ContentLength ?? 0;
                            if (contentLength > maxContentLength)
                            {
                                maxContentLength = contentLength;
                                heaviestUrl = url;
                            }
                        }
                    }
                }
                catch { }
            }

            if (maxContentLength >= 0)
            {
                string sizeStr = (maxContentLength > 1024 * 1024) 
                    ? $"{(double)maxContentLength / 1024 / 1024:F1} MB" 
                    : $"{(double)maxContentLength / 1024:F0} KB";
                logger.Log($"  [Steam Scraper] Selected heaviest video ({sizeStr}): {heaviestUrl}");
            }

            return heaviestUrl;
        }

        public async Task<GameDetails> GetGameDetails(string appId, SimpleLogger logger)
        {
            try
            {
                logger.Log($"  [Steam Scraper] Fetching details for AppID: {appId}");
                var response = await _httpClient.GetStringAsync(DetailsApiUrl + appId);
                var appDetailsDict = JsonConvert.DeserializeObject<Dictionary<string, SteamAppDetails>>(response);

                GameDetails gameDetails = null;

                if (appDetailsDict != null && appDetailsDict.TryGetValue(appId, out var appDetails))
                {
                    if (appDetails.Success)
                    {
                        var data = appDetails.Data;
                        gameDetails = new GameDetails
                        {
                            Description = data.ShortDescription,
                            Developer = data.Developers?.FirstOrDefault(),
                            Publisher = data.Publishers?.FirstOrDefault(),
                            ReleaseDate = data.ReleaseDate?.Date,
                            MediaUrls = new Dictionary<string, string>()
                        };

                        if (!string.IsNullOrEmpty(data.HeaderImage))
                        {
                            gameDetails.MediaUrls["marquee"] = data.HeaderImage; // Use header as marquee/logo
                        }
                        if (!string.IsNullOrEmpty(data.Background))
                        {
                            gameDetails.MediaUrls["fanart"] = data.Background;
                        }

                        var screenshot = data.Screenshots?.FirstOrDefault();
                        if (screenshot != null)
                        {
                            gameDetails.MediaUrls["image"] = screenshot.PathFull; // Use first screenshot as main image
                        }

                        // Extract official trailers from API if available
                        if (data.Movies != null)
                        {
                            foreach (var video in data.Movies)
                            {
                                // Prioritize MP4 (Max then 480) for best compatibility and quality
                                string videoUrl = video.Mp4?.High ?? video.Mp4?.Low;
                                
                                // Fallback to WebM if MP4 is absolutely missing (rare for modern publisher trailers)
                                if (string.IsNullOrEmpty(videoUrl))
                                {
                                    videoUrl = video.Webm?.High ?? video.Webm?.Low;
                                }

                                if (!string.IsNullOrEmpty(videoUrl))
                                {
                                    gameDetails.MediaUrls["video"] = videoUrl;
                                    logger.Log($"  [Steam Scraper] Found official trailer (API): {videoUrl}");
                                    break; // Take the first available official trailer
                                }
                            }
                        }
                    }
                }

                // HTML Fallback for games without API-exposed movies (like APB Reloaded)
                // Also run if gameDetails is still null (API failed but HTML might work)
                if (gameDetails == null || !gameDetails.MediaUrls.ContainsKey("video"))
                {
                    var htmlVideos = await ScrapeSteamStorePageForVideos(appId, logger);
                    var bestVideo = await GetHeaviestVideoUrl(htmlVideos, logger);
                    if (!string.IsNullOrEmpty(bestVideo))
                    {
                        if (gameDetails == null) gameDetails = new GameDetails { MediaUrls = new Dictionary<string, string>() };
                        gameDetails.MediaUrls["video"] = bestVideo;
                        logger.Log($"  [Steam Scraper] Found official trailer (HTML Fallback): {bestVideo}");
                    }
                }

                return gameDetails;
            }
            catch (Exception ex)
            {
                logger.Log($"  [Steam Scraper] Failed to get details for AppID {appId}: {ex.Message}");
            }

            return null;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}
