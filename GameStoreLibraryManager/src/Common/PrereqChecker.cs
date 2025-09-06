using System;
using System.IO;
using Microsoft.Win32;

namespace GameStoreLibraryManager.Common
{
    public static class PrereqChecker
    {
        public static bool HasDotnet8DesktopRuntime()
        {
            try
            {
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(baseDir))
                {
                    foreach (var dir in Directory.GetDirectories(baseDir))
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name) && name.StartsWith("8.")) return true;
                    }
                }
            }
            catch { }
            // Registry fallback (x64)
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
                {
                    if (key != null)
                    {
                        foreach (var sub in key.GetSubKeyNames())
                        {
                            if (sub.StartsWith("8.")) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool HasWebView2Runtime()
        {
            // Check Evergreen runtime via EdgeUpdate Clients key
            const string clientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($"SOFTWARE\\Microsoft\\EdgeUpdate\\Clients\\{clientGuid}"))
                {
                    var pv = key?.GetValue("pv") as string;
                    if (!string.IsNullOrEmpty(pv)) return true;
                }
            }
            catch { }
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey($"Software\\Microsoft\\EdgeUpdate\\Clients\\{clientGuid}"))
                {
                    var pv = key?.GetValue("pv") as string;
                    if (!string.IsNullOrEmpty(pv)) return true;
                }
            }
            catch { }
            // Fallback: check install folder
            try
            {
                var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var appDir = Path.Combine(x86, "Microsoft", "EdgeWebView", "Application");
                if (Directory.Exists(appDir))
                {
                    foreach (var dir in Directory.GetDirectories(appDir))
                    {
                        var name = Path.GetFileName(dir);
                        // version-like folder
                        if (!string.IsNullOrEmpty(name) && char.IsDigit(name[0])) return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
