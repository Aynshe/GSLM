using GameStoreLibraryManager.Common;
using GameStoreLibraryManager.Xbox.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using Windows.ApplicationModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Xml;

namespace GameStoreLibraryManager.Xbox
{
    public class XboxLibrary
    {
        private class DbGameDetails
        {
            public string PFN { get; set; }
            public string ProductId { get; set; }
            public string Title { get; set; }
            public bool IsMsixvc { get; set; }
        }

        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly XboxAccountClient _api;

        public XboxLibrary(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
            _api = new XboxAccountClient(config, logger);
        }

        public async Task<IEnumerable<LauncherGameInfo>> GetAllGamesAsync()
        {
            _logger.Log("[Xbox] Starting Xbox game retrieval process.");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var dbGameDetails = GetGameDetailsFromDbCache();
            _logger.Log($"[Xbox] Found {dbGameDetails.Count} unique game entries in the Xbox App cache.");

            var pfnToGameDetailsMap = dbGameDetails.Values
                .Where(d => !string.IsNullOrEmpty(d.PFN))
                .ToDictionary(d => d.PFN, d => d, StringComparer.OrdinalIgnoreCase);

            var entitledGames = await GetOnlineGamesAsync(pfnToGameDetailsMap);
            _logger.Log($"[Xbox] Found {entitledGames.Count} unique entitled games from APIs, keyed by ProductId.");

            if (_config.GetBoolean("enable_xbox_library", false))
            {
                _logger.Log("[Xbox] 'Import installed games' is enabled. Scanning for local packages.");
                var installedPackages = GetInstalledPackages();
                _logger.Log($"[Xbox] Found {installedPackages.Count} installed UWP packages from the OS.");

                foreach (var package in installedPackages.Values)
                {
                    if (pfnToGameDetailsMap.TryGetValue(package.Id.FamilyName, out var details))
                    {
                        if (entitledGames.TryGetValue(details.ProductId, out var game))
                        {
                            _logger.Log($"[Xbox] Matched installed PFN '{package.Id.FamilyName}' to entitled game '{game.Name}'.");
                            game.IsInstalled = true;

                            if (details.IsMsixvc)
                            {
                                game.LauncherUrl = $"msgamelaunch://shortcutLaunch/?ProductId={details.ProductId}";
                                _logger.Log($"[Xbox] Generated ms-gamelaunch URL: {game.LauncherUrl}");
                            }
                            else
                            {
                                game.LauncherUrl = GetExecutablePathFromManifest(package);
                                _logger.Log($"[Xbox] Found classic game executable path: {game.LauncherUrl}");
                            }
                        }
                    }
                }
            }
            else
            {
                 _logger.Log("[Xbox] 'Import installed games' is disabled. Skipping local scan.");
            }

            if (_config.GetBoolean("create_gslm_shortcut", true))
            {
                _logger.Log("[Xbox] Adding synthetic '.GSLM Settings' entry for windows folder.");
                if (!entitledGames.ContainsKey("gslm-settings"))
                {
                    entitledGames["gslm-settings"] = new LauncherGameInfo
                    {
                        Id = "gslm-settings",
                        Name = ".GSLM Settings",
                        Launcher = "Xbox",
                        IsInstalled = true,
                        LauncherUrl = "internal://gslm-settings"
                    };
                }
            }

            if (_config.GetBoolean("enable_xbox_cloud_gaming", false))
            {
                _logger.Log("[Xbox] Adding synthetic '.Xbox Cloud Gaming' entry.");
                if (!entitledGames.ContainsKey("XBOX_CLOUD_GAMING"))
                {
                    entitledGames["XBOX_CLOUD_GAMING"] = new LauncherGameInfo
                    {
                        Id = "XBOX_CLOUD_GAMING",
                        Name = ".Xbox Cloud Gaming",
                        Launcher = "Xbox",
                        IsInstalled = true,
                        LauncherUrl = "internal://xboxcloudgaming"
                    };
                }
            }

            watch.Stop();
            _logger.Log($"[Xbox] Xbox game import finished in {watch.ElapsedMilliseconds} ms. Total games found: {entitledGames.Count}.");
            return entitledGames.Values;
        }

        private Dictionary<string, Package> GetInstalledPackages()
        {
            var packagesDict = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var packageManager = new PackageManager();
                var packages = packageManager.FindPackagesForUser(string.Empty);
                foreach (var package in packages)
                {
                    if (!package.IsFramework && !package.IsResourcePackage && !package.IsBundle && !package.IsDevelopmentMode)
                    {
                        packagesDict[package.Id.FamilyName] = package;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Xbox] Failed to get installed UWP packages from OS: {ex.Message}");
            }
            return packagesDict;
        }

