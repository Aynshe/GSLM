using System.Threading.Tasks;
using GameStoreLibraryManager.HfsPlay;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using GameStoreLibraryManager.Common;
using System;
using System.IO;

namespace GameStoreLibraryManager.Steam
{
    public class SteamStoreScraper
    {
        private readonly HttpClient _httpClient;
        private const string DetailsApiUrl = "https://store.steampowered.com/api/appdetails?appids=";
        private const string AppListApiUrl = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";
        private static List<SteamApp> _steamAppList;

        public SteamStoreScraper()
        {
            _httpClient = new HttpClient();
        }

        private async Task<List<SteamApp>> GetAppList(SimpleLogger logger)
        {
            if (_steamAppList != null)
            {
                return _steamAppList;
            }

            var cacheFile = Path.Combine(PathManager.CachePath, "steam_app_list.json");
            if (File.Exists(cacheFile) && (DateTime.UtcNow - new FileInfo(cacheFile).LastWriteTimeUtc).TotalHours < 24)
            {
                var json = await File.ReadAllTextAsync(cacheFile);
                _steamAppList = JsonConvert.DeserializeObject<AppListResponse>(json)?.AppList?.Apps;
                if (_steamAppList != null) return _steamAppList;
            }

            logger.Log("  [Steam Scraper] Downloading full Steam app list...");
            var response = await _httpClient.GetStringAsync(AppListApiUrl);
            await File.WriteAllTextAsync(cacheFile, response);
            _steamAppList = JsonConvert.DeserializeObject<AppListResponse>(response)?.AppList?.Apps;
            return _steamAppList;
        }

        public async Task<string> FindGameByName(string gameName, SimpleLogger logger)
        {
            var appList = await GetAppList(logger);
            if (appList == null)
            {
                logger.Log("  [Steam Scraper] ERROR: Could not retrieve Steam app list.");
                return null;
            }

            // Find the best match using Levenshtein distance
            var bestMatch = appList
                .Select(app => new { App = app, Distance = LevenshteinDistance(gameName.ToLower(), app.Name.ToLower()) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            // Only accept the match if it's very close
            if (bestMatch != null && bestMatch.Distance < 5)
            {
                logger.Log($"  [Steam Scraper] Found potential match for '{gameName}' on Steam: '{bestMatch.App.Name}' (AppID: {bestMatch.App.AppId})");
                return bestMatch.App.AppId.ToString();
            }

            logger.Log($"  [Steam Scraper] No close match found for '{gameName}' on Steam.");
            return null;
        }

        public async Task<GameDetails> GetGameDetails(string appId)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(DetailsApiUrl + appId);
                var appDetailsDict = JsonConvert.DeserializeObject<Dictionary<string, SteamAppDetails>>(response);

                if (appDetailsDict != null && appDetailsDict.TryGetValue(appId, out var appDetails) && appDetails.Success)
                {
                    var data = appDetails.Data;
                    var gameDetails = new GameDetails
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

                    var video = data.Movies?.FirstOrDefault();
                    if (video != null)
                    {
                        var videoUrl = !string.IsNullOrEmpty(video.Mp4.High) ? video.Mp4.High : video.Mp4.Low;
                        if (!string.IsNullOrEmpty(videoUrl))
                        {
                            gameDetails.MediaUrls["video"] = videoUrl;
                        }
                    }

                    return gameDetails;
                }
            }
            catch
            {
                // Ignore errors, just return null if scraping fails
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
