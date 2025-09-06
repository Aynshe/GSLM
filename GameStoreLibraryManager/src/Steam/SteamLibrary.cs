using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using GameStoreLibraryManager.Common;
using GameStoreLibraryManager.Steam.ValveKeyValue;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Threading.Tasks;
using GameStoreLibraryManager.Auth;

namespace GameStoreLibraryManager.Steam
{
    public class SteamLibrary
    {
        const string GameLaunchUrl = @"steam://rungameid/{0}";
        const string AppDetailsUrl = @"https://store.steampowered.com/api/appdetails?appids={0}";

        private Config _config;
        private SimpleLogger _logger;

        public SteamLibrary(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<LauncherGameInfo[]> GetAllGamesAsync()
        {
            _logger.Log("[Steam] Getting Steam games.");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var ownedGames = await GetOwnedGamesAsync();
            var allGames = ownedGames.ToDictionary(g => g.Id, g => g);

            var installedGames = new List<LauncherGameInfo>();
            if (_config.GetBoolean("steam_import_installed", false))
            {
                installedGames.AddRange(GetInstalledGames(ownedGames.ToDictionary(g => g.Id, g => JObject.Parse(JsonConvert.SerializeObject(g)) as JToken)));
            }

            foreach (var installedGame in installedGames)
            {
                if (allGames.TryGetValue(installedGame.Id, out var game))
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = installedGame.InstallDirectory;
                    game.LauncherUrl = installedGame.LauncherUrl;
                }
                else
                {
                    allGames.Add(installedGame.Id, installedGame);
                }
            }

            _logger.Log($"[Steam] Found {allGames.Count} games from API.");
            _logger.Log($"[Steam] Found {installedGames.Count} installed games.");
            var nonInstalledGamesCount = allGames.Values.Count(g => !g.IsInstalled);
            _logger.Log($"[Steam] Found {nonInstalledGamesCount} non-installed games.");

            watch.Stop();
            _logger.Log($"[Steam] Import process finished in {watch.ElapsedMilliseconds} ms.");

            return allGames.Values.ToArray();
        }

