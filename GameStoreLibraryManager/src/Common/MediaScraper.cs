using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameStoreLibraryManager.Gog;
using GameStoreLibraryManager.HfsPlay;
using GameStoreLibraryManager.Steam;

namespace GameStoreLibraryManager.Common
{
    public class MediaScraper
    {
        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly HfsPlayScraper _hfsScraper;
        private readonly SteamStoreScraper _steamScraper;
        private readonly GogScraper _gogScraper;
        private readonly SteamGridDb.SteamGridDbScraper _sgdbScraper;
        private readonly MediaDownloader _downloader;

        public MediaScraper(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
            _hfsScraper = new HfsPlayScraper();
            _steamScraper = new SteamStoreScraper();
            _gogScraper = new GogScraper(logger);
            _sgdbScraper = new SteamGridDb.SteamGridDbScraper(config);
            _downloader = new MediaDownloader(logger);
        }

        public async Task<GameDetails> ScrapeGameAsync(LauncherGameInfo game, string romsPath)
        {
            _logger.Log($"  Processing '{game.Name}' for media...");
            try
            {
                var gameDetails = new GameDetails { MediaUrls = new Dictionary<string, string>() };
                var allMediaTypes = new[] { "marquee", "image", "fanart", "video", "thumb" };

                var hfsPlayMedia = _config.GetString("hfsplay_scraper_media_types", "").Split(',').ToList();
                var steamMedia = _config.GetString("steam_scraper_media_types", "").Split(',').ToList();
                var gogMedia = _config.GetString("gog_scraper_media_types", "").Split(',').ToList();
                var sgdbMedia = _config.GetString("steamgriddb_scraper_media_types", "").Split(',').ToList();

                GameDetails hfsDetails = null;
                GameDetails steamDetails = null;
                GameDetails gogDetails = null;
                GameDetails sgdbDetails = null;
                string steamAppId = null;

                // Scrape from primary sources based on config
                if (steamMedia.Any())
                {
                    steamAppId = await _steamScraper.FindGameByName(game.Name, _logger);
                    if (!string.IsNullOrEmpty(steamAppId))
                    {
                        steamDetails = await _steamScraper.GetGameDetails(steamAppId, _logger);
                        if (steamDetails != null)
                        {
                            if (string.IsNullOrEmpty(gameDetails.Description)) gameDetails.Description = steamDetails.Description;
                            foreach (var mediaType in steamMedia)
                            {
                                if (steamDetails.MediaUrls.TryGetValue(mediaType, out var url))
                                {
                                    gameDetails.MediaUrls[mediaType] = url;
                                }
                            }
                        }
                    }
                }
                if (sgdbMedia.Any())
                {
                    // Check if we need to trigger automated auth
                    if (string.IsNullOrEmpty(_config.GetString("steamgriddb_api_key", "")) && 
                        _config.GetBoolean("steamgriddb_enable_token_generation", false))
                    {
                        string apiKeyPath = Path.Combine(PathManager.ApiKeyPath, "steamgriddb.apikey");
                        if (!File.Exists(apiKeyPath))
                        {
                            _logger.Log("  [MediaScraper] SteamGridDB API key missing. Launching automated auth...");
                            Auth.AuthUiLauncher.Run("steamgriddb");
                            // Scraper will reload it automatically on next call
                        }
                    }

                    // For SGDB, we try Steam AppID first (fetched above if steam scraper ran)
                    if (string.IsNullOrEmpty(steamAppId) && steamMedia.Any())
                    {
                        // Already tried and failed if we got here
                    }
                    else if (string.IsNullOrEmpty(steamAppId))
                    {
                        // Steam scraper didn't run, check if we can get AppID anyway for SGDB
                        steamAppId = await _steamScraper.FindGameByName(game.Name, _logger);
                    }

                    var sgdbGameId = await _sgdbScraper.FindGameIdAsync(game.Name, steamAppId, _logger);
                    if (!string.IsNullOrEmpty(sgdbGameId))
                    {
                        sgdbDetails = await _sgdbScraper.GetGameDetailsAsync(sgdbGameId, _logger);
                        if (sgdbDetails != null)
                        {
                            foreach (var mediaType in sgdbMedia)
                            {
                                if (sgdbDetails.MediaUrls.TryGetValue(mediaType, out var url))
                                {
                                    // SGDB has priority for the user
                                    gameDetails.MediaUrls[mediaType] = url;
                                }
                            }
                        }
                    }
                }

                if (gogMedia.Any())
                {
                    var gogId = await _gogScraper.SearchGameIdAsync(game.Name);
                    if (!string.IsNullOrEmpty(gogId))
                    {
                        gogDetails = await _gogScraper.GetGameDetailsAsync(gogId);
                        if (gogDetails != null)
                        {
                            if (string.IsNullOrEmpty(gameDetails.Description)) gameDetails.Description = gogDetails.Description;
                            foreach (var mediaType in gogMedia)
                            {
                                if (gogDetails.MediaUrls.TryGetValue(mediaType, out var url) && !gameDetails.MediaUrls.ContainsKey(mediaType))
                                {
                                    gameDetails.MediaUrls[mediaType] = url;
                                }
                            }
                        }
                    }
                }
                if (hfsPlayMedia.Any())
                {
                    hfsDetails = await ScrapeHfsPlay(game, _hfsScraper, _logger);
                    if (hfsDetails != null)
                    {
                        if (string.IsNullOrEmpty(gameDetails.Description)) gameDetails.Description = hfsDetails.Description;
                        foreach (var mediaType in hfsPlayMedia)
                        {
                            if (hfsDetails.MediaUrls.TryGetValue(mediaType, out var url) && !gameDetails.MediaUrls.ContainsKey(mediaType))
                            {
                                gameDetails.MediaUrls[mediaType] = url;
                            }
                        }
                    }
                }

                // Fallback scraping for any missing media
                async Task<GameDetails> GetSteamDetails()
                {
                    if (steamDetails == null)
                    {
                        var steamAppId = await _steamScraper.FindGameByName(game.Name, _logger);
                        if (!string.IsNullOrEmpty(steamAppId)) steamDetails = await _steamScraper.GetGameDetails(steamAppId, _logger);
                    }
                    return steamDetails;
                }
                async Task<GameDetails> GetGogDetails()
                {
                    if (gogDetails == null)
                    {
                        var gogId = await _gogScraper.SearchGameIdAsync(game.Name);
                        if (!string.IsNullOrEmpty(gogId)) gogDetails = await _gogScraper.GetGameDetailsAsync(gogId);
                    }
                    return gogDetails;
                }
                async Task<GameDetails> GetHfsDetails()
                {
                    if (hfsDetails == null) hfsDetails = await ScrapeHfsPlay(game, _hfsScraper, _logger);
                    return hfsDetails;
                }
                async Task<GameDetails> GetSgdbDetails()
                {
                    if (sgdbDetails == null)
                    {
                        var sId = await _steamScraper.FindGameByName(game.Name, _logger);
                        var sgdbId = await _sgdbScraper.FindGameIdAsync(game.Name, sId, _logger);
                        if (!string.IsNullOrEmpty(sgdbId)) sgdbDetails = await _sgdbScraper.GetGameDetailsAsync(sgdbId, _logger);
                    }
                    return sgdbDetails;
                }
                var fallbackOrder = new Func<Task<GameDetails>>[] { GetSgdbDetails, GetSteamDetails, GetGogDetails, GetHfsDetails };

                foreach (var mediaType in allMediaTypes)
                {
                    if (gameDetails.MediaUrls.ContainsKey(mediaType)) continue;

                    foreach (var fallbackScraperFunc in fallbackOrder)
                    {
                        var details = await fallbackScraperFunc();
                        if (details?.MediaUrls.TryGetValue(mediaType, out var url) == true)
                        {
                            gameDetails.MediaUrls[mediaType] = url;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(gameDetails.Description))
                {
                    foreach (var fallbackScraperFunc in fallbackOrder)
                    {
                         var details = await fallbackScraperFunc();
                         if (!string.IsNullOrEmpty(details?.Description))
                         {
                             gameDetails.Description = details.Description;
                             break;
                         }
                    }
                }

                if (gameDetails.MediaUrls.Any() || !string.IsNullOrEmpty(gameDetails.Description))
                {
                    var mediaPaths = new Dictionary<string, string>();
                    foreach (var mediaEntry in gameDetails.MediaUrls)
                    {
                        var mediaType = mediaEntry.Key;
                        var mediaUrl = mediaEntry.Value;
                        var baseFileName = $"{StringUtils.SanitizeFileName(game.Name)}-{mediaType}";
                        var subdirectory = (mediaType == "video") ? "videos" : "images";
                        var baseFilePath = Path.Combine(romsPath, subdirectory, baseFileName);

                        var finalFilePath = await _downloader.DownloadMedia(mediaUrl, baseFilePath);
                        if (!string.IsNullOrEmpty(finalFilePath))
                        {
                            mediaPaths[mediaType] = $"./{subdirectory}/{Path.GetFileName(finalFilePath)}";
                        }
                    }
                    gameDetails.MediaUrls = mediaPaths;
                    _logger.Log($"    Scraped details for {game.Name}");
                    return gameDetails;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Error] Could not scrape or download media for {game.Name}: {ex.Message}");
            }
            return null;
        }

        private async Task<GameDetails> ScrapeHfsPlay(LauncherGameInfo game, HfsPlayScraper scraper, SimpleLogger logger)
        {
            var searchResult = await scraper.SearchGame(game.Name);
            if (searchResult?.Results?.Games?.Results == null) return null;

            var results = searchResult.Results.Games.Results.Where(r => IsModernPlatform(r.System)).ToList();
            logger.Log($"  Found {results.Count} potential modern platform matches on HFSPlay for '{game.Name}'.");

            var gameResult = results.FirstOrDefault(g => StringUtils.NormalizeName(g.Name) == StringUtils.NormalizeName(game.Name) && g.System == "PC - Personal Computer")
                             ?? results.FirstOrDefault(g => StringUtils.NormalizeName(g.Name) == StringUtils.NormalizeName(game.Name));

            if (gameResult == null && results.Any())
            {
                logger.Log($"  No exact match for '{game.Name}'. Finding best fuzzy match...");
                gameResult = results
                    .Select(r => new { Result = r, Distance = LevenshteinDistance(game.Name.ToLower(), r.Name.ToLower()) })
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault()?
                    .Result;
            }

            if (gameResult != null)
            {
                if (LevenshteinDistance(game.Name.ToLower(), gameResult.Name.ToLower()) > 5)
                {
                    logger.Log($"  Found match for '{game.Name}' ('{gameResult.Name}') but it is too different. Discarding.");
                    return null;
                }

                logger.Log($"  Found primary match for '{game.Name}' on HFSPlay: '{gameResult.Name}' on platform '{gameResult.System}'");
                var gameDetails = await scraper.GetGameDetails(gameResult.Id, gameResult.Slug);

                var missingMediaTypes = new List<string> { "fanart", "marquee", "video" };
                missingMediaTypes.RemoveAll(m => gameDetails.MediaUrls.ContainsKey(m));
                bool missingDescription = string.IsNullOrEmpty(gameDetails.Description);

                if (missingMediaTypes.Any() || missingDescription)
                {
                    var missingItemsLog = new List<string>();
                    if (missingDescription) missingItemsLog.Add("description");
                    missingItemsLog.AddRange(missingMediaTypes);
                    logger.Log($"    Missing items for {game.Name}: {string.Join(", ", missingItemsLog)}. Searching other platforms...");

                    var fallbackCandidates = results
                        .Where(r => r.Id != gameResult.Id && LevenshteinDistance(game.Name.ToLower(), r.Name.ToLower()) < 5)
                        .ToList();

                    foreach (var fallbackCandidate in fallbackCandidates)
                    {
                        if (!missingMediaTypes.Any() && !missingDescription) break;
                        logger.Log($"    Checking fallback: '{fallbackCandidate.Name}' on '{fallbackCandidate.System}'");
                        var fallbackDetails = await scraper.GetGameDetails(fallbackCandidate.Id, fallbackCandidate.Slug);

                        if (missingDescription && !string.IsNullOrEmpty(fallbackDetails.Description))
                        {
                            logger.Log($"      Found missing description on {fallbackCandidate.System}.");
                            gameDetails.Description = fallbackDetails.Description;
                            missingDescription = false;
                        }

                        foreach (var mediaType in missingMediaTypes.ToList())
                        {
                            if (fallbackDetails.MediaUrls.TryGetValue(mediaType, out var mediaUrl))
                            {
                                logger.Log($"      Found missing {mediaType} on {fallbackCandidate.System}.");
                                gameDetails.MediaUrls[mediaType] = mediaUrl;
                                missingMediaTypes.Remove(mediaType);
                            }
                        }
                    }
                }
                return gameDetails;
            }
            return null;
        }

        private bool IsModernPlatform(string systemName)
        {
            if (string.IsNullOrEmpty(systemName)) return false;
            var lowerSystemName = systemName.ToLower();
            var modernKeywords = new[] { "pc", "playstation 3", "playstation 4", "playstation 5", "xbox 360", "xbox one", "xbox series", "wii u", "switch" };
            return modernKeywords.Any(keyword => lowerSystemName.Contains(keyword));
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
    }
}