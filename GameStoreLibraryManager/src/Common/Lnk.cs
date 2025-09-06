using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameStoreLibraryManager.Common
{
    public static class Lnk
    {
        [SupportedOSPlatform("windows")]
        public static void Create(string lnkPath, string targetPath, string arguments, string workingDir, string description)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(type);
                var shortcut = shell.CreateShortcut(lnkPath);

                shortcut.TargetPath = targetPath;
                shortcut.Arguments = arguments;
                shortcut.WorkingDirectory = workingDir ?? Path.GetDirectoryName(targetPath);
                shortcut.Description = description;
                // shortcut.IconLocation = targetPath + ",0"; // Optional: can be set if needed

                shortcut.Save();

                Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                // Log the error. For now, we'll just print to console as there's no logger here.
                Console.WriteLine($"[LNK] Failed to create .lnk file at {lnkPath}. Error: {ex.Message}");
            }
        }
    }
}
