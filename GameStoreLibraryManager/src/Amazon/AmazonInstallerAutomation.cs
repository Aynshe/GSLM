using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using GameStoreLibraryManager.Common;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Data.SQLite;
using System.Net.Http;

namespace GameStoreLibraryManager.Amazon
{
    public static class AmazonInstallerAutomation
    {
        private static volatile bool s_AutomationCompleted = false;

        // Localized labels for the primary action button in Amazon Games installer dialog
        private static readonly string[] ContinueButtonNames = new[]
        {
            "Continue",      // en
            "Continuer",     // fr
            "Continuar",     // es/pt
            "Fortfahren",    // de
            "Continua",      // it
            "Продолжить",    // ru
            "继续",            // zh-cn
            "繼續",            // zh-tw
            "続行",            // ja
            "계속"             // ko
        };

        public static bool TryInstallFirstGame(Config config, SimpleLogger logger, string[] args)
        {
            s_AutomationCompleted = false;
            try
            {
                // Step 1: Find .bat file and extract game ID from it.
                string batPath = args?.FirstOrDefault(a => a.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(batPath) || !File.Exists(batPath))
                {
                    logger.Log($"[InstallAutomation][Amazon] Error: .bat file path not found in command line arguments.");
                    return false;
                }
                logger.Log($"[InstallAutomation][Amazon] Found .bat file: {batPath}");

                string gameId = null;
                try
                {
                    var lines = File.ReadAllLines(batPath);
                    foreach (var line in lines)
                    {
                        // Match either GUIDs or amzn1.adg.product IDs
                        var match = Regex.Match(line, @"amazon-games://install/([a-zA-Z0-9.-]+)");
                        if (match.Success)
                        {
                            gameId = match.Groups[1].Value;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"[InstallAutomation][Amazon] Error reading or parsing .bat file: {ex.Message}");
                }

                if (string.IsNullOrEmpty(gameId))
                {
                    logger.Log($"[InstallAutomation][Amazon] Error: Could not extract game ID from {batPath}.");
                    return false;
                }
                logger.Log($"[InstallAutomation][Amazon] Extracted Game ID: {gameId}");

                logger.Log("[InstallAutomation][Amazon] Scanning for 'Continue' button...");
                using var notice = InstallNoticeWindow.ShowTopMost(
                    "Please wait, automatic installation...",
                    autoClose: null,
                    keepShowingWhile: () => !s_AutomationCompleted);

                // Phase 1: Wait for Amazon Games to be foreground to avoid acting on other apps
                var fgDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                logger.Log("[InstallAutomation][Amazon] Waiting for Amazon Games to be foreground...");
                while (DateTime.UtcNow < fgDeadline)
                {
                    if (IsAmazonInForeground())
                    {
                        logger.Log("[InstallAutomation][Amazon] Amazon Games is foreground.");
                        break;
                    }
                    Thread.Sleep(250);
                }
                Thread.Sleep(5000); // Grace period for app to render

                var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
                while (DateTime.UtcNow < deadline)
                {
                    if (!IsAmazonProcessRunning())
                    {
                        logger.Log("[InstallAutomation][Amazon] Stopping: Amazon Games process not running.");
                        s_AutomationCompleted = true;
                        return false;
                    }

                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && IsWindowVisible(fg) && IsAmazonProcessName(GetProcessNameSafe(GetWindowProcessId(fg))))
                    {
                        string dumpDir = null;
                        try
                        {
                            dumpDir = Path.Combine(Path.GetTempPath(), "AmazonOCR", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                            Directory.CreateDirectory(dumpDir);
                            TempCleanup.RegisterPath(dumpDir);
                        }
                        catch (Exception ex) { logger.Log($"[InstallAutomation][Amazon][OCR] Failed to create dump dir: {ex.Message}"); }

                        bool clicked = OcrHelper.TryOcrClickInWindow(
                            fg,
                            ContinueButtonNames,
                            ClickScreen,
                            OcrButtonColor.Yellow,
                            msg => logger.Log($"[InstallAutomation][Amazon][OCR] {msg}"),
                            dumpDir
                        );

                        if (clicked)
                        {
                            logger.Log("[InstallAutomation][Amazon] Clicked 'Continue' button. Now monitoring for installation completion...");

                            // Reuse the existing notice window
                            notice.MoveToCenter();
                            notice.UpdateText("Installation in progress, please wait...");

                            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Amazon Games\Data\Games\Sql\GameInstallInfo.sqlite");
                            var installTimeout = DateTime.UtcNow + TimeSpan.FromMinutes(15);
                            bool isInstalled = false;

                            while (DateTime.UtcNow < installTimeout)
                            {
                                if (!IsAmazonProcessRunning())
                                {
                                    logger.Log("[InstallAutomation][Amazon] Amazon Games process closed unexpectedly during installation. Aborting.");
                                    s_AutomationCompleted = true;
                                    return false;
                                }

                                if (File.Exists(dbPath))
                                {
                                    try
                                    {
                                        using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;"))
                                        {
                                            conn.Open();
                                            using (var cmd = new SQLiteCommand("SELECT Installed FROM DbSet WHERE Id = @id", conn))
                                            {
                                                cmd.Parameters.AddWithValue("@id", gameId);
                                                var result = cmd.ExecuteScalar();
                                                if (result != null && (long)result == 1)
                                                {
                                                    logger.Log($"[InstallAutomation][Amazon] Detected game ID {gameId} with Installed=1 in the database. Installation complete.");
                                                    isInstalled = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Log($"[InstallAutomation][Amazon] Error querying SQLite DB: {ex.Message}");
                                    }
                                }
                                Thread.Sleep(TimeSpan.FromSeconds(5));
                            }

                            if (isInstalled)
                            {
                                notice.UpdateText("Game installed! Waiting for Amazon Games to close...");
                                Thread.Sleep(TimeSpan.FromSeconds(10));

                                try
                                {
                                    var processes = Process.GetProcesses().Where(p => IsAmazonProcessName(p.ProcessName));
                                    foreach (var process in processes)
                                    {
                                        logger.Log($"[InstallAutomation][Amazon] Closing process: {process.ProcessName} (ID: {process.Id})");
                                        process.Kill();
                                    }
                                }
                                catch (Exception ex) { logger.Log($"[InstallAutomation][Amazon] Error closing Amazon Games process: {ex.Message}"); }

                                if (config.GetBoolean("amazon_execute_game_after_install", false))
                                {
                                    logger.Log("[InstallAutomation][Amazon] 'amazon_execute_game_after_install' is true. Preparing to launch game.");
                                    var gameInfo = GetInstalledGameInfo(gameId, dbPath, logger);

                                    if (gameInfo != null)
                                    {
                                        notice.UpdateText("The game will now run");
                                        logger.Log("[InstallAutomation][Amazon] Displaying 'The game will now run' message.");
                                        Thread.Sleep(2000);

                                        logger.Log($"[InstallAutomation][Amazon] Creating shortcut for {gameInfo.Name}.");
                                        ShortcutManager.CreateShortcut(gameInfo, config);

                                        string sanitizedName = StringUtils.SanitizeFileName(gameInfo.Name);
                                        string shortcutPath = Path.Combine(PathManager.AmazonRomsPath, $"{sanitizedName}.url");

                                        if (File.Exists(shortcutPath))
                                        {
                                            try
                                            {
                                                using (var client = new HttpClient())
                                                {
                                                    // Reload game list first, as requested by the user
                                                    logger.Log("[InstallAutomation][Amazon] Sending reload request to http://127.0.0.1:1234/reloadgames");
                                                    var reloadResponse = client.GetAsync("http://127.0.0.1:1234/reloadgames").Result;
                                                    logger.Log($"[InstallAutomation][Amazon] Reload request sent. Status: {reloadResponse.StatusCode}");

                                                    logger.Log("[InstallAutomation][Amazon] Waiting 5 seconds before launching...");
                                                    Thread.Sleep(5000);

                                                    // Then, launch the game
                                                    logger.Log($"[InstallAutomation][Amazon] Launching game via HTTP POST: {shortcutPath}");
                                                    var content = new StringContent(shortcutPath, Encoding.UTF8, "text/plain");
                                                    // Using .Result here is a deliberate choice for simplicity in this synchronous context.
                                                    // The containing method is not async, and this is a fire-and-forget call at the end of the process.
                                                    var response = client.PostAsync("http://127.0.0.1:1234/launch", content).Result;
                                                    logger.Log($"[InstallAutomation][Amazon] Launch request sent. Status: {response.StatusCode}");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.Log($"[InstallAutomation][Amazon] Failed to send web request: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            logger.Log($"[InstallAutomation][Amazon] Error: Could not find created shortcut at {shortcutPath}");
                                        }
                                    }
                                    else
                                    {
                                        logger.Log($"[InstallAutomation][Amazon] Error: Could not retrieve game info for ID {gameId}. Cannot launch game.");
                                    }
                                }
                            }
                            else
                            {
                                logger.Log("[InstallAutomation][Amazon] Installation timed out.");
                            }

                            s_AutomationCompleted = true;
                            return isInstalled;
                        }
                    }
                    Thread.Sleep(300);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[InstallAutomation][Amazon] Error: {ex.Message}");
            }
            s_AutomationCompleted = true; // Ensure notice window closes on any error/timeout
            return false;
        }

        private static LauncherGameInfo GetInstalledGameInfo(string gameId, string dbPath, SimpleLogger logger)
        {
            if (!File.Exists(dbPath))
            {
                logger.Log($"[InstallAutomation][Amazon] GetInstalledGameInfo failed: DB not found at {dbPath}");
                return null;
            }

            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;"))
                {
                    connection.Open();
                    string sql = "SELECT ProductTitle, InstallDirectory FROM DbSet WHERE Id = @id AND Installed = 1;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@id", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new LauncherGameInfo
                                {
                                    Id = gameId,
                                    Name = reader["ProductTitle"] as string,
                                    InstallDirectory = reader["InstallDirectory"] as string,
                                    IsInstalled = true,
                                    Launcher = "Amazon",
                                    LauncherUrl = $"amazon-games://play/{gameId}"
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[InstallAutomation][Amazon] Error reading game info from SQLite DB: {ex.Message}");
            }

            return null;
        }

        private static int GetWindowProcessId(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                return unchecked((int)pid);
            }
            catch { return 0; }
        }

        private static string GetProcessNameSafe(int pid)
        {
            if (pid <= 0) return null;
            try { return Process.GetProcessById(pid)?.ProcessName; } catch { return null; }
        }

        private static bool IsAmazonProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            var n = processName.Trim();
            return n.IndexOf("amazon games", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAmazonInForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                int pid = GetWindowProcessId(fg);
                return IsAmazonProcessName(GetProcessNameSafe(pid));
            }
            catch { return false; }
        }

        private static bool IsAmazonProcessRunning()
        {
            try
            {
                return Process.GetProcesses().Any(p => IsAmazonProcessName(p.ProcessName));
            }
            catch { return false; }
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private static void ClickScreen(int x, int y)
        {
            try
            {
                System.Windows.Forms.Cursor.Position = new Point(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch { }
        }
    }
}
