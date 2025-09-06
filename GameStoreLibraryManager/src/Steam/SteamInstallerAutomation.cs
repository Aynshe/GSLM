using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GameStoreLibraryManager.Common;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace GameStoreLibraryManager.Steam
{
    public static class SteamInstallerAutomation
    {
        // Set to true when the automation (Install + optional EULA) is completed; used to close the notice ASAP
        private static volatile bool s_AutomationCompleted = false;
        // Track active notice window so OCR can temporarily hide it during capture to avoid occlusion
        private static InstallNoticeWindow s_ActiveNotice;
        // Localized labels for the primary action buttons
        private static readonly string[] InstallButtonNames = new[]
        {
            "Install",           // en
            "Installer",         // fr
            "Instalar",          // es/pt
            "Installieren",      // de
            "Installa",          // it
            "Установить",        // ru
            "安装",                // zh-cn
            "安裝",                // zh-tw
            "インストール",         // ja
            "설치",                // ko
        };

        private static readonly string[] AcceptButtonNames = new[]
        {
            "Accept",            // en
            "Accepter",          // fr
            "Aceptar",           // es
            "Akzeptieren",       // de
            "Accetta",           // it
            "Принять",           // ru
            "同意",                // zh
            "同意する",             // ja
            "동의",                // ko
        };

        // Retry budget tracking to avoid infinite loops on problematic windows
        private static readonly ConcurrentDictionary<long, (DateTime lastAttempt, int count)> s_RetryBudget = new();
        private static readonly ConcurrentDictionary<long, DateTime> s_InvalidWindowCooldown = new();

        public static bool TryInstallFirstGame(Config config, SimpleLogger logger)
        {
            s_AutomationCompleted = false;
            s_ActiveNotice = null;

            // Check if Steam is already running
            bool steamAlreadyRunning = Process.GetProcessesByName("steam").Any();
            if (!steamAlreadyRunning)
            {
                logger.Log("[InstallAutomation] Steam not running. Starting Steam...");
            }

            // Launch Steam (will do nothing if already running)
            try { Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }); } catch { }

            // If Steam was not running, give it time to start/update until UI is ready
            if (!steamAlreadyRunning)
            {
                logger.Log("[InstallAutomation] Waiting for Steam to start (and finish update if any)...");
                WaitForSteamReady(TimeSpan.FromSeconds(60), logger);
            }

            // Show topmost notice window (non-blocking) while Steam is foreground or actionable text is present
            using var notice = InstallNoticeWindow.ShowTopMost(
                "Please wait, automatic installation...",
                autoClose: null,
                // Keep showing while not completed and Steam might still present actionable UI
                keepShowingWhile: () => !s_AutomationCompleted && (IsSteamInForeground() || IsSteamActionablePresent() || IsSteamProcessRunning()));
            // Expose the active notice to OCR loop so it can be hidden during capture
            s_ActiveNotice = notice;

            // Windows UI Automation path (no model required)
            bool useUia = config.GetBoolean("steam_enable_install_automation", true);
            if (useUia)
            {
                logger.Log("[InstallAutomation] UI Automation path scanning for actionable UI...");
                var uiaTimeout = TimeSpan.FromMinutes(2);
                try
                {
                    if (TryUiAutomationFirstInstall(config, logger, uiaTimeout))
                    {
                        logger.Log("[InstallAutomation] UI Automation path succeeded. Exiting manager.");
                        s_AutomationCompleted = true;
                        return true;
                    }
                    logger.Debug("[InstallAutomation] UI Automation path did not succeed.");
                }
                catch (Exception ex)
                {
                    logger.Log($"[InstallAutomation] UI Automation error: {ex.Message}");
                }
            }

            // Only UI Automation is supported now.
            return false;
        }

        private static void WaitForSteamReady(TimeSpan maxWait, SimpleLogger logger)
        {
            DateTime end = DateTime.UtcNow + maxWait;
            while (DateTime.UtcNow < end)
            {
                try
                {
                    // Steam processes must exist
                    var steamProcs = Process.GetProcessesByName("steam");
                    if (steamProcs.Length == 0)
                    {
                        Thread.Sleep(300);
                        continue;
                    }

                    // There should be at least one visible top-level window owned by steam/steamwebhelper
                    bool hasUi = EnumTopLevelWindows(hwnd =>
                    {
                        int pid = GetProcessIdFromWindow(hwnd);
                        string pname = null;
                        try { pname = Process.GetProcessById(pid)?.ProcessName; } catch { }
                        if (string.IsNullOrEmpty(pname)) return false;
                        if (!(pname.StartsWith("steam", StringComparison.OrdinalIgnoreCase))) return false;
                        // Visible window with any title/class is good enough
                        return true;
                    });

                    if (hasUi)
                    {
                        logger.Debug("[InstallAutomation] Steam UI appears ready.");
                        return;
                    }
                }
                catch { }
                Thread.Sleep(300);
            }
            logger.Debug("[InstallAutomation] Proceeding without Steam UI ready confirmation (timeout).");
        }

        private static bool EnumTopLevelWindows(Func<IntPtr, bool> onMatch)
        {
            bool anyClicked = false;
            EnumWindows((h, l) =>
            {
                // skip invisible/minimized
                if (!IsWindowVisible(h)) return true;
                if (onMatch(h)) { anyClicked = true; return false; }
                return true;
            }, IntPtr.Zero);
            return anyClicked;
        }

        private static bool IsSteamInForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                int pid = GetProcessIdFromWindow(fg);
                if (pid <= 0) return false;
                string pname = null;
                try { pname = Process.GetProcessById(pid)?.ProcessName; } catch { }
                return !string.IsNullOrEmpty(pname) && pname.StartsWith("steam", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // True if core Steam processes are running; used to keep the notice visible
        private static bool IsSteamProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Any() || Process.GetProcessesByName("steamwebhelper").Any();
            }
            catch { return false; }
        }

        // Check if any Steam window currently exposes an actionable Install/Accept control
        private static bool IsSteamActionablePresent()
        {
            bool present = false;
            EnumTopLevelWindows(hwnd =>
            {
                int pid = GetProcessIdFromWindow(hwnd);
                string pname = null;
                try { pname = Process.GetProcessById(pid)?.ProcessName; } catch { }
                if (string.IsNullOrEmpty(pname) || !pname.StartsWith("steam", StringComparison.OrdinalIgnoreCase)) return false;
                if (HasInstallControl(hwnd)) { present = true; return true; }
                return false;
            });
            return present;
        }

        private static int GetProcessIdFromWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                return unchecked((int)pid);
            }
            catch { return 0; }
        }

        // Check if a given window still shows an actionable Install/Accept control
        private static bool HasInstallControl(IntPtr hwnd)
        {
            bool present = false;
            EnumChildWindows(hwnd, (child, l) =>
            {
                string ctext = GetWindowText(child) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ctext)) return true;
                if (InstallButtonNames.Any(lbl => ctext.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    AcceptButtonNames.Any(lbl => ctext.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    present = true;
                    return false; // stop
                }
                return true;
            }, IntPtr.Zero);
            return present;
        }

        // Windows UI Automation (UIA) path — no ML model required (via FlaUI)
        private static bool TryUiAutomationFirstInstall(Config config, SimpleLogger logger, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            bool installClicked = false; // once true, continue scanning for Accept
            while (DateTime.UtcNow < deadline)
            {
                // If we already clicked Install and Steam lost foreground, stop searching for Accept
                if (installClicked && !IsSteamInForeground())
                {
                    logger.Debug("[InstallAutomation][UIA] Steam not in foreground after Install; stopping Accept search.");
                    s_AutomationCompleted = true;
                    return true;
                }
                bool acted = EnumTopLevelWindows(hwnd =>
                {
                    int pid = GetProcessIdFromWindow(hwnd);
                    string pname = null;
                    try { pname = Process.GetProcessById(pid)?.ProcessName; } catch { }
                    if (string.IsNullOrEmpty(pname) || !pname.StartsWith("steam", StringComparison.OrdinalIgnoreCase)) return false;

                    long key = hwnd.ToInt64();
                    if (ShouldSkipByRetryBudget(key, logger)) return false;

                    if (!GetWindowRect(hwnd, out var rect)) return false;
                    int w = Math.Max(0, rect.Right - rect.Left);
                    int h = Math.Max(0, rect.Bottom - rect.Top);
                    if (w <= 20 || h <= 20) return false;

                    try
                    {
                        // Use FlaUI to attach and search
                        using var app = FlaUI.Core.Application.Attach(pid);
                        using var automation = new UIA3Automation();
                        var window = automation.FromHandle(hwnd)?.AsWindow();
                        if (window == null) { NoteRetryAttempt(key); return false; }

                        // Find buttons and match by name heuristics; filter enabled in code
                        var buttons = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));

                        if (buttons == null || buttons.Length == 0)
                        {
                            NoteRetryAttempt(key);
                            return false;
                        }

                        AutomationElement target = null;
                        string targetKind = null; // "install" or "accept"
                        foreach (var b in buttons)
                        {
                            if (!b.IsEnabled) continue;
                            string name = string.Empty;
                            try { name = b.Name ?? string.Empty; } catch { }
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            bool isInstall = InstallButtonNames.Any(lbl =>
                                string.Equals(name, lbl, StringComparison.OrdinalIgnoreCase) ||
                                (name.Length >= 3 && lbl.StartsWith(name, StringComparison.OrdinalIgnoreCase)) ||
                                (lbl.Length >= 3 && name.StartsWith(lbl, StringComparison.OrdinalIgnoreCase)));
                            bool isAccept = AcceptButtonNames.Any(lbl =>
                                string.Equals(name, lbl, StringComparison.OrdinalIgnoreCase) ||
                                name.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0);
                            if ((!installClicked && isInstall) || (installClicked && isAccept))
                            {
                                target = b;
                                targetKind = isAccept ? "accept" : "install";
                                break;
                            }
                        }

                        if (target == null)
                        {
                            // Broaden search: any invokable element with matching name
                            var all = window.FindAllDescendants();
                            foreach (var el in all)
                            {
                                if (!el.IsEnabled) continue;
                                string name = string.Empty;
                                try { name = el.Name ?? string.Empty; } catch { }
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                bool matchInstall = InstallButtonNames.Any(lbl =>
                                    string.Equals(name, lbl, StringComparison.OrdinalIgnoreCase) ||
                                    name.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0);
                                bool matchAccept = AcceptButtonNames.Any(lbl =>
                                    string.Equals(name, lbl, StringComparison.OrdinalIgnoreCase) ||
                                    name.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0);
                                if ((!installClicked && !matchInstall) || (installClicked && !matchAccept)) continue;
                                var inv2 = el.Patterns.Invoke;
                                if (!inv2.IsSupported) continue;
                                target = el;
                                targetKind = matchAccept ? "accept" : "install";
                                break;
                            }

                            if (target == null)
                            {
                                NoteRetryAttempt(key);
                                return false;
                            }
                        }

                        // Try native Invoke
                        try
                        {
                            var inv = target.Patterns.Invoke;
                            if (inv.IsSupported)
                            {
                                inv.Pattern.Invoke();
                                logger.Log($"[InstallAutomation][UIA] Invoked {targetKind} via UIA3 InvokePattern.");
                                ClearRetryBudget(key);
                                Thread.Sleep(250);
                                if (targetKind == "install")
                                {
                                    installClicked = true; // continue to look for Accept
                                    return false; // keep scanning
                                }
                                else
                                {
                                    s_AutomationCompleted = true;
                                    return true; // done after Accept
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Debug($"[InstallAutomation][UIA] UIA Invoke failed: {ex.Message}");
                        }

                        // Fallback: click center via our hwnd client coords
                        try
                        {
                            var br = target.BoundingRectangle;
                            if (br.Width > 1 && br.Height > 1)
                            {
                                int cxAbs = (int)(br.Left + br.Width / 2.0);
                                int cyAbs = (int)(br.Top + br.Height / 2.0);
                                var pt = new POINT { X = cxAbs, Y = cyAbs };
                                ScreenToClient(hwnd, ref pt);
                                logger.Debug($"[InstallAutomation][UIA] Clicking {targetKind} at client=({pt.X},{pt.Y}) hwnd=0x{hwnd.ToInt64():X}");
                                ClickHwndClient(hwnd, pt.X, pt.Y);
                                ClearRetryBudget(key);
                                Thread.Sleep(250);
                                if (targetKind == "install")
                                {
                                    installClicked = true;
                                    return false; // continue to look for Accept
                                }
                                else
                                {
                                    s_AutomationCompleted = true;
                                    return true; // done after Accept
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Debug($"[InstallAutomation][UIA] Fallback click failed: {ex.Message}");
                        }

                        NoteRetryAttempt(key);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"[InstallAutomation][UIA] Attach/search failed: {ex.Message}");
                        NoteRetryAttempt(key);
                        return false;
                    }
                });

                if (acted)
                {
                    Thread.Sleep(200);
                    return true;
                }

                Thread.Sleep(150);
            }
            return false;
        }

        private static bool ShouldSkipByRetryBudget(long key, SimpleLogger logger)
        {
            const int MaxRetries = 40;
            const int BudgetWindowMinutes = 1;
            
            var now = DateTime.UtcNow;
            if (s_RetryBudget.TryGetValue(key, out var budget))
            {
                if ((now - budget.lastAttempt).TotalMinutes < BudgetWindowMinutes && budget.count >= MaxRetries)
                {
                    logger.Debug($"[InstallAutomation] Skipping hwnd=0x{key:X} (retry budget exhausted: {budget.count}/{MaxRetries})");
                    return true;
                }
            }
            return false;
        }

        private static void NoteRetryAttempt(long key)
        {
            var now = DateTime.UtcNow;
            s_RetryBudget.AddOrUpdate(key, (now, 1), (_, old) =>
            {
                if ((now - old.lastAttempt).TotalMinutes >= 3)
                    return (now, 1); // reset budget
                return (now, old.count + 1);
            });
        }

        private static void ClearRetryBudget(long key)
        {
            s_RetryBudget.TryRemove(key, out _);
        }

        // Send a left-click at the given client coordinates (x,y) to the target hwnd
        private static void ClickHwndClient(IntPtr hwnd, int clientX, int clientY)
        {
            try
            {
                // Optionally bring window to foreground to ensure it receives input
                try { SetForegroundWindow(hwnd); } catch { }

                // lParam packs x and y as 16-bit each in client coordinates
                IntPtr lParam = new IntPtr(MAKELPARAM((ushort)clientX, (ushort)clientY));
                // Send down/up; PostMessage to avoid blocking UI thread
                PostMessage(hwnd, WM_LBUTTONDOWN, new IntPtr(1), lParam);
                Thread.Sleep(30);
                PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            }
            catch { }
        }

        // -------- P/Invoke --------
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private static string GetWindowText(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static int MAKELPARAM(ushort low, ushort high)
        {
            return (high << 16) | (low & 0xFFFF);
        }

        // -------- Small topmost notice window --------
                // Note: Internal NoticeWindow class removed in favor of Common.InstallNoticeWindow
    }
}
