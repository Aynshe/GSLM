using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using GameStoreLibraryManager.Auth;

namespace GameStoreLibraryManager.Epic
{
    public class EpicLibrary
    {
        const string GameLaunchUrl = @"com.epicgames.launcher://apps/{0}?action=launch&silent=true";
        const string GameInstallUrl = @"com.epicgames.launcher://apps/{0}?action=install";

        private Config _config;
        private SimpleLogger _logger;
        public EpicToken CurrentToken { get; private set; }

        public EpicLibrary(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<LauncherGameInfo[]> GetAllGamesAsync()
        {
            _logger.Log("[Epic] Getting Epic games.");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var apiGames = await GetOwnedGamesAsync();
            var allGames = apiGames.ToDictionary(g => g.Id, g => g);

            var installedGames = GetInstalledGames(apiGames).ToList();

            foreach (var installedGame in installedGames)
            {
                if (allGames.TryGetValue(installedGame.Id, out var game))
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = installedGame.InstallDirectory;
                    game.LauncherUrl = installedGame.LauncherUrl;
                    game.ExecutableName = installedGame.ExecutableName;
                }
                else
                {
                    allGames.Add(installedGame.Id, installedGame);
                }
            }

            _logger.Log($"[Epic] Found {allGames.Count} games from API.");
            _logger.Log($"[Epic] Found {installedGames.Count} installed games.");
            var nonInstalledGamesCount = allGames.Values.Count(g => !g.IsInstalled);
            _logger.Log($"[Epic] Found {nonInstalledGamesCount} non-installed games.");

            watch.Stop();
            _logger.Log($"[Epic] Import process finished in {watch.ElapsedMilliseconds} ms.");

            return allGames.Values.ToArray();
        }

        private async Task<List<LauncherGameInfo>> GetOwnedGamesAsync()
        {
            var apiGames = new List<EpicLibraryItem>();

            try
            {
                var api = new EpicApi();
                EpicToken token = null;

                string tokenPath = Path.Combine(PathManager.ApiKeyPath, "epic.token");
                bool protect = _config.GetBoolean("enable_dpapi_protection", false);
                string codePath = Path.Combine(PathManager.ApiKeyPath, "epic.code");

                // Auto-generate code with embedded UI if enabled and nothing is present yet
                if (!File.Exists(codePath) && !File.Exists(tokenPath) && _config.GetBoolean("epic_enable_token_generation", false))
                {
                    try { Directory.CreateDirectory(PathManager.ApiKeyPath); } catch { }
                    _logger.Log("[EPIC] No token/refresh found. Launching embedded UI to obtain authorization code (epic_enable_token_generation=true)...");
                    AuthUiLauncher.Run("epic");
                }

                if (File.Exists(codePath))
                {
                    try
                    {
                        string content = File.ReadAllText(codePath).Trim();
                        string authCode = null;

                        // Try as URL with ?code=
                        try
                        {
                            var uri = new Uri(content, UriKind.RelativeOrAbsolute);
                            if (uri.IsAbsoluteUri)
                            {
                                var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                                authCode = q["code"];
                            }
                        }
                        catch { }

                        // Try as JSON with authorizationCode or redirectUrl containing ?code=
                        if (string.IsNullOrEmpty(authCode) && (content.StartsWith("{") || content.StartsWith("[")))
                        {
                            try
                            {
                                dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(content);
                                authCode = (string)(obj?.authorizationCode) ?? (string)(obj?.exchangeCode);
                                if (string.IsNullOrEmpty(authCode))
                                {
                                    string redirect = (string)(obj?.redirectUrl);
                                    if (!string.IsNullOrEmpty(redirect))
                                    {
                                        var u = new Uri(redirect, UriKind.Absolute);
                                        var q2 = System.Web.HttpUtility.ParseQueryString(u.Query);
                                        authCode = q2["code"];
                                    }
                                }
                            }
                            catch { }
                        }

                        // Fallback: treat as raw code
                        if (string.IsNullOrEmpty(authCode)) authCode = content;

                        if (!string.IsNullOrEmpty(authCode))
                        {
                            token = await api.AuthenticateWithAuthorizationCode(authCode);
                            if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
                            {
                                SecureStore.WriteString(tokenPath, token.RefreshToken, protect);
                            }
                            else
                            {
                                _logger.Log("[EPIC] Authentication with authorization code failed. The code might be expired or invalid.");
                            }
                        }
                    }
                    finally
                    {
                        try { File.Delete(codePath); } catch { }
                    }
                }

                if (token == null && File.Exists(tokenPath))
                {
                    string refreshToken = SecureStore.ReadString(tokenPath)?.Trim();
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        token = await api.AuthenticateWithRefreshToken(refreshToken);
                        if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
                        {
                            // migrate if needed
                            if (protect && !SecureStore.IsProtectedFile(tokenPath))
                            {
                                SecureStore.WriteString(tokenPath, token.RefreshToken, true);
                            }
                            else
                            {
                                SecureStore.WriteString(tokenPath, token.RefreshToken, protect);
                            }
                        }
                    }
                }

                CurrentToken = token;

                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    _logger.Log("[Epic] Epic API key found. Getting games from API.");
                    apiGames = await api.GetLibraryItems(token.AccessToken, token.AccountId);
                }
                else
                {
                    _logger.Log("[Epic] Could not get an access token. Only installed games will be listed.");
                    _logger.Log($"1. Create a file named 'epic.code' in the '{PathManager.ApiKeyPath}' directory.");
                    _logger.Log("2. Open the following URL in your browser, log in if necessary:");
                    _logger.Log("   https://www.epicgames.com/id/api/redirect?clientId=34a02cf8f4414e29b15921876da36f9a&responseType=code");
                    _logger.Log("3. After logging in, you will be redirected. Copy the authorization code from the page content.");
                    _logger.Log("4. Paste this code into the 'epic.code' file and save it.");
                    _logger.Log("5. Rerun this application.");
                }
            }
            catch (Exception ex)
            {
                _logger.Log("[EPIC] Error getting games from API: " + ex.Message);
            }

