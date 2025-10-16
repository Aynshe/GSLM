using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GameStoreLibraryManager.Gog;

namespace GameStoreLibraryManager.Common
{
    public class ExistingShortcut
    {
        public string Launcher { get; set; }
        public string GameId { get; set; }
        public bool IsInstalled { get; set; }
        public string FilePath { get; set; }
    }

    public static class ShortcutManager
    {
        public static void CreateShortcut(LauncherGameInfo game, Config config)
        {
            if (string.IsNullOrEmpty(game.Name))
            {
                return;
            }

            string romsPath;
            if (game.Launcher == "Steam") romsPath = PathManager.SteamRomsPath;
            else if (game.Launcher == "Epic") romsPath = PathManager.EpicRomsPath;
            else if (game.Launcher == "GOG") romsPath = PathManager.GogRomsPath;
            else if (game.Launcher == "Amazon") romsPath = PathManager.AmazonRomsPath;
            else if (game.Launcher == "Xbox") romsPath = PathManager.XboxRomsPath; // This correctly points to roms/windows
            else return;

            if (!game.IsInstalled)
            {
                if (game.LauncherUrl != null && game.LauncherUrl.StartsWith("internal://xboxcloudgaming-launch/"))
                {
                    romsPath = Path.Combine(romsPath, "Cloud Games");
                }
                else
                {
                    romsPath = Path.Combine(romsPath, "Not Installed");
                }
            }

            if (!Directory.Exists(romsPath))
            {
                Directory.CreateDirectory(romsPath);
            }

            string sanitizedName = StringUtils.SanitizeFileName(game.Name);

            if (game.LauncherUrl != null && game.LauncherUrl.StartsWith("internal://"))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                string shortcutArgs = "";
                if (game.LauncherUrl == "internal://luna") shortcutArgs = "-luna -fullscreen";
                else if (game.LauncherUrl == "internal://gslm-settings") shortcutArgs = "-menu";
                else if (game.LauncherUrl == "internal://xboxcloudgaming") shortcutArgs = "-xboxcloudgaming -fullscreen";
                else if (game.LauncherUrl.StartsWith("internal://xboxcloudgaming-launch/"))
                {
                    var gameId = game.LauncherUrl.Split('/').Last();
                    shortcutArgs = $"-xboxcloudgaming -fullscreen -launch {gameId}";
                }
                else return;

                string fileName = sanitizedName + ".bat";
                if (game.LauncherUrl == "internal://luna")
                {
                    fileName = "." + fileName;
                }
                string shortcutPath = Path.Combine(romsPath, fileName);

                if (File.Exists(shortcutPath)) return;

                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GameStoreLibraryManager.exe");
                string shortcutContent = $"@echo off\r\n\"{exePath}\" {shortcutArgs}\r\n";
                try
                {
                    File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
                    Console.WriteLine($"[Shortcut] Created shortcut for {game.Name} at {shortcutPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Shortcut] Failed to create shortcut {shortcutPath}: {ex.Message}");
                }
                return;
            }

            if (game.Launcher == "Xbox")
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                if (game.IsInstalled)
                {
                    if (game.LauncherUrl.StartsWith("msgamelaunch://"))
                    {
                        var shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.url");
                        if (File.Exists(shortcutPath)) return;
                        var shortcutContent = $"[InternetShortcut]\r\nURL={game.LauncherUrl}\r\n";
                        File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
                    }
                    else if (!string.IsNullOrEmpty(game.LauncherUrl) && File.Exists(game.LauncherUrl))
                    {
                        var shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.bat");
                        if (File.Exists(shortcutPath)) return;
                        string shortcutContent = $"@echo off\r\n\"{game.LauncherUrl}\"\r\n";
                        File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
                    }
                }
                else // Not installed
                {
                    var shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.url");
                    if (File.Exists(shortcutPath)) return;
                    var shortcutContent = $"[InternetShortcut]\r\nURL={game.LauncherUrl}\r\n";
                    File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
                }
            }
            else if (game.Launcher == "GOG")
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                var gogExePath = GogLibrary.GetGalaxyExecutablePath();
                if (string.IsNullOrEmpty(gogExePath) || !File.Exists(gogExePath)) return;

                var shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.lnk");
                if (File.Exists(shortcutPath)) return;

