using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GameStoreLibraryManager.Common;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;

namespace GameStoreLibraryManager.Gog
{
    public static class GogInstallerAutomation
    {
        private static readonly string[] InstallButtonNames = new[] { "Install", "Installer", "Instalar", "Installieren", "Installa", "Установить", "安装", "安裝", "インストール", "설치" };
        private static readonly string[] CancelButtonNames = new[] { "Annuler", "Cancel", "Abbrechen", "Cancelar", "Annulla", "Отмена", "取消", "キャンセル", "취소" };

        public static bool TryInstallFirstGame(Config config, SimpleLogger logger, string gameId)
        {
            bool gogAlreadyRunning = Process.GetProcessesByName("GalaxyClient").Any();
            if (!gogAlreadyRunning)
            {
                logger.Log("[GogInstallAutomation] GOG not running. Starting GOG...");
                var gogPath = GogLibrary.GetGalaxyExecutablePath();
                if (!string.IsNullOrEmpty(gogPath))
                {
                    Process.Start(gogPath, $"/command=showGamePage /gameId={gameId}");
                }
                else
                {
                    logger.Log("[GogInstallAutomation] Could not find GOG Galaxy executable.");
                    return false;
                }
            }

            var notice = InstallNoticeWindow.ShowTopMost("Please wait, automatic installation...");

            var uiaTimeout = TimeSpan.FromMinutes(2);
            try
            {
                if (TryUiAutomationFirstInstall(logger, uiaTimeout))
                {
                    logger.Log("[GogInstallAutomation] UI Automation path succeeded.");
                    TrackInstallationProgress(config, logger, gameId, notice).GetAwaiter().GetResult();
                    return true;
                }
                logger.Debug("[GogInstallAutomation] UI Automation path did not succeed.");

                if (!IsGogProcessRunning())
                {
                    logger.Log("[GogInstallAutomation][CRITICAL] GOG Client process is not running after UI automation failure. The installation cannot continue. Aborting GSLM.");
                    notice?.Dispose();
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[GogInstallAutomation] UI Automation error: {ex.Message}");
            }

            return false;
        }

        private static bool TryUiAutomationFirstInstall(SimpleLogger logger, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            bool firstClickDone = false;
            bool secondClickDone = false;

            while (DateTime.UtcNow < deadline && !secondClickDone)
            {
                EnumWindows((hwnd, lParam) => {
                    if (!IsWindowVisible(hwnd)) return true;
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == 0) return true;

                    try
                    {
                        var process = Process.GetProcessById((int)pid);
                        if (!process.ProcessName.Equals("GalaxyClient", StringComparison.OrdinalIgnoreCase)) return true;

                        using (var automation = new UIA3Automation())
                        {
                            var window = automation.FromHandle(hwnd)?.AsWindow();
                            if (window == null) return true;

                            if (!firstClickDone)
                            {
                                var installControl = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.Text)).Or(cf.ByControlType(ControlType.ListItem)))
                                                  .FirstOrDefault(c => InstallButtonNames.Any(name => c.Name != null && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

                                if (installControl != null)
                                {
                                    if (installControl.Patterns.Invoke.IsSupported)
                                    {
                                        installControl.Patterns.Invoke.Pattern.Invoke();
                                        logger.Log($"[GogInstallAutomation] Clicked main '{installControl.Name}' control.");
                                    }
                                    else
                                    {
                                        installControl.Click();
                                        logger.Log($"[GogInstallAutomation] Mouse clicked main '{installControl.Name}' control.");
                                    }
                                    firstClickDone = true;
                                    logger.Log("[GogInstallAutomation] Waiting 5s for the integrated confirmation UI to appear...");
                                    Thread.Sleep(5000); // Wait for the integrated modal to appear
                                }
                            }
                            else // firstClickDone is true, now look for the confirmation UI (integrated modal)
                            {
                                logger.Debug("[GogInstallAutomation] Searching for confirmation UI...");
                                
                                List<AutomationElement> allDescendants = new List<AutomationElement>();
                                List<AutomationElement> installCandidates = new List<AutomationElement>();
                                List<AutomationElement> cancelCandidates = new List<AutomationElement>();

                                try {
                                    allDescendants = window.FindAllDescendants().ToList();
                                    
                                    foreach(var el in allDescendants) {
                                        try {
                                            if (el.IsEnabled && !string.IsNullOrEmpty(el.Name)) {
                                                if (InstallButtonNames.Any(name => el.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                                                    installCandidates.Add(el);
                                                else if (CancelButtonNames.Any(name => el.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                                                    cancelCandidates.Add(el);
                                            }
                                        } catch { }
                                    }
                                } catch (Exception ex) {
                                    logger.Debug($"[GogInstallAutomation] UIA Descendants error: {ex.Message}");
                                }

                                // Calculate window center
                                var winRect = window.BoundingRectangle;
                                double winCenterX = winRect.Left + winRect.Width / 2.0;
                                double winCenterY = winRect.Top + winRect.Height / 2.0;

                                // Pick the 'Installer' candidate closest to the window center
                                var installElement = installCandidates
                                    .OrderBy(el => {
                                        try {
                                            var r = el.BoundingRectangle;
                                            double cx = r.Left + r.Width / 2.0;
                                            double cy = r.Top + r.Height / 2.0;
                                            return Math.Sqrt(Math.Pow(cx - winCenterX, 2) + Math.Pow(cy - winCenterY, 2));
                                        } catch { return double.MaxValue; }
                                    }).FirstOrDefault();

                                var cancelElement = cancelCandidates
                                    .OrderBy(el => {
                                        try {
                                            var r = el.BoundingRectangle;
                                            double cx = r.Left + r.Width / 2.0;
                                            double cy = r.Top + r.Height / 2.0;
                                            return Math.Sqrt(Math.Pow(cx - winCenterX, 2) + Math.Pow(cy - winCenterY, 2));
                                        } catch { return double.MaxValue; }
                                    }).FirstOrDefault();

                                if (installElement != null && cancelElement != null)
                                {
                                    var rect = installElement.BoundingRectangle;
                                    logger.Log($"[GogInstallAutomation] Confirmation UI detected near center (Install: '{installElement.Name}' at {rect.Left},{rect.Top}).");

                                    try {
                                        window.Focus();
                                        Thread.Sleep(500);

                                        if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                                        {
                                            int cx = (int)(rect.Left + rect.Width / 2);
                                            int cy = (int)(rect.Top + rect.Height / 2);
                                            
                                            logger.Log($"[GogInstallAutomation] Clicking confirmation at ({cx},{cy}). Window center: ({winCenterX:F0},{winCenterY:F0}).");
                                            
                                            // Fallback to mouse_event directly as it's most reliable for center-aligned web overlays
                                            ClickScreen(cx, cy);
                                            
                                            secondClickDone = true;
                                            return false; 
                                        }
                                    } catch (Exception ex) {
                                        logger.Debug($"[GogInstallAutomation] Click attempt failed: {ex.Message}");
                                    }
                                }

                                // Fallback to OCR if UIA did not succeed
                                logger.Debug("[GogInstallAutomation] UIA detection failed or incomplete. Trying OCR...");
                                string dumpDir = null;
                                try {
                                    dumpDir = Path.Combine(Path.GetTempPath(), "GogOCR", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                                    Directory.CreateDirectory(dumpDir);
                                    TempCleanup.RegisterPath(dumpDir);
                                } catch { }

                                bool ocrClicked = OcrHelper.TryOcrClickInWindow(
                                    hwnd,
                                    InstallButtonNames,
                                    ClickScreen,
                                    OcrButtonColor.Purple,
                                    msg => logger.Log($"[GogInstallAutomation][OCR] {msg}"),
                                    dumpDir
                                );

                                if (ocrClicked)
                                {
                                    logger.Log("[GogInstallAutomation] Clicked 'Install' button via OCR.");
                                    secondClickDone = true;
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"[GogInstallAutomation] UIA loop error: {ex.Message}");
                    }
                    return !secondClickDone; // Continue enumeration if we haven't finished
                }, IntPtr.Zero);

                Thread.Sleep(500);
            }

            return secondClickDone;
        }

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

        private static async Task TrackInstallationProgress(Config config, SimpleLogger logger, string gameId, InstallNoticeWindow notice)
        {
            try
            {
                notice.UpdateText("Installation in progress, please wait...");
                notice.MoveToCenter();
            notice.BringToFront();

            logger.Log("[GogInstallAutomation] Waiting for installation to complete by checking registry...");
            var overallTimeout = DateTime.UtcNow.AddMinutes(30);
            bool installComplete = false;

            while (DateTime.UtcNow < overallTimeout && !installComplete)
            {
                if (!IsGogProcessRunning())
                {
                    logger.Log("[GogInstallAutomation] GOG process is no longer running. Aborting installation check.");
                    break;
                }

                var gogLibrary = new GogLibrary(config, logger);
                if (gogLibrary.GetInstalledGames().Any(g => g.Id == gameId))
                {
                    installComplete = true;
                    logger.Log($"[GogInstallAutomation] Game {gameId} detected as installed via registry.");
                }
                await Task.Delay(5000); // Check every 5 seconds
            }

            if (installComplete)
            {
                logger.Log("[GogInstallAutomation] Installation complete.");

                notice.UpdateText("Game installed! Waiting for GOG to close...");

                // Kill GOG process
                foreach (var process in Process.GetProcessesByName("GalaxyClient"))
                {
                    try { process.Kill(); } catch (Exception ex) { logger.Log($"[GogInstallAutomation] Failed to kill GOG process: {ex.Message}"); }
                }
                logger.Log("[GogInstallAutomation] GOG process terminated.");

                var gogLibrary = new GogLibrary(config, logger);
                var installedGame = gogLibrary.GetInstalledGames().FirstOrDefault(g => g.Id == gameId);
                string sanitizedName = "";

                if (installedGame != null)
                {
                    sanitizedName = StringUtils.SanitizeFileName(installedGame.Name);
                    logger.Log($"[GogInstallAutomation] Creating shortcut for {installedGame.Name}.");
                    ShortcutManager.CreateShortcut(installedGame, config);
                }
                else
                {
                    logger.Log($"[GogInstallAutomation] Could not find installed game info for {gameId} to create shortcut.");
                }

                // Reload localhost service
                try
                {
                    using var client = new HttpClient();
                    var response = await client.GetAsync("http://127.0.0.1:1234/reloadgames");
                    logger.Log($"[GogInstallAutomation] Reload request sent to http://127.0.0.1:1234/reloadgames. Status: {response.StatusCode}");
                    var signalMessage = await ReloadSignalListener.WaitForSignalAsync(logger);
                    if (signalMessage == null || !signalMessage.Contains("\"Not Installed\""))
                    {
                        logger.Log("[GogInstallAutomation] Did not receive the correct signal from ES. The game may not launch correctly.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"[GogInstallAutomation] Failed to send reload request or wait for signal: {ex.Message}");
                }

                // Execute game if enabled
                if (config.GetBoolean("gog_execute_game_after_install", false) && installedGame != null)
                {
                    notice.UpdateText("The game will now run");

                    try
                    {
                        string shortcutPath = Path.Combine(PathManager.GogRomsPath, $"{sanitizedName}.lnk");
                        if (File.Exists(shortcutPath))
                        {
                            using var client = new HttpClient();
                            var content = new StringContent(shortcutPath, Encoding.UTF8, "text/plain");
                            var response = await client.PostAsync("http://127.0.0.1:1234/launch", content);
                            logger.Log($"[GogInstallAutomation] Launch request for '{shortcutPath}' sent to http://127.0.0.1:1234/launch. Status: {response.StatusCode}");
                        }
                        else
                        {
                            logger.Log($"[GogInstallAutomation] Shortcut not found at '{shortcutPath}'. Cannot launch game.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"[GogInstallAutomation] Failed to send launch request: {ex.Message}");
                    }
                }
            }
            else
            {
                logger.Log("[GogInstallAutomation] Timed out waiting for installation to complete.");
            }
            }
            finally
            {
                notice?.Dispose();
            }
        }

        private static bool IsGogProcessRunning()
        {
            return Process.GetProcessesByName("GalaxyClient").Any();
        }

        // -------- P/Invoke --------
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    }
}