        private async Task<List<LauncherGameInfo>> GetOwnedGamesAsync()
        {
            var ownedGames = new List<LauncherGameInfo>();
            string apiKey = null;
            try
            {
                string apiKeyPath = Path.Combine(PathManager.ApiKeyPath, "steam.apikey");
                bool protect = _config.GetBoolean("enable_dpapi_protection", false);
                if (File.Exists(apiKeyPath))
                {
                    apiKey = SecureStore.ReadString(apiKeyPath)?.Trim();
                    // Migrate plaintext to protected if enabled
                    if (protect && !SecureStore.IsProtectedFile(apiKeyPath) && !string.IsNullOrEmpty(apiKey))
                    {
                        SecureStore.WriteString(apiKeyPath, apiKey, true);
                    }
                }
                else if (_config.GetBoolean("steam_enable_api_generation", false))
                {
                    _logger.Log("[Steam] API key not found. Launching embedded UI to generate it (steam_enable_api_generation=true)...");
                    try { Directory.CreateDirectory(PathManager.ApiKeyPath); } catch { }
                    AuthUiLauncher.Run("steam");
                    if (File.Exists(apiKeyPath))
                    {
                        apiKey = SecureStore.ReadString(apiKeyPath)?.Trim();
                        if (protect && !SecureStore.IsProtectedFile(apiKeyPath) && !string.IsNullOrEmpty(apiKey))
                        {
                            SecureStore.WriteString(apiKeyPath, apiKey, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log("[Steam] Error reading steam.apikey file: " + ex.Message);
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.Log("[Steam] Steam API Key not found or empty. Cannot fetch owned games from Steam Web API.");
                _logger.Log("  To get an API key, go to: https://steamcommunity.com/dev/apikey");
                _logger.Log($"  Create a file named 'steam.apikey' in the '{PathManager.ApiKeyPath}' directory and paste your key into it.");
                return ownedGames;
            }

            _logger.Log("[Steam] Steam API key found. Getting games from API.");

            string steamId64 = GetSteamId64();
            if (string.IsNullOrEmpty(steamId64))
            {
                _logger.Log("[Steam] Could not find user SteamID64. Cannot fetch game list from API.");
                return ownedGames;
            }

            string url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={apiKey}&steamid={steamId64}&format=json&include_appinfo=1&include_played_free_games=true&skip_unvetted_apps=false";

            try
            {
                string json;
                using (var httpClient = new HttpClient())
                {
                    json = await httpClient.GetStringAsync(url);
                }

                var response = JObject.Parse(json)["response"];
                if (response != null && response.ToString() != "{}")
                {
                    var responseGames = response["games"];
                    if (responseGames != null)
                    {
                        bool useDelay = _config.GetBoolean("steam_api_delay", false);
                        foreach (var g in responseGames)
                        {
                            string appId = g["appid"]?.ToString();
                            if (string.IsNullOrEmpty(appId))
                                continue;

                            if (useDelay)
                            {
                                await Task.Delay(500); // Delay to avoid rate limiting
                            }
                            var gameDetails = await GetAppDetailsAsync(appId);
                            string name = gameDetails?["name"]?.ToString() ?? g["name"]?.ToString();

                            if (string.IsNullOrEmpty(name))
                                continue;

                            ownedGames.Add(new LauncherGameInfo()
                            {
                                Id = appId,
                                Name = name,
                                LauncherUrl = string.Format(GameLaunchUrl, appId),
                                Launcher = "Steam"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log("[Steam] Steam API call failed. Error: " + ex.Message);
            }

            return ownedGames;
        }

        private async Task<JObject> GetAppDetailsAsync(string appId)
        {
            string cachePath = Path.Combine(PathManager.CachePath, "steam", "appdetails");
            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);

            string cacheFile = Path.Combine(cachePath, $"{appId}.json");
            string json = null;

            if (File.Exists(cacheFile))
            {
                json = File.ReadAllText(cacheFile);
            }
            else
            {
                string url = string.Format(AppDetailsUrl, appId);
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        json = await httpClient.GetStringAsync(url);
                    }

                    var response = JObject.Parse(json)[appId];
                    if (response["success"].Value<bool>())
                    {
                        File.WriteAllText(cacheFile, response["data"].ToString());
                        return response["data"] as JObject;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Steam] Failed to get app details for {appId}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    return JObject.Parse(json);
                }
                catch { }
            }

            return null;
        }

        private LauncherGameInfo[] GetInstalledGames(Dictionary<string, JToken> apiGames)
        {
            var games = new List<LauncherGameInfo>();
            var installPath = GetInstallPath();
            if (string.IsNullOrEmpty(installPath))
                return games.ToArray();

            string libraryfoldersPath = Path.Combine(installPath, "config", "libraryfolders.vdf");
            if (!File.Exists(libraryfoldersPath))
                return games.ToArray();

            try
            {
                var libraryfolders = KeyValue.LoadAsText(libraryfoldersPath);
                if (libraryfolders == null)
                    return games.ToArray();

                var folders = GetLibraryFolders(libraryfolders);

                foreach (var folder in folders)
                {
                    var libFolder = Path.Combine(folder, "steamapps");
                    if (Directory.Exists(libFolder))
                    {
                        foreach (var game in GetInstalledGamesFromFolder(libFolder, apiGames))
                        {
                            if (game.Id == "228980")
                                continue;

                            games.Add(game);
                        }
                    }
                }
            }
            catch { }

            return games.ToArray();
        }

        private List<LauncherGameInfo> GetInstalledGamesFromFolder(string path, Dictionary<string, JToken> apiGames)
        {
            var games = new List<LauncherGameInfo>();

            foreach (var file in Directory.GetFiles(path, @"appmanifest*"))
            {
                if (file.EndsWith("tmp", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var game = GetInstalledGameFromFile(Path.Combine(path, file), apiGames);
                    if (game == null)
                        continue;

                    if (string.IsNullOrEmpty(game.InstallDirectory) || game.InstallDirectory.Contains(@"steamapps\music"))
                        continue;

                    games.Add(game);
                }
                catch (Exception ex)
                {
                    _logger.Log("[Steam] Error reading manifest file: " + ex.Message);
                }
            }

            return games;
        }

        LauncherGameInfo GetInstalledGameFromFile(string path, Dictionary<string, JToken> apiGames)
        {
            var kv = KeyValue.LoadAsText(path);
            if (kv == null)
                return null;

            SteamAppStateFlags appState;
            if (!string.IsNullOrEmpty(kv["StateFlags"].Value) && Enum.TryParse<SteamAppStateFlags>(kv["StateFlags"].Value, out appState))
            {
                if (!appState.HasFlag(SteamAppStateFlags.FullyInstalled))
                    return null;
            }
            else
                return null;

            var name = string.Empty;
            if (string.IsNullOrEmpty(kv["name"].Value))
            {
                if (kv["UserConfig"]["name"].Value != null)
                {
                    name = kv["UserConfig"]["name"].Value;
                }
            }
            else
                name = kv["name"].Value;

            var gameId = kv["appID"].Value;
            if (string.IsNullOrEmpty(gameId))
                return null;

            if (apiGames != null && apiGames.ContainsKey(gameId))
            {
                var apiGame = apiGames[gameId];
                var apiName = apiGame["name"]?.ToString();
                if (!string.IsNullOrEmpty(apiName))
                    name = apiName;
            }

            if (gameId == "228980")
                return null;

            var installDir = Path.Combine((new FileInfo(path)).Directory.FullName, "common", kv["installDir"].Value);
            if (!Directory.Exists(installDir))
            {
                installDir = Path.Combine((new FileInfo(path)).Directory.FullName, "music", kv["installDir"].Value);
                if (!Directory.Exists(installDir))
                {
                    installDir = string.Empty;
                }
            }

            var game = new LauncherGameInfo()
            {
                Id = gameId,
                Name = name,
                InstallDirectory = installDir,
                LauncherUrl = string.Format(GameLaunchUrl, gameId) + "\"" + " -silent",
                Launcher = "Steam",
                IsInstalled = true
            };

            return game;
        }

        static List<string> GetLibraryFolders(KeyValue foldersData)
        {
            var dbs = new List<string>();
            foreach (var child in foldersData.Children)
            {
                int val;
                if (int.TryParse(child.Name, out val))
                {
                    if (!string.IsNullOrEmpty(child.Value) && Directory.Exists(child.Value))
                        dbs.Add(child.Value);
                    else if (child.Children != null && child.Children.Count > 0)
                    {
                        var path = child.Children.FirstOrDefault(a => a.Name != null && a.Name.Equals("path", StringComparison.OrdinalIgnoreCase) == true);
                        if (path != null && !string.IsNullOrEmpty(path.Value) && Directory.Exists(path.Value))
                            dbs.Add(path.Value);
                    }
                }
            }

            return dbs;
        }

        public static string GetInstallPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam"))
                    {
                        if (key != null)
                        {
                            var o = key.GetValue("InstallPath");
                            if (o != null)
                                return o as string;
                        }
                    }
                }
                catch { }
            }
            else
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (home == null) return null;

                string[] possiblePaths =
                {
                    Path.Combine(home, ".steam", "steam"),
                    Path.Combine(home, ".local", "share", "Steam")
                };

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                        return path;
                }
            }

            return null;
        }

        private string GetSteamId64()
        {
            string steamPath = GetInstallPath();
            if (string.IsNullOrEmpty(steamPath))
                return null;

            string loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (!File.Exists(loginUsersPath))
                return null;

            try
            {
                string vdfText = File.ReadAllText(loginUsersPath);
                var regex = new Regex("\"(\\d{17})\"\\s*\\{[^}]*\"MostRecent\"\\s*\"1\"", RegexOptions.IgnoreCase);
                var match = regex.Match(vdfText);

                if (match.Success)
                    return match.Groups[1].Value;

                // Fallback
                regex = new Regex("\"(\\d{17})\"");
                match = regex.Match(vdfText);
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                _logger.Log("[Steam] Error finding SteamID64: " + ex.Message);
            }

            return null;
        }
    }
}
