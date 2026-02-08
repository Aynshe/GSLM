using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using GameStoreLibraryManager.Common;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameStoreLibraryManager.Gog
{
    public class GogScraper
    {
        private readonly HttpClient _httpClient;
        private readonly SimpleLogger _logger;

        public GogScraper(SimpleLogger logger)
        {
            _httpClient = new HttpClient();
            _logger = logger;
        }

        public async Task<string> SearchGameIdAsync(string gameName)
        {
            try
            {
                var formattedGameName = HttpUtility.UrlEncode(gameName);
                // Use official GOG catalog API for search
                var searchUrl = $"https://catalog.gog.com/v1/catalog?search={formattedGameName}&limit=5";

                _logger.Log($"[GOG Scraper] Searching for '{gameName}' on GOG Catalog: {searchUrl}");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var response = await _httpClient.GetStringAsync(searchUrl);
                var searchJson = JObject.Parse(response);
                var products = searchJson["products"] as JArray;

                if (products == null || !products.Any())
                {
                    _logger.Log($"[GOG Scraper] No results found for '{gameName}'.");
                    return null;
                }

                foreach (var product in products)
                {
                    var name = product["title"]?.ToString();
                    var id = product["id"]?.ToString();
                    var type = product["productType"]?.ToString();

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                    {
                        // Prioritize exact match and "game" type
                        if (name.Equals(gameName, StringComparison.OrdinalIgnoreCase) && 
                            (string.IsNullOrEmpty(type) || type.Equals("game", StringComparison.OrdinalIgnoreCase) || type.Equals("Game", StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.Log($"[GOG Scraper] Found exact match: '{name}' with ID '{id}'.");
                            return id;
                        }
                    }
                }

                // Fallback to best fuzzy match if any
                var bestMatch = products
                    .Select(p => new { Product = p, Name = p["title"]?.ToString(), Id = p["id"]?.ToString(), Distance = LevenshteinDistance(gameName.ToLower(), p["title"]?.ToString()?.ToLower() ?? "") })
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (bestMatch != null && bestMatch.Distance < 5)
                {
                    _logger.Log($"[GOG Scraper] Found fuzzy match for '{gameName}': '{bestMatch.Name}' (ID: {bestMatch.Id}, Distance: {bestMatch.Distance})");
                    return bestMatch.Id;
                }

                _logger.Log($"[GOG Scraper] No suitable match found for '{gameName}'.");
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG Scraper] Error searching for game ID: {ex.Message}");
            }

            return null;
        }

        public int LevenshteinDistance(string s, string t)
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
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        public async Task<GameDetails> GetGameDetailsAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;

            try
            {
                _logger.Log($"[GOG Scraper] Fetching details for GOG ID: {gameId}");
                var gameDetails = new GameDetails { MediaUrls = new Dictionary<string, string>() };

                // Fetch description and thumb
                var detailsUrl = $"https://api.gog.com/v2/games/{gameId}";
                var detailsResponse = await _httpClient.GetStringAsync(detailsUrl);
                var detailsJson = JObject.Parse(detailsResponse);

                var thumbUrl = detailsJson["_links"]?["boxArtImage"]?["href"]?.ToString();
                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    gameDetails.MediaUrls["thumb"] = thumbUrl;
                }

                // Fetch description and developer/publisher from products API (more reliable for some fields)
                var productsUrl = $"https://api.gog.com/products/{gameId}?expand=screenshots,videos";
                var productsResponse = await _httpClient.GetStringAsync(productsUrl);
                if (!string.IsNullOrWhiteSpace(productsResponse) && productsResponse != "{}")
                {
                    var productsJson = JObject.Parse(productsResponse);
                    
                    // Description often in HTML, but we want plain text if possible
                    var description = productsJson["description"]?.ToString();
                    if (!string.IsNullOrEmpty(description))
                    {
                        // Strip HTML tags for simple description
                        var cleanDescription = Regex.Replace(description, "<.*?>", string.Empty);
                        var firstLine = Regex.Match(cleanDescription, @"^([^.\n]*)").Groups[1].Value.Trim();
                        gameDetails.Description = HttpUtility.HtmlDecode(firstLine);
                    }

                    gameDetails.Developer = productsJson["developers"]?.FirstOrDefault()?["name"]?.ToString();
                    gameDetails.Publisher = productsJson["publisher"]?.ToString();

                    var screenshotNode = productsJson["screenshots"]?.FirstOrDefault();
                    if (screenshotNode != null)
                    {
                        var imageId = screenshotNode["image_id"]?.ToString();
                        if (!string.IsNullOrEmpty(imageId) && !gameDetails.MediaUrls.ContainsKey("image"))
                        {
                            gameDetails.MediaUrls["image"] = $"https://images.gog-statics.com/{imageId}.jpg";
                        }
                    }

                    // Attempt to extract video (GOG usually returns YouTube IDs)
                    var videoNode = productsJson["videos"]?.FirstOrDefault();
                    if (videoNode != null)
                    {
                        var videoId = videoNode["video_id"]?.ToString();
                        var provider = videoNode["provider"]?.ToString()?.ToLower();

                        if (!string.IsNullOrEmpty(videoId))
                        {
                            if (provider == "youtube")
                            {
                                // While not a direct MP4, it's the official source GOG uses.
                                // Our MediaDownloader will need to handle this or we can try to find a direct source.
                                gameDetails.MediaUrls["video"] = $"https://www.youtube.com/watch?v={videoId}";
                                _logger.Log($"[GOG Scraper] Found official video (YouTube): {videoId}");
                            }
                        }
                    }
                }

                if (gameDetails.MediaUrls.Count == 0 && string.IsNullOrEmpty(gameDetails.Description))
                {
                    _logger.Log($"[GOG Scraper] No media or description found for GOG ID: {gameId}");
                    return null;
                }

                return gameDetails;
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG Scraper] Error fetching game details for ID '{gameId}': {ex.Message}");
                return null;
            }
        }
    }
}