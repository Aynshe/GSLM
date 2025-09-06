using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using GameStoreLibraryManager.Auth;

namespace GameStoreLibraryManager.Gog
{
    public class GogLibrary
    {
        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly GogApi _api;
        private readonly string _cachePath;
        private readonly string _blacklistCachePath;

        public GogLibrary(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
            _api = new GogApi(logger);
            _cachePath = Path.Combine(PathManager.CachePath, "gog_library.json");
            _blacklistCachePath = Path.Combine(PathManager.CachePath, "gog_blacklist.json");
        }

        public async Task<IEnumerable<LauncherGameInfo>> GetAllGamesAsync()
        {
            _logger.Log("[GOG] Getting GOG games.");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var apiGames = await GetOwnedGamesAsync();
            var allGames = apiGames.ToDictionary(g => g.Id);

            var installedGames = GetInstalledGames();

            foreach (var installedGame in installedGames)
            {
                if (allGames.TryGetValue(installedGame.Id, out var game))
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = installedGame.InstallDirectory;
                    game.ExecutableName = installedGame.ExecutableName;
                }
                else
                {
                    allGames.Add(installedGame.Id, installedGame);
                }
            }

            _logger.Log($"[GOG] Found {allGames.Count} games from API/Cache.");
            _logger.Log($"[GOG] Found {installedGames.Count} installed games.");
            var nonInstalledGamesCount = allGames.Values.Count(g => !g.IsInstalled);
            _logger.Log($"[GOG] Found {nonInstalledGamesCount} non-installed games.");

            watch.Stop();
            _logger.Log($"[GOG] Import process finished in {watch.ElapsedMilliseconds} ms.");
            return allGames.Values;
        }

        private async Task<List<LauncherGameInfo>> GetOwnedGamesAsync()
        {
            var apiGames = new List<GogOwnedGame>();
            // Auto-launch embedded auth UI if enabled and no token/code available
            try
            {
                var tokenFile = Path.Combine(PathManager.ApiKeyPath, "gog.token");
                var codeFile = Path.Combine(PathManager.ApiKeyPath, "gog.code");
                if (!File.Exists(tokenFile) && !File.Exists(codeFile) && _config.GetBoolean("gog_enable_token_generation", false))
                {
                    _logger.Log("[GOG] No token/code found. Launching embedded UI to obtain authorization code (gog_enable_token_generation=true)...");
                    try { Directory.CreateDirectory(PathManager.ApiKeyPath); } catch { }
                    AuthUiLauncher.Run("gog");
                }
            }
            catch { }

            var isAuthenticated = await _api.Authenticate(true);
            if (isAuthenticated)
            {
                var cachedGames = new List<GogOwnedGame>();
                if (File.Exists(_cachePath))
                {
                    cachedGames = JsonConvert.DeserializeObject<List<GogOwnedGame>>(File.ReadAllText(_cachePath)) ?? new List<GogOwnedGame>();
                }

                var blacklistedIds = new HashSet<long>();
                if (File.Exists(_blacklistCachePath))
                {
                    blacklistedIds = JsonConvert.DeserializeObject<HashSet<long>>(File.ReadAllText(_blacklistCachePath)) ?? new HashSet<long>();
                }

                var apiGameIds = await _api.GetOwnedGameIdsAsync();
                if (apiGameIds.Count > 0)
                {
                    var cachedGameIds = new HashSet<long>(cachedGames.Select(g => g.Id));
                    var newGameIds = apiGameIds.Where(id => !cachedGameIds.Contains(id) && !blacklistedIds.Contains(id)).ToList();

                    if (newGameIds.Count > 0)
                    {
                        var newlyAddedGames = 0;
                        var newlyBlacklistedIds = 0;

                        foreach (var gameId in newGameIds)
                        {
                            var gameDetails = await _api.GetGameDetailsAsync(gameId);
                            if (gameDetails != null)
                            {
                                cachedGames.Add(new GogOwnedGame { Id = gameId, Title = gameDetails.Title });
                                newlyAddedGames++;
                            }
                            else
                            {
                                blacklistedIds.Add(gameId);
                                newlyBlacklistedIds++;
                            }
                        }

                        if (newlyAddedGames > 0)
                        {
                            try { File.WriteAllText(_cachePath, JsonConvert.SerializeObject(cachedGames, Formatting.Indented)); }
                            catch (Exception ex) { _logger.Log($"[GOG] Failed to save library cache. Error: {ex.Message}"); }
                        }

                        if (newlyBlacklistedIds > 0)
                        {
                            try { File.WriteAllText(_blacklistCachePath, JsonConvert.SerializeObject(blacklistedIds.ToList(), Formatting.Indented)); }
                            catch (Exception ex) { _logger.Log($"[GOG] Failed to save blacklist cache. Error: {ex.Message}"); }
                        }
                    }
                }
                apiGames = cachedGames;
            }
            else
            {
                _logger.Log("[GOG] Authentication failed. Loading games from cache if available.");
                if (File.Exists(_cachePath))
                {
                    apiGames = JsonConvert.DeserializeObject<List<GogOwnedGame>>(File.ReadAllText(_cachePath)) ?? new List<GogOwnedGame>();
                }
            }

            return apiGames.Select(g => new LauncherGameInfo
            {
                Id = g.Id.ToString(),
                Name = g.Title,
                IsInstalled = false,
                Launcher = "GOG"
            }).ToList();
        }

        public List<LauncherGameInfo> GetInstalledGames()
        {
            var games = new List<LauncherGameInfo>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return games;
            }

            try
            {
                using (var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var gogKey = rootKey.OpenSubKey("SOFTWARE\\WOW6432Node\\GOG.com\\Games"))
                {
                    if (gogKey == null) return games;

                    foreach (var gameId in gogKey.GetSubKeyNames())
                    {
                        using (var gameKey = gogKey.OpenSubKey(gameId))
                        {
                            if (gameKey != null)
                            {
                                games.Add(new LauncherGameInfo
                                {
                                    Id = gameId,
                                    Name = gameKey.GetValue("GameName") as string,
                                    InstallDirectory = gameKey.GetValue("path") as string,
                                    ExecutableName = gameKey.GetValue("exe") as string,
                                    IsInstalled = true,
                                    Launcher = "GOG"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG] Error reading registry for installed games: {ex.Message}");
            }

            return games;
        }

        public static string GetGalaxyExecutablePath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            try
            {
                // First Method: Try the modern registry key
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\GOG.com\\GalaxyClient"))
                {
                    if (key != null)
                    {
                        var path = key.GetValue("path") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            var exePath = Path.Combine(path, "GalaxyClient.exe");
                            if (File.Exists(exePath))
                            {
                                return exePath;
                            }
                        }
                    }
                }

                // Second Method: Try the legacy registry key
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\GOG.com\\GalaxyClient\\paths"))
                {
                    if (key != null)
                    {
                        var path = key.GetValue("client") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            // This value might be the exe path directly or the directory
                            if (File.Exists(path) && path.EndsWith("GalaxyClient.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                return path;
                            }

                            var exePath = Path.Combine(path, "GalaxyClient.exe");
                            if (File.Exists(exePath))
                            {
                                return exePath;
                            }
                        }
                    }
                }
            }
            catch (Exception) { /* Ignore exceptions to prevent crashes */ }

            return null;
        }
    }
}