        private string GetExecutablePathFromManifest(Package package)
        {
            try
            {
                var manifestPath = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) return null;

                var xmlDoc = new XmlDocument();
                xmlDoc.Load(manifestPath);

                var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                var manifestNamespace = xmlDoc.DocumentElement.NamespaceURI;
                if (!string.IsNullOrEmpty(manifestNamespace))
                {
                    nsmgr.AddNamespace("m", manifestNamespace);
                }

                var appNode = xmlDoc.SelectSingleNode("//m:Application", nsmgr);
                if (appNode != null)
                {
                    var executable = appNode.Attributes["Executable"]?.Value;
                    if (!string.IsNullOrEmpty(executable))
                    {
                        return Path.Combine(package.InstalledLocation.Path, executable);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Xbox] Error parsing AppxManifest for {package.Id.FamilyName}: {ex.Message}");
            }
            return null;
        }

        private Dictionary<string, DbGameDetails> GetGameDetailsFromDbCache()
        {
            var gameDetails = new Dictionary<string, DbGameDetails>(StringComparer.OrdinalIgnoreCase);
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe", "LocalState", "AsyncCache.db");

            if (!File.Exists(dbPath)) return gameDetails;

            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand("SELECT value FROM AsyncCache", connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var jsonValue = reader.GetString(0);
                            if (string.IsNullOrWhiteSpace(jsonValue) || !jsonValue.Trim().StartsWith("{")) continue;

                            try
                            {
                                var data = JObject.Parse(jsonValue);
                                var productData = data["data"] ?? data;
                                if (productData?["productKind"]?.ToString() == "GAME" || (productData["IsGame"]?.ToObject<bool>() ?? false))
                                {
                                    var pfn = productData["alternateIds"]?.FirstOrDefault(item => item["idType"]?.ToString() == "PACKAGEFAMILYNAME")?["id"]?.ToString() ?? productData["PackageFamilyName"]?.ToString();
                                    var productId = productData["ProductId"]?.ToString() ?? productData["StoreId"]?.ToString();
                                    var title = productData["title"]?.ToString() ?? productData["DisplayName"]?.ToString();
                                    var isMsixvc = productData["isMSIXVC"]?.ToObject<bool>() ?? false;

                                    if (!string.IsNullOrEmpty(pfn) && !string.IsNullOrEmpty(productId))
                                    {
                                        if (!gameDetails.ContainsKey(pfn))
                                        {
                                            gameDetails[pfn] = new DbGameDetails { PFN = pfn, Title = title, ProductId = productId, IsMsixvc = isMsixvc };
                                        }
                                    }
                                }
                            }
                            catch { /* Ignore individual record parsing errors */ }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Xbox] Failed to read Xbox App cache database: {ex.Message}");
            }
            return gameDetails;
        }

        private async Task<Dictionary<string, LauncherGameInfo>> GetOnlineGamesAsync(Dictionary<string, DbGameDetails> pfnToGameDetailsMap)
        {
            var onlineGames = new Dictionary<string, LauncherGameInfo>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(Path.Combine(PathManager.ApiKeyPath, "xbox_live.json")) || !File.Exists(Path.Combine(PathManager.ApiKeyPath, "xbox_xsts.json")))
            {
                throw new Exception("Xbox tokens not found. Login required.");
            }

            try
            {
                var libraryTitles = await _api.GetLibraryTitlesAsync();
                foreach (var title in libraryTitles)
                {
                    if (title.devices?.Contains("PC") == true && !string.IsNullOrEmpty(title.pfn))
                    {
                        if (pfnToGameDetailsMap.TryGetValue(title.pfn, out var details))
                        {
                             if (!onlineGames.ContainsKey(details.ProductId))
                            {
                                onlineGames[details.ProductId] = new LauncherGameInfo { Id = details.ProductId, Name = title.name, Launcher = "Xbox", LauncherUrl = $"ms-windows-store://pdp/?productid={details.ProductId}" };
                            }
                        }
                    }
                }

                if (_config.GetBoolean("enable_xbox_gamepass_catalog", false))
                {
                    var region = _config.GetString("xbox_gamepass_region", "US");
                    var language = System.Globalization.CultureInfo.CurrentCulture.Name;
                    const string pcCatalogId = "fdd9e2a7-0fee-49f6-ad69-4354098401ff";
                    var catalogProducts = await _api.GetGamePassCatalogAsync(pcCatalogId, region, language);
                    var productIds = catalogProducts.Select(p => p.id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
                    var productDetails = await _api.GetProductDetailsAsync(productIds, region, language);

                    foreach (var detail in productDetails.Values)
                    {
                        var title = detail.GetTitle();
                        var productId = detail.ProductId;

                        if (string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(title)) continue;

                        if (!onlineGames.ContainsKey(productId))
                        {
                            onlineGames[productId] = new LauncherGameInfo { Id = productId, Name = title, Launcher = "Xbox", LauncherUrl = $"ms-windows-store://pdp/?productid={productId}" };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[Xbox] Failed to get online games from API: {ex.Message}");
            }
            return onlineGames;
        }
    }
}