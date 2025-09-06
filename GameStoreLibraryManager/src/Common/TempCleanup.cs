#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GameStoreLibraryManager.Common
{
    internal static class TempCleanup
    {
        private static readonly object s_Lock = new object();
        private static readonly HashSet<string> s_Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_Registered;

        public static void RegisterPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                EnsureRegistered();
                lock (s_Lock)
                {
                    s_Paths.Add(path);
                }
            }
            catch { }
        }

        private static void EnsureRegistered()
        {
            if (s_Registered) return;
            lock (s_Lock)
            {
                if (s_Registered) return;
                try
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => TryCleanupAll();
                    AppDomain.CurrentDomain.DomainUnload += (_, __) => TryCleanupAll();
                }
                catch { }
                s_Registered = true;
            }
        }

        private static void TryCleanupAll()
        {
            List<string> toDelete;
            lock (s_Lock)
            {
                toDelete = new List<string>(s_Paths);
                s_Paths.Clear();
            }
            foreach (var p in toDelete)
            {
                TryDeleteDirectory(p);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                // Retry a few times in case files are still being written
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        Directory.Delete(path, recursive: true);
                        return;
                    }
                    catch
                    {
                        if (attempt == maxAttempts) break;
                        Thread.Sleep(150);
                    }
                }
            }
            catch { }
        }
    }
}