                var arguments = $"/command=runGame /gameId={game.Id}";
                Lnk.Create(shortcutPath, gogExePath, arguments, Path.GetDirectoryName(gogExePath), $"Launch {game.Name}");
            }
            else
            {
                string shortcutPath;
                string shortcutContent;
                bool useBatForEpic = game.Launcher == "Epic" && !game.IsInstalled && config.GetBoolean("epic_use_bat_for_not_installed", true);
                bool useBatForAmazon = game.Launcher == "Amazon" && !game.IsInstalled && config.GetBoolean("amazon_use_bat_for_not_installed", true);

                if (useBatForAmazon)
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                    shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.bat");
                    shortcutContent = $"@echo off\r\nstart \"\" \"amazon-games://\"\r\n" +
                                      $"timeout /t 5 /nobreak > NUL\r\n" +
                                      $"start \"\" \"{game.LauncherUrl}\"\r\nexit";
                }
                else if (useBatForEpic)
                {
                    shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.bat");
                    shortcutContent = $"@echo off\nstart \"\" \"{game.LauncherUrl}\"";
                }
                else
                {
                    shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.url");
                    shortcutContent = $"[InternetShortcut]\r\nURL={game.LauncherUrl}\r\n";
                }

                if (File.Exists(shortcutPath)) return;
                File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
            }
        }

        public static List<ExistingShortcut> GetAllExistingShortcuts(Config config)
        {
            var shortcuts = new List<ExistingShortcut>();

            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.SteamRomsPath, "Steam", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.SteamRomsPath, "Not Installed"), "Steam", false));
            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.EpicRomsPath, "Epic", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.EpicRomsPath, "Not Installed"), "Epic", false));
            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.AmazonRomsPath, "Amazon", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.AmazonRomsPath, "Not Installed"), "Amazon", false));

            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.XboxRomsPath, "Xbox", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.XboxRomsPath, "Not Installed"), "Xbox", false));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.XboxRomsPath, "Cloud Games"), "Xbox", false));

            if (config.GetBoolean("gog_import_installed", true) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CleanShortcuts(PathManager.GogRomsPath, "*.lnk");
            }

            return shortcuts;
        }

        private static void CleanShortcuts(string path, string pattern)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.GetFiles(path, pattern))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Shortcut] Failed to delete stale shortcut {file}: {ex.Message}");
                }
            }
        }

        private static List<ExistingShortcut> GetShortcutsFromDirectory(string path, string launcher, bool isInstalled)
        {
            var shortcuts = new List<ExistingShortcut>();
            if (!Directory.Exists(path)) return shortcuts;

            if (launcher == "GOG") return shortcuts;

            var files = Directory.GetFiles(path, "*.url").Concat(Directory.GetFiles(path, "*.bat"));

            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    string gameId = null;
                    var extension = Path.GetExtension(file).ToLower();

                    string url = null;
                    if (extension == ".url") url = GetUrlFromContent(content);
                    else if (extension == ".bat") url = GetUrlFromBatContent(content);

                    if (string.IsNullOrEmpty(url)) continue;

                    gameId = GetGameIdFromUrl(url, launcher, file);

                    if (!string.IsNullOrEmpty(gameId))
                    {
                        shortcuts.Add(new ExistingShortcut
                        {
                            Launcher = launcher,
                            GameId = gameId,
                            IsInstalled = isInstalled,
                            FilePath = file
                        });
                    }
                }
                catch { }
            }
            return shortcuts;
        }

        private static string GetUrlFromBatContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            var match = Regex.Match(content, @"GameStoreLibraryManager\.exe.*-launch\s+([^\s\""]+)");
            if (match.Success) return match.Groups[1].Value.Trim();

            match = Regex.Match(content, @"start\s+\""\""\s+\""([^""]+/(?:apps|install|play)/[^""]+)\""");
            if (match.Success) return match.Groups[1].Value.Trim();

            match = Regex.Match(content, @"^\""(.+?)\""\s*$", RegexOptions.Multiline);
            if (match.Success) return match.Groups[1].Value.Trim();

            match = Regex.Match(content, @"start\s+\""\""\s+\""(.+?)\""");
            if (match.Success) return match.Groups[1].Value.Trim();

            return null;
        }

        private static string GetUrlFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            var match = Regex.Match(content, @"URL=(.+)");
            if (match.Success) return match.Groups[1].Value.Trim();
            return null;
        }

        private static string GetGameIdFromUrl(string url, string launcher, string filePath)
        {
            if (string.IsNullOrEmpty(url)) return null;

            string gameId = null;

            if (launcher == "Steam")
            {
                var match = Regex.Match(url, @"steam://rungameid/(\d+)");
                if (match.Success) gameId = match.Groups[1].Value;
            }
            else if (launcher == "Epic")
            {
                var match = Regex.Match(url, @"com\.epicgames\.launcher://apps/([^?]+)");
                if (match.Success) gameId = match.Groups[1].Value;
            }
            else if (launcher == "Amazon")
            {
                var match = Regex.Match(url, @"amazon-games://(play|install)/([^?]+)");
                if (match.Success) gameId = match.Groups[2].Value;
            }
            else if (launcher == "Xbox")
            {
                var match = Regex.Match(url, @"ms-windows-store://pdp/\?PFN=([^&]+)");
                if (match.Success)
                {
                    gameId = match.Groups[1].Value;
                }
                else
                {
                    match = Regex.Match(url, @"ms-windows-store://pdp/\?productid=([^&]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        gameId = match.Groups[1].Value;
                    }
                    else
                    {
                        match = Regex.Match(url, @"msgamelaunch://shortcutLaunch/\?ProductId=([^&]+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            gameId = match.Groups[1].Value;
                        }
                        else if (File.Exists(url))
                        {
                            gameId = Path.GetFileNameWithoutExtension(url);
                        }
                        else if (!url.Contains("://"))
                        {
                            // This is not a URL, but the game ID extracted from the bat file content for cloud games
                            gameId = url;
                        }
                    }
                }
            }

            if (gameId != null && launcher == "Xbox" && filePath.Contains(Path.DirectorySeparatorChar + "Cloud Games" + Path.DirectorySeparatorChar))
            {
                return $"{gameId}-cloud";
            }

            return gameId;
        }
    }
}