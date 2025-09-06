using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using GameStoreLibraryManager.Common;
using GameStoreLibraryManager.Amazon;
using GameStoreLibraryManager.Epic;
using GameStoreLibraryManager.Gog;
using GameStoreLibraryManager.HfsPlay;
using GameStoreLibraryManager.Steam;
using GameStoreLibraryManager.Auth;
using GameStoreLibraryManager.Menu;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GameStoreLibraryManager
{
    // Helper class to store info from existing gamelist.xml
    class GamelistEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsComplete { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var mutex = new System.Threading.Mutex(true, "{8F6F0AC4-B9A1-45fd-A8CF-72F04E6BDE8F}", out bool createdNew);
            if (!createdNew)
            {
                var instanceLogger = new SimpleLogger("instance.log", append: true);
                instanceLogger.Log($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Another instance is already running. Exiting.");
                return;
            }

            try
            {
                MainAsync(args).GetAwaiter().GetResult();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        static async Task MainAsync(string[] args)
        {
            // Special mode: installation automation (e.g., called from EmulationStation with %* and -installstore)
            if (args != null && args.Any(a => string.Equals(a, "-installstore", StringComparison.OrdinalIgnoreCase)))
            {
                var installLogger = new SimpleLogger("install_automation.log");
                installLogger.Log("[Boot] Logger initialized.");
                try
                {
                    installLogger.Debug($"[Boot] CommandLine: {Environment.CommandLine}");
                    installLogger.Debug($"[Boot] Args: {string.Join(" ", args ?? Array.Empty<string>())}");
                }
                catch { }

                var cfg = new Config();
                bool steamEnabled = cfg.GetBoolean("steam_enable_install_automation", true);
                bool amazonEnabled = cfg.GetBoolean("amazon_enable_install_automation", false);
                bool epicEnabled = cfg.GetBoolean("epic_enable_install_automation", false);

                bool isSteamContext = args.Any(a =>
                    (!string.IsNullOrEmpty(a)) &&
                    (a.IndexOf("\\steam\\not installed\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     a.IndexOf("/steam/not installed/", StringComparison.OrdinalIgnoreCase) >= 0));
                bool isAmazonContext = args.Any(a =>
                    (!string.IsNullOrEmpty(a)) &&
                    (a.IndexOf("\\amazon\\not installed\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     a.IndexOf("/amazon/not installed/", StringComparison.OrdinalIgnoreCase) >= 0));
                bool isEpicContext = args.Any(a =>
                    (!string.IsNullOrEmpty(a)) &&
                    (a.IndexOf("\\epic\\not installed\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     a.IndexOf("/epic/not installed/", StringComparison.OrdinalIgnoreCase) >= 0));
                bool isGogContext = args.Any(a =>
                    (!string.IsNullOrEmpty(a)) &&
                    (a.IndexOf("\\gog\\not installed\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     a.IndexOf("/gog/not installed/", StringComparison.OrdinalIgnoreCase) >= 0));

                try
                {
                    if (isGogContext && cfg.GetBoolean("gog_enable_install_automation", false))
                    {
                        string gameId = null;
                        var lnkPath = args.FirstOrDefault(a => a.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
                        if (lnkPath != null && File.Exists(lnkPath))
                        {
                            try
                            {
                                var type = Type.GetTypeFromProgID("WScript.Shell");
                                dynamic shell = Activator.CreateInstance(type);
                                var shortcut = shell.CreateShortcut(lnkPath);
                                string arguments = shortcut.Arguments;
                                Marshal.FinalReleaseComObject(shortcut);
                                Marshal.FinalReleaseComObject(shell);

                                var match = Regex.Match(arguments ?? "", @"/gameId=(\d+)");
                                if (match.Success)
                                {
                                    gameId = match.Groups[1].Value;
                                    installLogger.Log($"[GOG] Parsed gameId '{gameId}' from shortcut '{Path.GetFileName(lnkPath)}'.");
                                }
                            }
                            catch (Exception ex)
                            {
                                installLogger.Log($"[GOG] Failed to read .lnk file '{lnkPath}': {ex.Message}");
                            }
                        }

                        if (!string.IsNullOrEmpty(gameId))
                        {
                            GogInstallerAutomation.TryInstallFirstGame(cfg, installLogger, gameId);
                        }
                        else
                        {
                            installLogger.Log("[GOG] Could not determine gameId for installation.");
                        }
                        return;
                    }
                    if (isSteamContext && steamEnabled)
                    {
                        SteamInstallerAutomation.TryInstallFirstGame(cfg, installLogger);
                        return;
                    }
                    if (isAmazonContext && amazonEnabled)
                    {
                        Amazon.AmazonInstallerAutomation.TryInstallFirstGame(cfg, installLogger, args);
                        return;
                    }
                    if (isEpicContext && epicEnabled)
                    {
                        EpicInstallerAutomation.TryInstallFirstGame(cfg, installLogger, args);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    installLogger.Log($"[InstallAutomation] Unexpected error: {ex.Message}");
                }
                // No supported context or disabled; exit quietly
                return;
            }

            var logger = new SimpleLogger("last_scan.log");
            // Debug: capture raw command line and args for normal mode
            try
            {
                logger.Debug($"[Boot] CommandLine: {Environment.CommandLine}");
                logger.Debug($"[Boot] Args: {string.Join(" ", args ?? Array.Empty<string>())}");
            }
            catch { }
            logger.Log("Game Store Library Manager");
            logger.Log("--------------------------");

            // Determine special modes early to avoid showing the splash for direct-run modes
            bool isLunaMode = args != null && args.Any(a => string.Equals(a, "-luna", StringComparison.OrdinalIgnoreCase));
            bool isAuthUiMode = args != null && args.Length >= 1 && string.Equals(args[0], "authui", StringComparison.OrdinalIgnoreCase);
            bool isMenuMode = args != null && args.Any(a => string.Equals(a, "-menu", StringComparison.OrdinalIgnoreCase));

            if (isMenuMode)
            {
                logger.Log("'-menu' mode requested. Opening settings menu.");
                LaunchMenuForm();
                return;
            }

            // First launch check: if config doesn't exist, force open the menu.
            if (!File.Exists(PathManager.ConfigFilePath))
            {
                logger.Log("First launch: Configuration file not found. Opening settings menu to initialize.");
                var result = LaunchMenuForm();

                // If the user cancelled the initial setup, exit the application.
                if (result != DialogResult.OK)
                {
                    logger.Log("Initial setup was cancelled. Exiting.");
                    return;
                }
                logger.Log("Initial configuration saved.");
            }

            // Early config read to decide on Luna enablement and cleanup any leftover .bat when disabled
            var earlyConfig = new Config();
            bool lunaEnabled = earlyConfig.GetBoolean("enable_luna", false);
            if (!lunaEnabled)
            {
                // Remove leftover Luna shortcut(s) if present in Amazon roms directories
                try
                {
                    var baseName = StringUtils.SanitizeFileName("Amazon Luna");
                    var installedBat = Path.Combine(PathManager.AmazonRomsPath, "." + baseName + ".bat");
                    var installedBatNoDot = Path.Combine(PathManager.AmazonRomsPath, baseName + ".bat");
                    var notInstalledDir = Path.Combine(PathManager.AmazonRomsPath, "Not Installed");
                    var notInstalledBat = Path.Combine(notInstalledDir, "." + baseName + ".bat");
                    var notInstalledBatNoDot = Path.Combine(notInstalledDir, baseName + ".bat");
                    foreach (var p in new[] { installedBat, installedBatNoDot, notInstalledBat, notInstalledBatNoDot })
                    {
                        if (File.Exists(p))
                        {
                            try { File.Delete(p); logger.Log($"[Luna] Deleted disabled Luna shortcut: {Path.GetFileName(p)}"); } catch { }
                        }
                    }
                }
                catch { }
            }

            // Cleanup GSLM shortcut if disabled
            bool gslmShortcutEnabled = earlyConfig.GetBoolean("create_gslm_shortcut", true);
            if (!gslmShortcutEnabled)
            {
                // Remove leftover GSLM shortcut if present
                try
                {
                    var baseName = StringUtils.SanitizeFileName(".GSLM Settings");
                    var storePaths = new[] { PathManager.SteamRomsPath, PathManager.EpicRomsPath, PathManager.GogRomsPath, PathManager.AmazonRomsPath };

                    foreach (var storePath in storePaths)
                    {
                        if (string.IsNullOrEmpty(storePath) || !Directory.Exists(storePath)) continue;

                        var installedBat = Path.Combine(storePath, baseName + ".bat");
                        var notInstalledDir = Path.Combine(storePath, "Not Installed");
                        var notInstalledBat = Path.Combine(notInstalledDir, baseName + ".bat");

                        foreach (var p in new[] { installedBat, notInstalledBat })
                        {
                            if (File.Exists(p))
                            {
                                try { File.Delete(p); logger.Log($"[GSLM] Deleted disabled GSLM shortcut: {p}"); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            // Start splash overlay (centered, borderless, semi-transparent) only for normal startup
            GameStoreLibraryManager.Common.SplashOverlay splash = null;
            if (!isLunaMode && !isAuthUiMode)
            {
                splash = GameStoreLibraryManager.Common.SplashOverlay.Start("Loading online store library...", 0);
            }
            try
            {
            // Prerequisites check: .NET 8 Desktop Runtime and WebView2 Runtime
            try
            {
                bool needDotnet = !PrereqChecker.HasDotnet8DesktopRuntime();
                bool needWebView2 = !PrereqChecker.HasWebView2Runtime();
                if (needDotnet || needWebView2)
                {
                    logger.Log("Missing prerequisites detected. Opening download pages in your browser...");
                    try
                    {
                        if (needDotnet)
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime") { UseShellExecute = true });
                    }
                    catch { }
                    try
                    {
                        if (needWebView2)
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section") { UseShellExecute = true });
                    }
                    catch { }
                    return;
                }
            }
            catch { }

            // Special mode: Luna kiosk browser (only if enabled in config)
            if (isLunaMode)
            {
                if (!lunaEnabled)
                {
                    logger.Log("[Luna] '-luna' mode requested but enable_luna=false. Command disabled. Exiting.");
                    return;
                }
                // Default to windowed unless -fullscreen is provided. Allow -windowed to explicitly override.
                bool fullscreen = args.Any(a => string.Equals(a, "-fullscreen", StringComparison.OrdinalIgnoreCase))
                                   && !args.Any(a => string.Equals(a, "-windowed", StringComparison.OrdinalIgnoreCase));
                var t = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var form = new GameStoreLibraryManager.Luna.LunaBrowserForm(fullscreen: fullscreen);
                    Application.Run(form);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return;
            }

            // CLI helper: launch embedded auth UI and exit
            if (isAuthUiMode)
            {
                var store = args.Length >= 2 ? args[1] : null;
                if (string.IsNullOrWhiteSpace(store))
                {
                    logger.Log("Usage: GameStoreLibraryManager authui <steam|amazon|gog|epic>");
                    return;
                }
                logger.Log($"Launching embedded auth UI for '{store}'...");
                var code = AuthUiLauncher.Run(store);
                logger.Log($"Auth UI closed with code {code}.");
                return;
            }

            if (splash != null) splash.SetProgress(10);
            var config = earlyConfig; // reuse already-loaded config

            var epicLibrary = new EpicLibrary(config, logger);
            var steamLibrary = new SteamLibrary(config, logger);
            var gogLibrary = new GogLibrary(config, logger);
            var amazonLibrary = new AmazonLibrary(config, logger);

            logger.Log("\nFetching game libraries (parallel)...");
            if (splash != null) splash.SetProgress(25);
            List<LauncherGameInfo> epicGames = null, steamGames = null, gogGames = null, amazonGames = null;
            var epicTask = Task.Run(async () =>
            {
                try { epicGames = (await epicLibrary.GetAllGamesAsync()).ToList(); }
                catch (Exception ex) { logger.Log($"[Epic] Fetch failed: {ex.Message}"); epicGames = new List<LauncherGameInfo>(); }
            });
            var steamTask = Task.Run(async () =>
            {
                try { steamGames = (await steamLibrary.GetAllGamesAsync()).ToList(); }
                catch (Exception ex) { logger.Log($"[Steam] Fetch failed: {ex.Message}"); steamGames = new List<LauncherGameInfo>(); }
            });
            var gogTask = Task.Run(async () =>
            {
                try { gogGames = (await gogLibrary.GetAllGamesAsync()).ToList(); }
                catch (Exception ex) { logger.Log($"[GOG] Fetch failed: {ex.Message}"); gogGames = new List<LauncherGameInfo>(); }
            });
            var amazonTask = Task.Run(async () =>
            {
                try { amazonGames = (await amazonLibrary.GetAllGamesAsync()).ToList(); }
                catch (Exception ex) { logger.Log($"[Amazon] Fetch failed: {ex.Message}"); amazonGames = new List<LauncherGameInfo>(); }
            });

            await Task.WhenAll(epicTask, steamTask, gogTask, amazonTask);
            if (splash != null) splash.SetProgress(60);

            var allGames = new List<LauncherGameInfo>();
            allGames.AddRange(epicGames);
            allGames.AddRange(steamGames);
            allGames.AddRange(gogGames);
            allGames.AddRange(amazonGames);

            // Append synthetic Amazon Luna entry (installed) if enabled. ShortcutManager will create a .bat launcher.
            if (config.GetBoolean("enable_luna", false))
            {
                var lunaGame = new LauncherGameInfo
                {
                    Id = "LUNA",
                    Name = "Amazon Luna",
                    IsInstalled = true,
                    Launcher = "Amazon",
                    LauncherUrl = "internal://luna"
                };
                // Ensure it flows into shortcut reconciliation AND media scraping/gamelist generation
                amazonGames.Add(lunaGame);
                allGames.Add(lunaGame);
                logger.Log("[Amazon] Added synthetic 'Amazon Luna' entry.");
            }

            // Append synthetic GSLM Settings entry if enabled.
            if (config.GetBoolean("create_gslm_shortcut", true))
            {
                var stores = new Dictionary<string, List<LauncherGameInfo>>
                {
                    { "Steam", steamGames },
                    { "Epic", epicGames },
                    { "GOG", gogGames },
                    { "Amazon", amazonGames }
                };

                foreach (var store in stores)
                {
                    var settingsShortcut = new LauncherGameInfo
                    {
                        Id = $"GSLM_SETTINGS_{store.Key}",
                        Name = ".GSLM Settings",
                        IsInstalled = true,
                        Launcher = store.Key,
                        LauncherUrl = $"internal://gslm-settings"
                    };
                    store.Value.Add(settingsShortcut);
                    allGames.Add(settingsShortcut);
                    logger.Log($"[{store.Key}] Added synthetic '.GSLM Settings' entry.");
                }
            }

            logger.Log($"Found {allGames.Count} total games.");

            logger.Log("\nReconciling shortcuts...");
            if (splash != null) splash.SetProgress(70);
            var newGameLookup = allGames.ToDictionary(g => g.Launcher + "_" + g.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var existingShortcut in ShortcutManager.GetAllExistingShortcuts(config))
            {
                if (!newGameLookup.TryGetValue(existingShortcut.Launcher + "_" + existingShortcut.GameId, out var game) || game.IsInstalled != existingShortcut.IsInstalled)
                {
                    // This shortcut is considered stale. However, we must not delete it if it's an installed game
                    // and the user has chosen to manage installed games for this store outside of this tool.
                    if (existingShortcut.IsInstalled)
                    {
                        var importFlag = $"{existingShortcut.Launcher.ToLower()}_import_installed";
                        if (!config.GetBoolean(importFlag, true))
                        {
                            logger.Log($"  Skipping deletion of installed game shortcut '{Path.GetFileName(existingShortcut.FilePath)}' because '{importFlag}' is false.");
                            continue;
                        }
                    }

                    try
                    {
                        File.Delete(existingShortcut.FilePath);
                        logger.Log($"- Deleted stale shortcut: {Path.GetFileName(existingShortcut.FilePath)}");
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"  [Error] Could not delete stale shortcut {existingShortcut.FilePath}: {ex.Message}");
                    }
                }
            }

            if (allGames.Any())
            {
                logger.Log("\nCreating new shortcuts...");
                foreach (var game in allGames)
                {
                    if (game.IsInstalled)
                    {
                        var importFlag = $"{game.Launcher.ToLower()}_import_installed";
                        if (!config.GetBoolean(importFlag, true))
                        {
                            logger.Log($"  Skipping shortcut creation for installed game '{game.Name}' because '{importFlag}' is false.");
                            continue;
                        }
                    }

                    try
                    {
                        ShortcutManager.CreateShortcut(game, config);
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"  [Error] Could not create shortcut for {game.Name}: {ex.Message}");
                    }
                }
                logger.Log("Shortcut management complete.");
                if (splash != null) splash.SetProgress(80);
            }

            if (config.GetBoolean("scrape_media", false))
            {
                logger.Log("\nScraping media...");
                var scraper = new HfsPlayScraper();
                var gamelistGenerator = new GamelistGenerator(config, logger);
                var downloader = new MediaDownloader(logger);
                await ScrapeMediaForLibrary(epicGames, scraper, gamelistGenerator, downloader, PathManager.EpicRomsPath, logger, config, epicLibrary.CurrentToken?.AccessToken);
                await ScrapeMediaForLibrary(steamGames, scraper, gamelistGenerator, downloader, PathManager.SteamRomsPath, logger, config);
                await ScrapeMediaForLibrary(gogGames, scraper, gamelistGenerator, downloader, PathManager.GogRomsPath, logger, config);
                await ScrapeMediaForLibrary(amazonGames, scraper, gamelistGenerator, downloader, PathManager.AmazonRomsPath, logger, config);
                logger.Log("Media scraping complete.");
                if (splash != null) splash.SetProgress(100);
            }
            else
            {
                if (splash != null) splash.SetProgress(100);
            }
            }
            finally
            {
                try { if (splash != null) splash.Close(); } catch { }
            }
        }

        private static void ProcessSyntheticMedia(GameDetails details, string romsPath, string baseName)
        {
            var imagesDir = Path.Combine(romsPath, "images");
            var videosDir = Path.Combine(romsPath, "videos");

            void TryCopyAsset(string mediaType)
            {
                var extension = (mediaType == "video") ? "mp4" : "png";
                var sourceFileName = $"{baseName}-{mediaType}.{extension}";
                var destFileName = sourceFileName;

                var destDir = (mediaType == "video") ? videosDir : imagesDir;
                var src = Path.Combine(AppContext.BaseDirectory, "img", sourceFileName);
                var dest = Path.Combine(destDir, destFileName);

                if (!File.Exists(src)) return;

                if (!File.Exists(dest))
                {
                    try
                    {
                        Directory.CreateDirectory(destDir);
                        File.Copy(src, dest);
                    }
                    catch { }
                }

                if (!details.MediaUrls.ContainsKey(mediaType))
                {
                    var subdir = (mediaType == "video") ? "videos" : "images";
                    details.MediaUrls[mediaType] = $"./{subdir}/{destFileName}";
                }
            }

            TryCopyAsset("marquee");
            TryCopyAsset("fanart");
            TryCopyAsset("image");
            TryCopyAsset("thumb");
            TryCopyAsset("video");
        }

        static async Task ScrapeMediaForLibrary(List<LauncherGameInfo> games, HfsPlayScraper hfsScraper, GamelistGenerator gamelistGenerator, MediaDownloader downloader, string romsPath, SimpleLogger logger, Config config, string accessToken = null)
        {
            if (games == null || !games.Any()) return;
            logger.Log($"Scraping media for {games.Count} games in {romsPath}");
            var allGameDetails = new Dictionary<string, GameDetails>();
            var steamScraper = new SteamStoreScraper();
            var forceSteamFirst = config.GetBoolean("force_steam_first", false);

            // Load existing gamelist data to make intelligent scraping decisions
            var gamelistPath = Path.Combine(romsPath, "gamelist.xml");
            var existingGamelistEntries = new Dictionary<string, GamelistEntry>();
            if (File.Exists(gamelistPath))
            {
                try
                {
                    var doc = XDocument.Load(gamelistPath);
                    var requiredTags = new[] { "desc", "image", "video", "marquee", "fanart" };
                    foreach (var gameElement in doc.Descendants("game"))
                    {
                        var name = gameElement.Element("name")?.Value;
                        var path = gameElement.Element("path")?.Value;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                        {
                            var entry = new GamelistEntry
                            {
                                Name = name,
                                Path = path,
                                IsInstalled = !path.Contains("Not Installed"),
                                IsComplete = requiredTags.All(tag => gameElement.Element(tag) != null && !string.IsNullOrEmpty(gameElement.Element(tag).Value))
                            };
                            existingGamelistEntries[StringUtils.NormalizeName(name)] = entry;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"[Gamelist] Failed to parse existing gamelist.xml: {ex.Message}");
                }
            }

            bool forceRescrapeIncomplete = config.GetBoolean("rescrape_incomplete_games", false);

            foreach (var game in games)
            {
                if (string.IsNullOrEmpty(game.Name))
                {
                    continue;
                }

                // Special case: GSLM Settings synthetic entry
                if (game.Name == ".GSLM Settings")
                {
                    logger.Log("  Processing '.GSLM Settings' (synthetic entry)...");
                    var details = new GameDetails
                    {
                        Name = "zz.Game StoreLibrary Manager",
                        Description = "GSLM - settings menu for Game Store Library Manager, a store scraper for Steam, Epic, GOG and Amazon for RetroBat.",
                        Developer = "Aynshe",
                        Publisher = "Aynshe",
                        ReleaseDate = "2025-08-06",
                        MediaUrls = new Dictionary<string, string>()
                    };
                    ProcessSyntheticMedia(details, romsPath, StringUtils.SanitizeFileName(game.Name));
                    allGameDetails[game.Id] = details;
                    continue;
                }

                // Special case: Amazon Luna synthetic entry
                if (game.Launcher == "Amazon" && game.Id == "LUNA")
                {
                    logger.Log("  Processing 'Amazon Luna' (synthetic entry)...");
                    var details = new GameDetails
                    {
                        Description = "Amazon Luna is Amazon's cloud gaming service that lets you stream a selection of games without local installation.",
                        Developer = "Amazon",
                        Publisher = "Amazon",
                        ReleaseDate = "1 mars 2022",
                        MediaUrls = new Dictionary<string, string>()
                    };
                    ProcessSyntheticMedia(details, romsPath, "." + StringUtils.SanitizeFileName(game.Name));
                    var copiedKeys = string.Join(", ", details.MediaUrls.Keys);
                    logger.Log($"    Luna media prepared: [{copiedKeys}]\n    Description + ReleaseDate set.");
                    allGameDetails[game.Id] = details;
                    continue;
                }

                // INTELLIGENT SCRAPE LOGIC
                if (existingGamelistEntries.TryGetValue(StringUtils.NormalizeName(game.Name), out var existingEntry))
                {
                    bool isStatusUnchanged = existingEntry.IsInstalled == game.IsInstalled;
                    bool shouldSkip = isStatusUnchanged && (existingEntry.IsComplete || !forceRescrapeIncomplete);

                    if (shouldSkip)
                    {
                        logger.Log($"  Skipping '{game.Name}' (status unchanged and metadata is complete or rescrape is disabled).");
                        continue;
                    }
                }

                logger.Log($"  Processing '{game.Name}'...");
                try
                {
                    GameDetails gameDetails = null;

                    var canScrapeOnSteam = game.Launcher == "Steam" || game.Launcher == "Epic" || game.Launcher == "GOG" || game.Launcher == "Amazon";

                    if (forceSteamFirst && canScrapeOnSteam)
                    {
                        logger.Log($"  [Force Steam First] Trying Steam Store for '{game.Name}'.");
                        if (game.Launcher == "Steam")
                        {
                            gameDetails = await steamScraper.GetGameDetails(game.Id);
                        }
                        else // Epic or GOG
                        {
                            var steamAppId = await steamScraper.FindGameByName(game.Name, logger);
                            if (!string.IsNullOrEmpty(steamAppId))
                            {
                                gameDetails = await steamScraper.GetGameDetails(steamAppId);
                            }
                        }

                        if (gameDetails == null)
                        {
                            logger.Log($"  Steam Store failed or no match found. Falling back to HFSPlay for '{game.Name}'.");
                            gameDetails = await ScrapeHfsPlay(game, hfsScraper, logger);
                        }
                    }
                    else
                    {
                        // Original logic: HFSPlay first
                        gameDetails = await ScrapeHfsPlay(game, hfsScraper, logger);
                        if (gameDetails == null && canScrapeOnSteam)
                        {
                            logger.Log($"  No suitable match found for '{game.Name}' on HFSPlay. Falling back to Steam Store API.");
                            if (game.Launcher == "Steam")
                            {
                                gameDetails = await steamScraper.GetGameDetails(game.Id);
                            }
                            else // Epic or GOG
                            {
                                var steamAppId = await steamScraper.FindGameByName(game.Name, logger);
                                if (!string.IsNullOrEmpty(steamAppId))
                                {
                                    gameDetails = await steamScraper.GetGameDetails(steamAppId);
                                }
                            }
                        }
                    }

                    if (gameDetails != null)
                    {
                        var mediaPaths = new Dictionary<string, string>();
                        foreach (var mediaEntry in gameDetails.MediaUrls)
                        {
                            var mediaType = mediaEntry.Key;
                            var mediaUrl = mediaEntry.Value;
                            var baseFileName = $"{StringUtils.SanitizeFileName(game.Name)}-{mediaType}";
                            var subdirectory = (mediaType == "video") ? "videos" : "images";
                            var baseFilePath = Path.Combine(romsPath, subdirectory, baseFileName);

                            var finalFilePath = await downloader.DownloadMedia(mediaUrl, baseFilePath);
                            if (!string.IsNullOrEmpty(finalFilePath))
                            {
                                mediaPaths[mediaType] = $"./{subdirectory}/{Path.GetFileName(finalFilePath)}";
                            }
                        }
                        gameDetails.MediaUrls = mediaPaths;
                        allGameDetails[game.Id] = gameDetails;
                        logger.Log($"    Scraped details for {game.Name}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"[Error] Could not scrape or download media for {game.Name}: {ex.Message}");
                }
            }

            // We need to run the generator even if no new details were scraped, to fix paths and de-duplicate
            var generatedDoc = gamelistGenerator.GenerateGamelist(romsPath, games, allGameDetails);
            var tempPath = Path.GetTempFileName();
            generatedDoc.Save(tempPath);
            var finalPath = Path.Combine(romsPath, "gamelist.xml");
            File.Move(tempPath, finalPath, true);
            logger.Log($"Generated gamelist.xml for {romsPath}");
        }

        private static async Task<GameDetails> ScrapeHfsPlay(LauncherGameInfo game, HfsPlayScraper scraper, SimpleLogger logger)
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

        private static bool IsModernPlatform(string systemName)
        {
            if (string.IsNullOrEmpty(systemName)) return false;
            var lowerSystemName = systemName.ToLower();
            var modernKeywords = new[] { "pc", "playstation 3", "playstation 4", "playstation 5", "xbox 360", "xbox one", "xbox series", "wii u", "switch" };
            return modernKeywords.Any(keyword => lowerSystemName.Contains(keyword));
        }

        public static int LevenshteinDistance(string s, string t)
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

        private static DialogResult LaunchMenuForm()
        {
            DialogResult result = DialogResult.Cancel;
            var menuThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var menuForm = new MenuForm();
                result = menuForm.ShowDialog();
            });
            menuThread.SetApartmentState(ApartmentState.STA);
            menuThread.Start();
            menuThread.Join();
            return result;
        }
    }
}
