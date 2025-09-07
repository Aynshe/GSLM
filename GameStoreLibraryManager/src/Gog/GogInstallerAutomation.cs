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
        private static readonly string[] InstallButtonNames = new[] { "Install", "Installer" };

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
                                var installControl = window.FindFirstDescendant(cf => cf.ByName("Installer"));
                                if (installControl != null && installControl.Patterns.Invoke.IsSupported)
                                {
                                    installControl.Patterns.Invoke.Pattern.Invoke();
                                    logger.Log("[GogInstallAutomation] Clicked main 'Install' control.");
                                    firstClickDone = true;
                                }
                            }
                            else // firstClickDone is true, now look for the modal
                            {
                                var modal = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Table))
                                                  .FirstOrDefault(t => t.Name != null && t.Name.StartsWith("Installer vers"));
                                if (modal != null)
                                {
                                    // Attempt to select language
                                    var languageComboBox = modal.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox))?.AsComboBox();
                                    if (languageComboBox != null)
                                    {
                                        var systemLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                                        string targetLanguage = "English"; // Default
                                        var langMap = new Dictionary<string, string> {
                                            { "fr", "Français" }, { "de", "Deutsch" }, { "es", "Español" }, { "ru", "Русский" }, { "pl", "Polski" }, { "pt", "Português" }, { "it", "Italiano" }, { "zh", "中文" }
                                        };
                                        if (langMap.ContainsKey(systemLang)) { targetLanguage = langMap[systemLang]; }

                                        logger.Log($"[GogInstallAutomation] Attempting to select language: {targetLanguage}");
                                        try
                                        {
                                            if (languageComboBox.Items.Any(item => item.Text.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                languageComboBox.Select(targetLanguage);
                                                logger.Log($"[GogInstallAutomation] Selected language: {targetLanguage}");
                                            }
                                            else
                                            {
                                                logger.Log($"[GogInstallAutomation] Target language '{targetLanguage}' not found, defaulting to English.");
                                                languageComboBox.Select("English");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.Log($"[GogInstallAutomation] Could not select language: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        logger.Log("[GogInstallAutomation] Language selection control not found. Proceeding with default.");
                                    }

                                    var modalInstallButton = modal.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Installer")))?.AsButton();
                                    if (modalInstallButton != null && modalInstallButton.IsEnabled)
                                    {
                                        modalInstallButton.Click();
                                        logger.Log("[GogInstallAutomation] Clicked 'Install' button in modal dialog.");
                                        secondClickDone = true;
                                        return false; // Stop enumeration, we are completely done.
                                    }
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
    }
}
