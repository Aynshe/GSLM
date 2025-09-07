using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;
using Microsoft.Win32;
using System.Net.Http;

namespace GameStoreLibraryManager.Epic
{
    public static class EpicInstallerAutomation
    {
        private static volatile bool s_AutomationCompleted = false;

        private static readonly string[] InstallButtonNames = { "Install", "Installer", "Instalar", "Installieren", "Installa", "Установить", "安装", "安裝", "インストール", "설치" };

        public static async Task<bool> TryInstallFirstGame(Config config, SimpleLogger logger, string[] args)
        {
            s_AutomationCompleted = false;
            try
            {
                string gameId = ExtractGameId(args, logger);
                if (string.IsNullOrEmpty(gameId)) return false;

                logger.Log("[InstallAutomation][Epic] Scanning for 'Install' button...");
                using var notice = InstallNoticeWindow.ShowTopMost("Please wait, automatic installation...", autoClose: null, keepShowingWhile: () => !s_AutomationCompleted);

                if (!await WaitForEpicLauncherAsync(logger)) return false;

                bool clicked = false;
                var clickDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
                while (DateTime.UtcNow < clickDeadline && !clicked)
                {
                    if (!IsEpicProcessRunning())
                    {
                        logger.Log("[InstallAutomation][Epic] Epic Games Launcher process is not running. Aborting.");
                        s_AutomationCompleted = true;
                        return false;
                    }

                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && IsWindowVisible(fg) && IsEpicProcessName(GetProcessNameSafe(GetWindowProcessId(fg))))
                    {
                        string dumpDir = null;
                        try
                        {
                            dumpDir = Path.Combine(Path.GetTempPath(), "EpicOCR", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                            Directory.CreateDirectory(dumpDir);
                            TempCleanup.RegisterPath(dumpDir);
                        }
                        catch (Exception ex) { logger.Log($"[InstallAutomation][Epic][OCR] Failed to create dump dir: {ex.Message}"); }

                        clicked = OcrHelper.TryOcrClickInWindow(
                            fg,
                            InstallButtonNames,
                            ClickScreen,
                            OcrButtonColor.Blue,
                            msg => logger.Log($"[InstallAutomation][Epic][OCR] {msg}"),
                            dumpDir
                        );
                    }
                    if (!clicked) await Task.Delay(300);
                }

                if (!clicked)
                {
                    logger.Log("[InstallAutomation][Epic] Failed to find or click the 'Install' button via OCR.");
                    s_AutomationCompleted = true;
                    return false;
                }

                logger.Log("[InstallAutomation][Epic] 'Install' button clicked. Monitoring for installation completion...");
                notice.MoveToCenter();
                notice.UpdateText("Installation in progress, please wait...");

                bool isInstalled = await WaitForInstallCompletionAsync(gameId, logger);

                if (isInstalled)
                {
                    logger.Log($"[InstallAutomation][Epic] Game '{gameId}' successfully installed.");

                    notice.UpdateText("Game installed! Waiting for Epic Games to close...");
                    await Task.Delay(TimeSpan.FromSeconds(10));

                    try
                    {
                        var processes = Process.GetProcesses().Where(p => IsEpicProcessName(p.ProcessName));
                        foreach (var process in processes)
                        {
                            logger.Log($"[InstallAutomation][Epic] Closing process: {process.ProcessName} (ID: {process.Id})");
                            process.Kill();
                        }
                    }
                    catch (Exception ex) { logger.Log($"[InstallAutomation][Epic] Error closing Epic Games process: {ex.Message}"); }

                    if (config.GetBoolean("epic_execute_game_after_install", false))
                    {
                        logger.Log("[InstallAutomation][Epic] 'epic_execute_game_after_install' is true. Preparing to launch game.");
                        var gameInfo = GetInstalledGameInfo(gameId, logger);

                        if (gameInfo != null)
                        {
                            notice.UpdateText("The game will now run");
                            logger.Log("[InstallAutomation][Epic] Displaying 'The game will now run' message.");

                            logger.Log($"[InstallAutomation][Epic] Creating shortcut for {gameInfo.Name}.");
                            ShortcutManager.CreateShortcut(gameInfo, config);

                            string sanitizedName = StringUtils.SanitizeFileName(gameInfo.Name);
                            string shortcutPath = Path.Combine(PathManager.EpicRomsPath, $"{sanitizedName}.url");

                            if (File.Exists(shortcutPath))
                            {
                                try
                                {
                                    using (var client = new HttpClient())
                                    {
                                        logger.Log("[InstallAutomation][Epic] Sending reload request to http://127.0.0.1:1234/reloadgames");
                                        var reloadResponse = await client.GetAsync("http://127.0.0.1:1234/reloadgames");
                                        logger.Log($"[InstallAutomation][Epic] Reload request sent. Status: {reloadResponse.StatusCode}");

                                        logger.Log("[InstallAutomation][Epic] Waiting for EmulationStation signal...");
                                        var signalMessage = await ReloadSignalListener.WaitForSignalAsync(logger);

                                        if (signalMessage == null || !signalMessage.Contains("\"Not Installed\""))
                                        {
                                            logger.Log("[InstallAutomation][Epic] Did not receive the correct signal from ES. The game may not launch correctly.");
                                        }

                                        logger.Log($"[InstallAutomation][Epic] Launching game via HTTP POST: {shortcutPath}");
                                        var content = new StringContent(shortcutPath, Encoding.UTF8, "text/plain");
                                        var response = await client.PostAsync("http://127.0.0.1:1234/launch", content);
                                        logger.Log($"[InstallAutomation][Epic] Launch request sent. Status: {response.StatusCode}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.Log($"[InstallAutomation][Epic] Failed to send web request: {ex.Message}");
                                }
                            }
                            else
                            {
                                logger.Log($"[InstallAutomation][Epic] Error: Could not find created shortcut at {shortcutPath}");
                            }
                        }
                        else
                        {
                            logger.Log($"[InstallAutomation][Epic] Error: Could not retrieve game info for ID {gameId}. Cannot launch game.");
                        }
                    }
                }
                else
                {
                    logger.Log($"[InstallAutomation][Epic] Installation timed out for game '{gameId}'.");
                }

                s_AutomationCompleted = true;
                return isInstalled;
            }
            catch (Exception ex)
            {
                logger.Log($"[InstallAutomation][Epic] An unexpected error occurred: {ex.Message}");
                s_AutomationCompleted = true;
                return false;
            }
        }

        private static string ExtractGameId(string[] args, SimpleLogger logger)
        {
            string batPath = args?.FirstOrDefault(a => a.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(batPath) || !File.Exists(batPath))
            {
                logger.Log("[InstallAutomation][Epic] Error: .bat file path not found in command line arguments.");
                return null;
            }

            try
            {
                var content = File.ReadAllText(batPath);
                var match = Regex.Match(content, @"com\.epicgames\.launcher://apps/([^?]+)");
                if (match.Success)
                {
                    var gameId = match.Groups[1].Value.Trim();
                    logger.Log($"[InstallAutomation][Epic] Extracted Game ID: {gameId}");
                    return gameId;
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[InstallAutomation][Epic] Error reading or parsing .bat file: {ex.Message}");
            }

            logger.Log("[InstallAutomation][Epic] Error: Could not extract game ID from .bat file.");
            return null;
        }

        private static async Task<bool> WaitForEpicLauncherAsync(SimpleLogger logger)
        {
            var fgDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            logger.Log("[InstallAutomation][Epic] Waiting for Epic Games Launcher to be in the foreground...");
            while (DateTime.UtcNow < fgDeadline)
            {
                if (IsEpicInForeground())
                {
                    logger.Log("[InstallAutomation][Epic] Epic Games Launcher is in the foreground.");
                    await Task.Delay(5000); // Grace period for UI to render
                    return true;
                }
                await Task.Delay(250);
            }
            logger.Log("[InstallAutomation][Epic] Timed out waiting for Epic Games Launcher.");
            return false;
        }

        private static async Task<bool> WaitForInstallCompletionAsync(string gameId, SimpleLogger logger)
        {
            var installTimeout = DateTime.UtcNow + TimeSpan.FromMinutes(45);
            while (DateTime.UtcNow < installTimeout)
            {
                if (!IsEpicProcessRunning())
                {
                    logger.Log("[InstallAutomation][Epic] Epic Games Launcher process closed unexpectedly. Aborting.");
                    return false;
                }

                if (IsGameInstalled(gameId, logger))
                {
                    return true;
                }
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
            return false;
        }

        #region Installation Check Helpers (Adapted from EpicLibrary)

        private static LauncherGameInfo GetInstalledGameInfo(string appName, SimpleLogger logger)
        {
            var manifests = GetInstalledManifests();
            var manifest = manifests?.FirstOrDefault(m => m.AppName == appName);

            if (manifest == null)
            {
                logger.Log($"[InstallAutomation][Epic] Could not find manifest for installed game {appName}.");
                return null;
            }

            return new LauncherGameInfo
            {
                Id = appName,
                Name = manifest.DisplayName,
                LauncherUrl = string.Format(@"com.epicgames.launcher://apps/{0}?action=launch&silent=true", appName),
                InstallDirectory = Path.GetFullPath(manifest.InstallLocation),
                ExecutableName = manifest.LaunchExecutable,
                Launcher = "Epic",
                IsInstalled = true
            };
        }

        private static bool IsGameInstalled(string appName, SimpleLogger logger)
        {
            var appList = GetInstalledAppList();
            if (appList == null || !appList.Any(a => a.AppName == appName))
            {
                return false;
            }

            var manifests = GetInstalledManifests();
            if (manifests == null)
            {
                return false;
            }

            var manifest = manifests.FirstOrDefault(m => m.AppName == appName);
            return manifest != null;
        }

        private static List<LauncherInstalled.InstalledApp> GetInstalledAppList()
        {
            var installListPath = Path.Combine(AllUsersPath, "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installListPath)) return new List<LauncherInstalled.InstalledApp>();
            try
            {
                var list = JsonTools.DeserializeString<LauncherInstalled>(File.ReadAllText(installListPath));
                return list?.InstallationList ?? new List<LauncherInstalled.InstalledApp>();
            }
            catch { return new List<LauncherInstalled.InstalledApp>(); }
        }

        private static IEnumerable<EpicGame> GetInstalledManifests()
        {
            var installListPath = GetMetadataPath();
            if (installListPath != null && Directory.Exists(installListPath))
            {
                foreach (var manFile in Directory.GetFiles(installListPath, "*.item"))
                {
                    EpicGame manifest = null;
                    try { manifest = JsonTools.DeserializeString<EpicGame>(File.ReadAllText(manFile)); } catch { }
                    if (manifest != null) yield return manifest;
                }
            }
        }

        private static string GetMetadataPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Epic Games\EOS", "ModSdkMetadataDir", null) as string;
            }
            return null;
        }

        private static string AllUsersPath => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "Epic")
            : null;

        #endregion

        #region Process/Window Helpers

        private static int GetWindowProcessId(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }

        private static string GetProcessNameSafe(int pid)
        {
            try { return Process.GetProcessById(pid)?.ProcessName; } catch { return null; }
        }

        private static bool IsEpicProcessName(string processName)
        {
            return !string.IsNullOrWhiteSpace(processName) && processName.Trim().Equals("EpicGamesLauncher", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEpicInForeground()
        {
            IntPtr fg = GetForegroundWindow();
            return fg != IntPtr.Zero && IsEpicProcessName(GetProcessNameSafe(GetWindowProcessId(fg)));
        }

        private static bool IsEpicProcessRunning()
        {
            return Process.GetProcesses().Any(p => IsEpicProcessName(p.ProcessName));
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
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch { }
        }

        #endregion
    }
}
