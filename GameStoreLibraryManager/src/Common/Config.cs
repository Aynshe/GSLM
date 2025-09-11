using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameStoreLibraryManager.Common
{
    public class Config
    {
        public class ConfigOption
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public string Comment { get; set; }
        }

        public const string SectionHeaderKey = "_SECTION_HEADER_";
        private Dictionary<string, string> _settings;
        private readonly List<ConfigOption> _defaultSettings = new List<ConfigOption>
        {
            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Steam" },
            new ConfigOption { Key = "steam_import_installed", Value = "false", Comment = "Import installed games from the Steam launcher." },
            new ConfigOption { Key = "steam_enable_api_generation", Value = "false", Comment = "Automatically open embedded UI to capture Steam API key (steam.apikey)." },
            new ConfigOption { Key = "steam_enable_install_automation", Value = "false", Comment = "Enable UI automation to click 'Install' in Steam install dialog when using -installstore mode." },
            new ConfigOption { Key = "steam_api_delay", Value = "false", Comment = "Add a delay between Steam API calls to avoid rate limiting." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Epic Games" },
            new ConfigOption { Key = "epic_import_installed", Value = "false", Comment = "Import installed games from the Epic Games launcher." },
            new ConfigOption { Key = "epic_enable_token_generation", Value = "false", Comment = "Automatically open embedded UI to capture Epic authorization code and generate token." },
            new ConfigOption { Key = "epic_enable_install_automation", Value = "false", Comment = "Enable UI automation for Epic install dialog when using -installstore mode." },
            new ConfigOption { Key = "epic_use_bat_for_not_installed", Value = "true", Comment = "[Workaround] Create .bat files for non-installed Epic games." },
            new ConfigOption { Key = "epic_execute_game_after_install", Value = "false", Comment = "Execute the game after automatic installation." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "GOG" },
            new ConfigOption { Key = "gog_import_installed", Value = "false", Comment = "Import installed and owned games from the GOG launcher." },
            new ConfigOption { Key = "gog_enable_token_generation", Value = "false", Comment = "Automatically open embedded UI to capture GOG authorization code and generate token." },
            new ConfigOption { Key = "gog_enable_install_automation", Value = "false", Comment = "Enable UI automation for GOG install dialog when using -installstore mode." },
            new ConfigOption { Key = "gog_execute_game_after_install", Value = "false", Comment = "Execute the game after automatic installation." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Amazon Games" },
            new ConfigOption { Key = "amazon_import_installed", Value = "false", Comment = "Import installed games from the Amazon Games launcher." },
            new ConfigOption { Key = "amazon_enable_token_generation", Value = "false", Comment = "Enable the generation of an Amazon authentication token." },
            new ConfigOption { Key = "amazon_enable_install_automation", Value = "false", Comment = "Enable UI automation for Amazon Games install dialog when using -installstore mode." },
            new ConfigOption { Key = "amazon_use_bat_for_not_installed", Value = "true", Comment = "[Workaround] Create .bat files for non-installed Prime games (Amazon)." },
            new ConfigOption { Key = "amazon_execute_game_after_install", Value = "false", Comment = "Execute the game after automatic installation." },
            new ConfigOption { Key = "enable_luna", Value = "false", Comment = "Enable Amazon Luna synthetic entry and kiosk mode shortcut." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Global Options - Scraping" },
            new ConfigOption { Key = "scrape_media", Value = "false", Comment = "Enable downloading of metadata and media (images, videos, etc.)." },
            new ConfigOption { Key = "force_steam_first", Value = "true", Comment = "Force searching on the Steam store before HFSPlay." },
            new ConfigOption { Key = "rescrape_incomplete_games", Value = "false", Comment = "Force metadata scraping for games that already have some, but incomplete, metadata." },
            new ConfigOption { Key = "create_gslm_shortcut", Value = "true", Comment = "Create a shortcut to GSLM settings in each store's roms directory." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Global Options - Security" },
            new ConfigOption { Key = "enable_dpapi_protection", Value = "false", Comment = "Protect *.token and *.apikey files with Windows DPAPI (CurrentUser)." },

            new ConfigOption { Key = SectionHeaderKey, Value = "", Comment = "Global Options - Display" },
            new ConfigOption { Key = "screen_index", Value = "0", Comment = "Index of the screen to display the menu on (0-indexed)." }
        };

        public Config()
        {
            _settings = new Dictionary<string, string>();
            LoadAndCreate();
        }

        private void LoadAndCreate()
        {
            string configPath = PathManager.ConfigFilePath;
            if (!File.Exists(configPath))
            {
                // Create the file with default settings
                _settings = _defaultSettings
                    .Where(s => s.Key != SectionHeaderKey)
                    .ToDictionary(k => k.Key, v => v.Value);
                SaveChanges();
                return;
            }

            // Load existing settings
            var lines = File.ReadAllLines(configPath).ToList();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    _settings[parts[0].Trim()] = parts[1].Trim();
                }
            }

            // Add missing settings and save if necessary
            bool updated = false;
            foreach (var defaultSetting in _defaultSettings)
            {
                if (defaultSetting.Key == SectionHeaderKey) continue;

                if (!_settings.ContainsKey(defaultSetting.Key))
                {
                    _settings[defaultSetting.Key] = defaultSetting.Value;
                    updated = true;
                }
            }

            if (updated)
            {
                SaveChanges();
            }
        }

        private void SaveChanges()
        {
            string configPath = PathManager.ConfigFilePath;
            var lines = new List<string>();
            foreach (var setting in _defaultSettings)
            {
                if (setting.Key == SectionHeaderKey)
                {
                    lines.Add("");
                    lines.Add($"#----------------------------------------------------------------#");
                    lines.Add($"# {setting.Comment}");
                    lines.Add($"#----------------------------------------------------------------#");
                }
                else
                {
                    lines.Add($"# {setting.Comment}");
                    lines.Add($"{setting.Key}={_settings[setting.Key]}");
                }
                lines.Add("");
            }
            File.WriteAllLines(configPath, lines);
        }

        public bool GetBoolean(string key, bool defaultValue)
        {
            if (_settings.TryGetValue(key, out string value))
            {
                if (bool.TryParse(value, out bool result))
                {
                    return result;
                }
            }
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            if (_settings.TryGetValue(key, out string value))
            {
                if (int.TryParse(value, out int result))
                {
                    return result;
                }
            }
            return defaultValue;
        }

        public string GetString(string key, string defaultValue = "")
        {
            if (_settings.TryGetValue(key, out string value))
            {
                return value ?? defaultValue;
            }
            return defaultValue;
        }

        public List<ConfigOption> GetSettings()
        {
            // Return a copy to prevent external modification of the private list
            return new List<ConfigOption>(_defaultSettings);
        }

        public void SaveSettings(Dictionary<string, string> newSettings)
        {
            foreach (var setting in newSettings)
            {
                _settings[setting.Key] = setting.Value;
            }
            SaveChanges();
        }
    }
}
