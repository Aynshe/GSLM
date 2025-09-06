using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;
using Newtonsoft.Json;
using System.Data.SQLite;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GameStoreLibraryManager.Amazon
{
    public class AmazonLibrary
    {
        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly AmazonApi _api;
        private readonly string _cachePath;

        public AmazonLibrary(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
            _api = new AmazonApi(logger, _config);
            _cachePath = Path.Combine(PathManager.CachePath, "amazon_library.json");
        }

        public async Task<IEnumerable<LauncherGameInfo>> GetAllGamesAsync()
        {
            _logger.Log("[Amazon] Getting Amazon games.");
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
                    // Ensure installed entries use play scheme, not install
                    game.LauncherUrl = $"amazon-games://play/{installedGame.Id}";
                }
                else
                {
                    allGames.Add(installedGame.Id, installedGame);
                }
            }

            _logger.Log($"[Amazon] Found {allGames.Count} games from API/Cache.");
            _logger.Log($"[Amazon] Found {installedGames.Count} installed games.");
            var nonInstalledGamesCount = allGames.Values.Count(g => !g.IsInstalled);
            _logger.Log($"[Amazon] Found {nonInstalledGamesCount} non-installed games.");

            watch.Stop();
            _logger.Log($"[Amazon] Import process finished in {watch.ElapsedMilliseconds} ms.");
            return allGames.Values;
        }

        private async Task<List<LauncherGameInfo>> GetOwnedGamesAsync()
        {
            var apiGames = new List<AmazonOwnedGame>();

            if (await _api.Authenticate())
            {
                _logger.Log("[Amazon] Fetching library from API.");
                var entitlements = await _api.GetAccountEntitlements();
                apiGames = entitlements
                    .Where(e => e.Product != null && !string.IsNullOrEmpty(e.Product.Id) && !string.IsNullOrEmpty(e.Product.Title))
                    .Select(e => new AmazonOwnedGame { Id = e.Product.Id, Title = e.Product.Title })
                    .ToList();

                try
                {
                    File.WriteAllText(_cachePath, JsonConvert.SerializeObject(apiGames, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Amazon] Failed to save library cache. Error: {ex.Message}");
                }
            }
            else
            {
                _logger.Log("[Amazon] Authentication failed. Loading games from cache if available.");
                if (File.Exists(_cachePath))
                {
                    apiGames = JsonConvert.DeserializeObject<List<AmazonOwnedGame>>(File.ReadAllText(_cachePath)) ?? new List<AmazonOwnedGame>();
                }
            }

            return apiGames.Select(g => new LauncherGameInfo
            {
                Id = g.Id,
                Name = g.Title,
                IsInstalled = false,
                Launcher = "Amazon",
                LauncherUrl = $"amazon-games://install/{g.Id}"
            }).ToList();
        }

        private List<LauncherGameInfo> GetInstalledGames()
        {
            var games = new List<LauncherGameInfo>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return games;
            }

            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Amazon Games\Data\Games\Sql\GameInstallInfo.sqlite");
            if (!File.Exists(dbPath))
            {
                _logger.Log("[Amazon] GameInstallInfo.sqlite not found.");
                return games;
            }

            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;"))
                {
                    connection.Open();
                    string sql = "SELECT * FROM DbSet WHERE Installed = 1;";
                    using (var command = new SQLiteCommand(sql, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var game = new LauncherGameInfo
                            {
                                Id = reader["Id"] as string,
                                Name = reader["ProductTitle"] as string,
                                InstallDirectory = reader["InstallDirectory"] as string,
                                IsInstalled = true,
                                Launcher = "Amazon",
                                LauncherUrl = $"amazon-games://play/{reader["Id"] as string}"
                            };
                            games.Add(game);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Amazon] Error reading installed games from SQLite DB: {ex.Message}");
            }

            return games;
        }

        public static string GetLauncherExecutablePath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            try
            {
                // Look in the 64-bit view of the registry
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey?.GetValue("DisplayName") as string == "Amazon Games")
                                {
                                    var installLocation = subKey.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrEmpty(installLocation))
                                    {
                                        var exePath = Path.Combine(installLocation, "Amazon Games.exe");
                                        if (File.Exists(exePath))
                                        {
                                            return exePath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail, we'll handle the null return
            }

            // Fallback to default location if registry fails
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Amazon Games", "App", "Amazon Games.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            return null;
        }
    }
}