            return apiGames.Select(g => new LauncherGameInfo
            {
                Id = g.AppName,
                Name = g.Metadata.DisplayName,
                Launcher = "Epic",
                Namespace = g.Namespace,
                CatalogItemId = g.CatalogItemId,
                LauncherUrl = string.Format(GameInstallUrl, g.AppName)
            }).ToList();
        }

        private static LauncherGameInfo[] GetInstalledGames(List<LauncherGameInfo> apiGames)
        {
            var games = new List<LauncherGameInfo>();
            if (!IsInstalled)
                return games.ToArray();

            var appList = GetInstalledAppList();
            var manifests = GetInstalledManifests();
            if (appList == null || manifests == null)
                return games.ToArray();

            foreach (var app in appList)
            {
                if (app.AppName.StartsWith("UE_"))
                    continue;

                var manifest = manifests.FirstOrDefault(a => a.AppName == app.AppName);
                if (manifest == null || manifest.AppName != manifest.MainGameAppName)
                    continue;

                if (manifest.AppCategories != null && manifest.AppCategories.Any(a => a == "plugins" || a == "plugins/engine"))
                    continue;

                var gameName = manifest.DisplayName ?? Path.GetFileName(app.InstallLocation);
                if (apiGames != null)
                {
                    var apiGame = apiGames.FirstOrDefault(g => g.Id == app.AppName);
                    if (apiGame != null && !string.IsNullOrEmpty(apiGame.Name))
                        gameName = apiGame.Name;
                }

                var installLocation = manifest.InstallLocation ?? app.InstallLocation;
                if (string.IsNullOrEmpty(installLocation))
                    continue;

                var game = new LauncherGameInfo()
                {
                    Id = app.AppName,
                    Name = gameName,
                    LauncherUrl = string.Format(GameLaunchUrl, manifest.AppName),
                    InstallDirectory = Path.GetFullPath(installLocation),
                    ExecutableName = manifest.LaunchExecutable,
                    Launcher = "Epic",
                    IsInstalled = true
                };

                games.Add(game);
            }
            return games.ToArray();
        }

        private static string AllUsersPath
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "Epic");
                else
                    return Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".config", "epic");
            }
        }

        public static bool IsInstalled
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return !string.IsNullOrEmpty(GetExecutablePath());
                else
                    return Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".config", "legendary"));
            }
        }

        private static string GetExecutablePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkCommand", null);
                return modSdkMetadataDir != null ? modSdkMetadataDir.ToString() : null;
            }

            return null;
        }

        private static string GetMetadataPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkMetadataDir", null);
                return modSdkMetadataDir != null ? modSdkMetadataDir.ToString() : null;
            }
            else
            {
                // On Linux, Heroic/Legendary stores manifests in ~/.config/legendary/metadata
                string legendaryConfigPath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".config", "legendary", "metadata");
                if (Directory.Exists(legendaryConfigPath))
                    return legendaryConfigPath;
            }

            return null;
        }

        private static List<LauncherInstalled.InstalledApp> GetInstalledAppList()
        {
            var installListPath = Path.Combine(AllUsersPath, "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installListPath))
                return new List<LauncherInstalled.InstalledApp>();
            var list = JsonTools.DeserializeString<LauncherInstalled>(File.ReadAllText(installListPath));
            return list.InstallationList;
        }

        private static IEnumerable<EpicGame> GetInstalledManifests()
        {
            var installListPath = GetMetadataPath();
            if (installListPath != null && Directory.Exists(installListPath))
            {
                foreach (var manFile in Directory.GetFiles(installListPath, "*.item"))
                {
                    EpicGame manifest = null;
                    try { manifest = JsonTools.DeserializeString<EpicGame>(File.ReadAllText(manFile)); }
                    catch { }
                    if (manifest != null)
                        yield return manifest;
                }
            }
        }
    }
}
