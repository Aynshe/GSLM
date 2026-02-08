using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameStoreLibraryManager.SteamGridDb
{
    public class SteamGridDbScraper
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private string _apiKey;
        private const string BaseUrl = "https://www.steamgriddb.com/api/v2";

        public SteamGridDbScraper(Config config)
        {
            _config = config;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GSLM/1.0");
            LoadApiKey();
        }

        private void LoadApiKey()
        {
            _apiKey = _config.GetString("steamgriddb_api_key", "");
            
            // If empty in config, try reading from file (automated auth target)
            if (string.IsNullOrEmpty(_apiKey))
            {
                string apiKeyPath = Path.Combine(PathManager.ApiKeyPath, "steamgriddb.apikey");
                if (File.Exists(apiKeyPath))
                {
                    _apiKey = SecureStore.ReadString(apiKeyPath)?.Trim();
                    
                    // Migrate to protected if enabled
                    bool protect = _config.GetBoolean("enable_dpapi_protection", false);
                    if (protect && !SecureStore.IsProtectedFile(apiKeyPath) && !string.IsNullOrEmpty(_apiKey))
                    {
                        SecureStore.WriteString(apiKeyPath, _apiKey, true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        public async Task<string> FindGameIdAsync(string gameName, string steamAppId, SimpleLogger logger)
        {
            if (string.IsNullOrEmpty(_apiKey)) 
            {
                LoadApiKey(); // Try reloading in case it was just generated
                if (string.IsNullOrEmpty(_apiKey)) return null;
            }

            try
            {
                // Try Steam AppID first if available
                if (!string.IsNullOrEmpty(steamAppId))
                {
                    logger.Log($"  [SGDB Scraper] Searching by Steam AppID: {steamAppId}");
                    var response = await _httpClient.GetAsync($"{BaseUrl}/games/steam/{steamAppId}");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var data = JObject.Parse(json);
                        if (data["success"]?.Value<bool>() == true)
                        {
                            return data["data"]?["id"]?.ToString();
                        }
                    }
                }

                // Fallback to name search
                logger.Log($"  [SGDB Scraper] Searching by name: {gameName}");
                var searchResponse = await _httpClient.GetAsync($"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(gameName)}");
                if (searchResponse.IsSuccessStatusCode)
                {
                    var json = await searchResponse.Content.ReadAsStringAsync();
                    var data = JObject.Parse(json);
                    if (data["success"]?.Value<bool>() == true)
                    {
                        var games = data["data"] as JArray;
                        if (games != null && games.Count > 0)
                        {
                            // Best match logic (simple first result or improved matching)
                            var bestMatch = games[0];
                            logger.Log($"  [SGDB Scraper] Found match: {bestMatch["name"]} (ID: {bestMatch["id"]})");
                            return bestMatch["id"]?.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"  [SGDB Scraper] Error finding game: {ex.Message}");
            }

            return null;
        }

        public async Task<GameDetails> GetGameDetailsAsync(string gameId, SimpleLogger logger)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(_apiKey)) return null;

            var details = new GameDetails { MediaUrls = new Dictionary<string, string>() };

            try
            {
                // Fetch Grids (Covers)
                var grids = await FetchBestMedia(gameId, "grids", logger);
                if (!string.IsNullOrEmpty(grids)) details.MediaUrls["image"] = grids;

                // Fetch Heroes (Fanarts)
                var heroes = await FetchBestMedia(gameId, "heroes", logger);
                if (!string.IsNullOrEmpty(heroes)) details.MediaUrls["fanart"] = heroes;

                // Fetch Logos (Marquees)
                var logos = await FetchBestMedia(gameId, "logos", logger);
                if (!string.IsNullOrEmpty(logos)) details.MediaUrls["marquee"] = logos;
            }
            catch (Exception ex)
            {
                logger.Log($"  [SGDB Scraper] Error fetching assets: {ex.Message}");
            }

            return details.MediaUrls.Any() ? details : null;
        }

        private async Task<string> FetchBestMedia(string gameId, string type, SimpleLogger logger)
        {
            // type can be "grids", "heroes", "logos", "icons"
            var response = await _httpClient.GetAsync($"{BaseUrl}/{type}/game/{gameId}?nsfw=false&humor=false");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                if (data["success"]?.Value<bool>() == true)
                {
                    var items = data["data"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        // Priority: preferred styles? Or just highest upvoted (usually first)
                        // For logos, we might prefer "official" or "white"
                        // For now, take the first one
                        return items[0]["url"]?.ToString();
                    }
                }
            }
            return null;
        }
    }
}
