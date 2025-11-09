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
using GameDetails = GameStoreLibraryManager.Common.GameDetails;
using GameStoreLibraryManager.Epic;
using GameStoreLibraryManager.Gog;
using GameStoreLibraryManager.HfsPlay;
using GameStoreLibraryManager.Steam;
using GameStoreLibraryManager.Xbox;
using GameStoreLibraryManager.Auth;
using GameStoreLibraryManager.Menu;
using System.IO.Pipes;
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
            // The -esreload command is a lightweight client that must run even if another instance is active.
            bool isSignalClient = args != null && args.Any(a => string.Equals(a, "-esreload", StringComparison.OrdinalIgnoreCase));
            if (isSignalClient)
            {
                // Don't acquire the mutex, just run the async main method and exit.
                MainAsync(args).GetAwaiter().GetResult();
                return;
            }

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
            if (args != null && args.Any(a => string.Equals(a, "-esreload", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var message = string.Join(" ", args.Where(a => !a.Equals("-esreload", StringComparison.OrdinalIgnoreCase)));
                    var pipeName = $"GSLM_ReloadSignalPipe_{Environment.UserName}";

                    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                    {
                        // Timeout of 5 seconds to connect to the server
                        client.Connect(5000);
                        using (var writer = new StreamWriter(client))
                        {
                            writer.Write(message);
                            writer.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log to a file in the user directory if something goes wrong, as console output might not be visible.
                    var errorLogPath = Path.Combine(PathManager.UserDataPath, "esreload_client_error.log");
                    File.AppendAllText(errorLogPath, $"[{DateTime.Now}] Error in -esreload client: {ex.Message}\n");
                }
                return; // Always exit after attempting to send the signal
            }

            // Special mode: installation automation (e.g., called from EmulationStation with %* and -installstore)
            if (args != null && args.Any(a => string.Equals(a, "-installstore", StringComparison.OrdinalIgnoreCase)))
            {
                var installLogger = new SimpleLogger("install_automation.log");
                installLogger.Log("[Boot] Logger initialized.");
                try
                {
                    installLogger.Debug($"[Boot] CommandLine: {Environment.CommandLine}");
                    installLogger.Debug($"[Boot] Args: {string.Join(" ", args ?? Array.Empty<string>())}");

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
                    }
                    else if (isSteamContext && steamEnabled)
                    {
                        SteamInstallerAutomation.TryInstallFirstGame(cfg, installLogger);
                    }
                    else if (isAmazonContext && amazonEnabled)
                    {
                        await Amazon.AmazonInstallerAutomation.TryInstallFirstGame(cfg, installLogger, args);
                    }
                    else if (isEpicContext && epicEnabled)
                    {
                        await EpicInstallerAutomation.TryInstallFirstGame(cfg, installLogger, args);
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
            
            // Force recreation of internal .bat files IF they point to the old executable
            try
            {
                var filesToScan = new[]
                {
                    ".GSLM Settings.bat",
                    ".Amazon Luna.bat",
                    "Amazon Luna.bat",
                    ".Xbox Cloud Gaming.bat"
                };

                var romsPaths = new[]
                {
                    PathManager.SteamRomsPath,
                    Path.Combine(PathManager.SteamRomsPath, "Not Installed"),
                    PathManager.EpicRomsPath,
                    Path.Combine(PathManager.EpicRomsPath, "Not Installed"),
                    PathManager.GogRomsPath,
                    Path.Combine(PathManager.GogRomsPath, "Not Installed"),
                    PathManager.AmazonRomsPath,
                    Path.Combine(PathManager.AmazonRomsPath, "Not Installed"),
                    PathManager.XboxRomsPath,
                    Path.Combine(PathManager.XboxRomsPath, "Not Installed")
                };

                foreach (var path in romsPaths)
                {
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
                    foreach (var file in filesToScan)
                    {
                        var filePath = Path.Combine(path, file);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                var content = File.ReadAllText(filePath);
                                // Delete the file only if it contains a call to the old executable name
                                if (content.IndexOf("GameStoreLibraryManager.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    File.Delete(filePath);
                                    logger.Log($"[Cleanup] Deleted outdated shortcut to force recreation: {file}");
                                }
                            }
                            catch {}
                        }
                    }
                }
            }
            catch {}

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
            bool isXboxCloudGamingMode = args != null && args.Any(a => string.Equals(a, "-xboxcloudgaming", StringComparison.OrdinalIgnoreCase));
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

            // Early config read to decide on feature enablement and cleanup any leftover .bat when disabled
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

            bool xboxCloudGamingEnabled = earlyConfig.GetBoolean("enable_xbox_cloud_gaming", false);
            if (!xboxCloudGamingEnabled)
            {
                // Remove leftover Xbox Cloud Gaming shortcut(s) if present in Xbox roms directories
                try
                {
                    var baseName = StringUtils.SanitizeFileName(".Xbox Cloud Gaming");
                    var installedBat = Path.Combine(PathManager.XboxRomsPath, baseName + ".bat");
                    var notInstalledDir = Path.Combine(PathManager.XboxRomsPath, "Not Installed");
                    var notInstalledBat = Path.Combine(notInstalledDir, baseName + ".bat");
                    foreach (var p in new[] { installedBat, notInstalledBat })
                    {
                        if (File.Exists(p))
                        {
                            try { File.Delete(p); logger.Log($"[XboxCloud] Deleted disabled Xbox Cloud Gaming shortcut: {Path.GetFileName(p)}"); } catch { }
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
            if (!isLunaMode && !isXboxCloudGamingMode && !isAuthUiMode)
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

                string launchGameId = null;
                var launchIndex = Array.FindIndex(args, a => string.Equals(a, "-launch", StringComparison.OrdinalIgnoreCase));
                if (launchIndex != -1 && launchIndex + 1 < args.Length)
                {
                    launchGameId = args[launchIndex + 1];
                }

                var t = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var form = new GameStoreLibraryManager.Luna.LunaBrowserForm(fullscreen: fullscreen, gameId: launchGameId);
                    Application.Run(form);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return;
            }

            // Special mode: Xbox Cloud Gaming kiosk browser (only if enabled in config)
            if (isXboxCloudGamingMode)
            {
                if (!xboxCloudGamingEnabled)
                {
                    logger.Log("[XboxCloud] '-xboxcloudgaming' mode requested but enable_xbox_cloud_gaming=false. Command disabled. Exiting.");
                    return;
                }
                // Default to windowed unless -fullscreen is provided. Allow -windowed to explicitly override.
                bool fullscreen = args.Any(a => string.Equals(a, "-fullscreen", StringComparison.OrdinalIgnoreCase))
                                   && !args.Any(a => string.Equals(a, "-windowed", StringComparison.OrdinalIgnoreCase));

                string launchGameId = null;
                var launchIndex = Array.FindIndex(args, a => string.Equals(a, "-launch", StringComparison.OrdinalIgnoreCase));
                if (launchIndex != -1 && launchIndex + 1 < args.Length)
                {
                    launchGameId = args[launchIndex + 1];
                }

                var t = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var form = new GameStoreLibraryManager.Xbox.XboxCloudGamingForm(fullscreen: fullscreen, gameId: launchGameId);
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
            var xboxLibrary = new XboxLibrary(config, logger);

            logger.Log("\nFetching game libraries (parallel)...");
            if (splash != null) splash.SetProgress(25);
            List<LauncherGameInfo> epicGames = null, steamGames = null, gogGames = null, amazonGames = null, xboxGames = null;
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
            var xboxTask = Task.Run(async () =>
            {
                if (config.GetBoolean("enable_xbox_library", false))
                {
                    xboxGames = await FetchXboxGamesWithAuthRetryAsync(xboxLibrary, logger, config);
                }
                else
                {
                    xboxGames = new List<LauncherGameInfo>();
                    logger.Log("[Xbox] Library is disabled in config. Skipping.");
                }
            });

            await Task.WhenAll(epicTask, steamTask, gogTask, amazonTask, xboxTask);
            if (splash != null) splash.SetProgress(60);

            var allGames = new List<LauncherGameInfo>();
            allGames.AddRange(epicGames);
            allGames.AddRange(steamGames);
            allGames.AddRange(gogGames);
            allGames.AddRange(amazonGames);
            allGames.AddRange(xboxGames);

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
                // Skip cleaning for any dynamically created cloud games, as they aren't in the initial `allGames` list.
                if (existingShortcut.FilePath.Contains(Path.DirectorySeparatorChar + "Cloud Games" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

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
                var gamelistGenerator = new GamelistGenerator(config, logger);
                await ScrapeMediaForLibrary(epicGames, gamelistGenerator, PathManager.EpicRomsPath, logger, config);
                await ScrapeMediaForLibrary(steamGames, gamelistGenerator, PathManager.SteamRomsPath, logger, config);
                await ScrapeMediaForLibrary(gogGames, gamelistGenerator, PathManager.GogRomsPath, logger, config);
                await ScrapeMediaForLibrary(amazonGames, gamelistGenerator, PathManager.AmazonRomsPath, logger, config);
                await ScrapeMediaForLibrary(xboxGames, gamelistGenerator, PathManager.XboxRomsPath, logger, config);
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

        static async Task ScrapeMediaForLibrary(List<LauncherGameInfo> games, GamelistGenerator gamelistGenerator, string romsPath, SimpleLogger logger, Config config)
        {
            if (games == null || !games.Any()) return;
            logger.Log($"Scraping media for {games.Count} games in {romsPath}");
            var allGameDetails = new Dictionary<string, GameDetails>();
            var mediaScraper = new MediaScraper(config, logger);

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

                // Special case: Xbox Cloud Gaming synthetic entry
                if (game.Launcher == "Xbox" && game.Id == "XBOX_CLOUD_GAMING")
                {
                    logger.Log("  Processing '.Xbox Cloud Gaming' (synthetic entry)...");
                    var details = new GameDetails
                    {
                        Description = "Xbox Cloud Gaming (Beta) lets you play hundreds of console games on any device.",
                        Developer = "Microsoft",
                        Publisher = "Microsoft",
                        ReleaseDate = "15 septembre 2020",
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

                var gameDetails = await mediaScraper.ScrapeGameAsync(game, romsPath);
                if (gameDetails != null)
                {
                    allGameDetails[game.Id] = gameDetails;
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

        private static async Task<List<LauncherGameInfo>> FetchXboxGamesWithAuthRetryAsync(XboxLibrary xboxLibrary, SimpleLogger logger, Config config)
        {
            try
            {
                return (await xboxLibrary.GetAllGamesAsync()).ToList();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Xbox tokens not found"))
                {
                    logger.Log("[Xbox] Login required. Launching authentication UI...");
                    var result = AuthUiLauncher.Run("xbox");
                    if (result == 0)
                    {
                        logger.Log("[Xbox] Auth UI complete. Exchanging code for tokens...");
                        try
                        {
                            var apiClient = new XboxAccountClient(config, logger);
                            await apiClient.Login();

                            logger.Log("[Xbox] Token exchange complete. Retrying game fetch...");
                            // After successful login, the tokens are created. Re-fetching should now succeed.
                            return (await xboxLibrary.GetAllGamesAsync()).ToList();
                        }
                        catch (Exception loginEx)
                        {
                            logger.Log($"[Xbox] Fetch failed after login attempt: {loginEx.Message}");
                        }
                    }
                    else
                    {
                        logger.Log("[Xbox] Authentication was cancelled or failed.");
                    }
                }
                else
                {
                    // Log other unexpected errors during the fetch process.
                    logger.Log($"[Xbox] Fetch failed with an unexpected error: {ex.Message}");
                }
                return new List<LauncherGameInfo>();
            }
        }
    }
}
