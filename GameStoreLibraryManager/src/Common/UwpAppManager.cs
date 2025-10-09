using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace GameStoreLibraryManager.Common
{
    public class UwpAppManager
    {
        public static async Task<IEnumerable<UwpAppInfo>> GetInstalledAppsAsync(SimpleLogger logger)
        {
            var apps = new List<UwpAppInfo>();
            try
            {
                var packageManager = new PackageManager();
                var packages = packageManager.FindPackagesForUser(string.Empty);
                foreach (var package in packages)
                {
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle || package.IsDevelopmentMode)
                    {
                        continue;
                    }

                    var manifest = (await package.GetAppListEntriesAsync()).FirstOrDefault();
                    if (manifest != null)
                    {
                        apps.Add(new UwpAppInfo
                        {
                            AppId = manifest.AppUserModelId,
                            DisplayName = manifest.DisplayInfo.DisplayName,
                            InstallLocation = package.InstalledLocation.Path
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[UwpAppManager] Failed to get UWP apps: {ex.Message}");
            }
            return apps;
        }
    }

    public class UwpAppInfo
    {
        public string AppId { get; set; }
        public string DisplayName { get; set; }
        public string InstallLocation { get; set; }
    }
}