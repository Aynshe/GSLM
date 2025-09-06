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
            if (game.Launcher == "Steam")
                romsPath = PathManager.SteamRomsPath;
            else if (game.Launcher == "Epic")
                romsPath = PathManager.EpicRomsPath;
            else if (game.Launcher == "GOG")
                romsPath = PathManager.GogRomsPath;
            else if (game.Launcher == "Amazon")
                romsPath = PathManager.AmazonRomsPath;
            else
                return;

            if (!game.IsInstalled)
            {
                romsPath = Path.Combine(romsPath, "Not Installed");
                if (!Directory.Exists(romsPath))
                    Directory.CreateDirectory(romsPath);
            }

            string sanitizedName = StringUtils.SanitizeFileName(game.Name);

            // Handle all synthetic .bat entries first, as per user instruction
            if (game.LauncherUrl != null && game.LauncherUrl.StartsWith("internal://"))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                string shortcutArgs = "";
                if (game.LauncherUrl == "internal://luna")
                {
                    shortcutArgs = "-luna -fullscreen";
                }
                else if (game.LauncherUrl == "internal://gslm-settings")
                {
                    shortcutArgs = "-menu";
                }
                else
                {
                    return; // Unknown internal protocol
                }

                string fileName;
                if (game.LauncherUrl == "internal://luna")
                {
                    // For Luna, the name is "Amazon Luna", so we must prepend the dot.
                    fileName = "." + game.Name + ".bat";
                }
                else
                {
                    // For GSLM, the name is ".GSLM Settings", so it already has the dot.
                    fileName = game.Name + ".bat";
                }
                string shortcutPath = Path.Combine(romsPath, fileName);

                if (File.Exists(shortcutPath)) return;

                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GameStoreLibraryManager.exe");
                string shortcutContent = $"@echo off\r\n\"{exePath}\" {shortcutArgs}\r\n";
                File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
                return; // IMPORTANT: Exit after handling the synthetic entry
            }

            // --- Original, working logic for real games ---

            if (game.Launcher == "GOG")
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                var gogExePath = GogLibrary.GetGalaxyExecutablePath();
                if (string.IsNullOrEmpty(gogExePath) || !File.Exists(gogExePath))
                {
                    Console.WriteLine($"[GOG] Could not find GOG Galaxy executable. Cannot create shortcut for {game.Name}");
                    return;
                }

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
                    var sb = new StringBuilder();
                    sb.AppendLine("@echo off");
                    sb.AppendLine("start \"\" \"amazon-games://\"");
                    sb.AppendLine("timeout /t 5 /nobreak > NUL");
                    sb.AppendLine($"start \"\" \"{game.LauncherUrl}\"");
                    sb.AppendLine("exit");
                    shortcutContent = sb.ToString();
                }
                else if (useBatForEpic)
                {
                    shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.bat");
                    shortcutContent = $"@echo off\nstart \"\" \"{game.LauncherUrl}\"";
                }
                else
                {
                    shortcutPath = Path.Combine(romsPath, $"{sanitizedName}.url");
                    shortcutContent = $"[InternetShortcut]\nURL={game.LauncherUrl}";
                }

                if (File.Exists(shortcutPath))
                    return;

                File.WriteAllText(shortcutPath, shortcutContent, Encoding.UTF8);
            }
        }

        public static List<ExistingShortcut> GetAllExistingShortcuts(Config config)
        {
            var shortcuts = new List<ExistingShortcut>();

            // Steam, Epic, and Amazon use parsing to reconcile
            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.SteamRomsPath, "Steam", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.SteamRomsPath, "Not Installed"), "Steam", false));
            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.EpicRomsPath, "Epic", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.EpicRomsPath, "Not Installed"), "Epic", false));
            shortcuts.AddRange(GetShortcutsFromDirectory(PathManager.AmazonRomsPath, "Amazon", true));
            shortcuts.AddRange(GetShortcutsFromDirectory(Path.Combine(PathManager.AmazonRomsPath, "Not Installed"), "Amazon", false));

            // For GOG, we can't parse .lnk, so we adopt a delete-and-recreate strategy.
            // We delete all existing .lnk files here, and they will be recreated later in the main loop.
            if (config.GetBoolean("gog_import_installed", true) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CleanShortcuts(PathManager.GogRomsPath);
                CleanShortcuts(Path.Combine(PathManager.GogRomsPath, "Not Installed"));
            }

            return shortcuts;
        }

        private static void CleanShortcuts(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.GetFiles(path, "*.lnk"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Shortcut] Failed to delete stale GOG shortcut {file}: {ex.Message}");
                }
            }
        }

        private static List<ExistingShortcut> GetShortcutsFromDirectory(string path, string launcher, bool isInstalled)
        {
            var shortcuts = new List<ExistingShortcut>();
            if (!Directory.Exists(path))
                return shortcuts;

            // GOG uses .lnk and is handled by CleanShortcuts, so we don't need to parse them here.
            if (launcher == "GOG") return shortcuts;

            var files = Directory.GetFiles(path, "*.url").Concat(Directory.GetFiles(path, "*.bat"));

            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    string url = null;
                    var extension = Path.GetExtension(file).ToLower();

                    if (extension == ".url")
                    {
                        url = GetUrlFromContent(content);
                    }
                    else if (extension == ".bat")
                    {
                        url = GetUrlFromBatContent(content);
                    }

                    if (string.IsNullOrEmpty(url)) continue;

                    string gameId = GetGameIdFromUrl(url, launcher);

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
            // Match launcher URLs that contain an install/apps/play path, to avoid grabbing a generic launcher start command.
            var match = Regex.Match(content, @"start\s+\""\""\s+\""([^""]+/(?:apps|install|play)/[^""]+)\""");
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // Fallback for simple bat files
            match = Regex.Match(content, @"start\s+\""\""\s+\""(.+?)\""");
            if (match.Success)
                return match.Groups[1].Value.Trim();

            return null;
        }

        private static string GetUrlFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            var match = Regex.Match(content, @"URL=(.+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();
            return null;
        }

        private static string GetGameIdFromUrl(string url, string launcher)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (launcher == "Steam")
            {
                var match = Regex.Match(url, @"steam://rungameid/(\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            else if (launcher == "Epic")
            {
                var match = Regex.Match(url, @"com\.epicgames\.launcher://apps/([^?]+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            else if (launcher == "Amazon")
            {
                var match = Regex.Match(url, @"amazon-games://(play|install)/([^?]+)");
                if (match.Success)
                    return match.Groups[2].Value;
            }

            return null;
        }
    }
}
