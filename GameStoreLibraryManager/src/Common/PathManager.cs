using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GameStoreLibraryManager.Common
{
    public static class PathManager
    {
        private static string _executablePath;
        private static string _retroBatPath;

        public static string ApplicationRootPath => _retroBatPath ?? _executablePath;

        static PathManager()
        {
            _executablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _retroBatPath = GetRetroBatPath();
        }

        private static string GetRetroBatPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey("Software\\RetroBat"))
                    {
                        if (key != null)
                        {
                            var retroBatPath = key.GetValue("LatestKnownInstallPath") as string;
                            if (!string.IsNullOrEmpty(retroBatPath) && Directory.Exists(retroBatPath))
                            {
                                return retroBatPath;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        public static string UserDataPath
        {
            get
            {
                string path = Path.Combine(_executablePath, "user");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string RomsPath
        {
            get
            {
                string basePath = _retroBatPath ?? _executablePath;
                string path = Path.Combine(basePath, "roms");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string SteamRomsPath
        {
            get
            {
                string path = Path.Combine(RomsPath, "steam");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string EpicRomsPath
        {
            get
            {
                string path = Path.Combine(RomsPath, "epic");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string GogRomsPath
        {
            get
            {
                string path = Path.Combine(RomsPath, "gog");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string AmazonRomsPath
        {
            get
            {
                string path = Path.Combine(RomsPath, "amazon");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string XboxRomsPath
        {
            get
            {
                // This now points to the 'windows' system folder for gamelist.xml and media
                string path = Path.Combine(RomsPath, "windows");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string ApiKeyPath
        {
            get
            {
                string path = Path.Combine(UserDataPath, "apikey");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string CachePath
        {
            get
            {
                string path = Path.Combine(UserDataPath, "caches");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string ConfigFilePath
        {
            get { return Path.Combine(_executablePath, "GameStoreLibraryManager.cfg"); }
        }
    }
}
