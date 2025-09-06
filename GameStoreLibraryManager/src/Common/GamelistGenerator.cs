using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GameStoreLibraryManager.HfsPlay;

namespace GameStoreLibraryManager.Common
{
    public class GamelistGenerator
    {
        private readonly Config _config;
        private readonly SimpleLogger _logger;

        public GamelistGenerator(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public XDocument GenerateGamelist(string roms_path, List<LauncherGameInfo> games, Dictionary<string, GameDetails> allGameDetails)
        {
            var gamelistPath = Path.Combine(roms_path, "gamelist.xml");
            XDocument doc;
            XElement gameList;

            if (File.Exists(gamelistPath))
            {
                try
                {
                    doc = XDocument.Load(gamelistPath);
                    gameList = doc.Element("gameList");
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Gamelist] Error loading existing gamelist.xml: {ex.Message}. A new one will be created.");
                    doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"));
                    gameList = new XElement("gameList");
                    doc.Add(gameList);
                }
            }
            else
            {
                doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"));
                gameList = new XElement("gameList");
                doc.Add(gameList);
            }

            // Step 1: Clean up duplicates
            var duplicates = gameList.Elements("game")
                                     .GroupBy(g => StringUtils.NormalizeName(g.Element("name")?.Value))
                                     .Where(group => group.Count() > 1);

            foreach (var group in duplicates)
            {
                _logger.Log($"[Gamelist] Found duplicate entries for '{group.Key}'. Merging.");
                // Order by the one with the most metadata, keeping it
                var bestEntry = group.OrderByDescending(g => g.Elements().Count()).First();
                // Remove all others
                foreach (var elementToRemove in group.Where(g => g != bestEntry))
                {
                    elementToRemove.Remove();
                }
            }

            // Step 1.5: If GSLM shortcut is disabled, remove existing entries from gamelist
            if (!_config.GetBoolean("create_gslm_shortcut", true))
            {
                var gslmKey = StringUtils.NormalizeName(".GSLM Settings");
                var toRemove = gameList.Elements("game")
                    .Where(g => StringUtils.NormalizeName(g.Element("name")?.Value) == gslmKey
                                 || (g.Element("path")?.Value?.EndsWith(".GSLM Settings.bat", StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
                foreach (var e in toRemove)
                {
                    _logger.Log("[Gamelist] Removing .GSLM Settings entry (create_gslm_shortcut=false).");
                    e.Remove();
                }
            }

            // Step 1.5: If Luna is disabled, remove existing Luna entries from gamelist
            if (!_config.GetBoolean("enable_luna", false))
            {
                var lunaKey = StringUtils.NormalizeName("Amazon Luna");
                var toRemove = gameList.Elements("game")
                    .Where(g => StringUtils.NormalizeName(g.Element("name")?.Value) == lunaKey
                             || (g.Element("path")?.Value?.EndsWith(".Amazon Luna.bat", StringComparison.OrdinalIgnoreCase) ?? false)
                             || (g.Element("path")?.Value?.EndsWith(".Amazon Luna.url", StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
                foreach (var e in toRemove)
                {
                    _logger.Log("[Gamelist] Removing Amazon Luna entry (enable_luna=false).");
                    e.Remove();
                }
            }

            // Step 2: Reconcile with current game list
            foreach (var game in games)
            {
                var gameId = game.Id;
                var sanitizedName = StringUtils.SanitizeFileName(game.Name);
                var isLuna = game.Launcher == "Amazon" && string.Equals(game.Id, "LUNA", StringComparison.OrdinalIgnoreCase);
                var displayName = isLuna ? "." + game.Name : game.Name;
                var fileNameBase = isLuna ? "." + sanitizedName : sanitizedName;

                string fileExtension;
                if ((game.Launcher == "Amazon" && string.Equals(game.Id, "LUNA", StringComparison.OrdinalIgnoreCase)) || game.Name == ".GSLM Settings")
                {
                    // Synthetic entries always use a .bat launcher
                    fileExtension = ".bat";
                }
                else if (game.Launcher == "GOG")
                {
                    fileExtension = ".lnk";
                }
                else if (game.Launcher == "Epic" && !game.IsInstalled && _config.GetBoolean("epic_use_bat_for_not_installed", true))
                {
                    fileExtension = ".bat";
                }
                else if (game.Launcher == "Amazon" && !game.IsInstalled && _config.GetBoolean("amazon_use_bat_for_not_installed", true))
                {
                    fileExtension = ".bat";
                }
                else
                {
                    fileExtension = ".url";
                }
                var path = game.IsInstalled ? $"./{fileNameBase}{fileExtension}" : $"./Not Installed/{fileNameBase}{fileExtension}";

                // Determine the final name that will be written to the XML for lookup consistency
                string finalName;
                if (allGameDetails.TryGetValue(gameId, out var gameDetails) && !string.IsNullOrEmpty(gameDetails.Name))
                {
                    finalName = gameDetails.Name;
                }
                else
                {
                    finalName = displayName;
                }

                var gameElement = gameList.Elements("game").FirstOrDefault(g => StringUtils.NormalizeName(g.Element("name")?.Value) == StringUtils.NormalizeName(finalName));

                if (gameElement == null)
                {
                    // Game not in XML, create it
                    gameElement = new XElement("game");
                    gameList.Add(gameElement);
                }

                // Always update path and name to be sure
                UpdateElement(gameElement, "path", path);
                UpdateElement(gameElement, "name", finalName);

                if (isLuna)
                {
                    UpdateElement(gameElement, "developer", "Amazon");
                    UpdateElement(gameElement, "publisher", "Amazon");
                }

                // Update metadata if we have it from the scraper
                if (gameDetails != null) // gameDetails was already fetched for the name lookup
                {
                    UpdateElement(gameElement, "desc", gameDetails.Description);
                    UpdateElement(gameElement, "developer", gameDetails.Developer);
                    UpdateElement(gameElement, "publisher", gameDetails.Publisher);

                    var formattedDate = FormatReleaseDate(gameDetails.ReleaseDate);
                    UpdateElement(gameElement, "releasedate", formattedDate);

                    if (gameDetails.MediaUrls != null)
                    {
                        UpdateElement(gameElement, "image", gameDetails.MediaUrls.GetValueOrDefault("image"));
                        UpdateElement(gameElement, "video", gameDetails.MediaUrls.GetValueOrDefault("video"));
                        UpdateElement(gameElement, "marquee", gameDetails.MediaUrls.GetValueOrDefault("marquee"));
                        UpdateElement(gameElement, "thumbnail", gameDetails.MediaUrls.GetValueOrDefault("thumb"));
                        UpdateElement(gameElement, "fanart", gameDetails.MediaUrls.GetValueOrDefault("fanart"));
                    }
                }
                else
                {
                    // If no details, just update name
                    UpdateElement(gameElement, "name", displayName);
                }
            }

            AddNotInstalledFolder(roms_path, gameList);

            return doc;
        }

        private void UpdateElement(XElement parent, string elementName, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                // Do not create empty tags, but remove existing ones if the new value is empty
                var existingElement = parent.Element(elementName);
                if (existingElement != null)
                {
                    // Decide if we should remove it. For now, we'll leave it.
                    // To remove: existingElement.Remove();
                }
                return;
            }

            var element = parent.Element(elementName);
            if (element == null)
            {
                element = new XElement(elementName);
                parent.Add(element);
            }

            element.Value = value;
        }

        private void AddNotInstalledFolder(string romsPath, XElement gameList)
        {
            var notInstalledPath = Path.Combine(romsPath, "Not Installed");
            if (!Directory.Exists(notInstalledPath))
            {
                Directory.CreateDirectory(notInstalledPath);
            }

            var folderElement = gameList.Elements("folder").FirstOrDefault(f => f.Element("path")?.Value == "./Not Installed");
            if (folderElement == null)
            {
                folderElement = new XElement("folder");
                gameList.Add(folderElement);
            }

            var storeName = new DirectoryInfo(romsPath).Name.ToLower();
            var imageSubfolder = "images";
            var videoSubfolder = "videos";
            var destImageFolder = Path.Combine(romsPath, imageSubfolder);
            var destVideoFolder = Path.Combine(romsPath, videoSubfolder);

            UpdateElement(folderElement, "path", "./Not Installed");
            UpdateElement(folderElement, "name", "Not Installed");

            var marqueeFile = $"{storeName}-marquee.png";
            CopyDefaultImage(marqueeFile, destImageFolder);
            UpdateElement(folderElement, "marquee", $"./{imageSubfolder}/{marqueeFile}");

            var fanartFile = $"{storeName}-fanart.png";
            CopyDefaultImage(fanartFile, destImageFolder);
            UpdateElement(folderElement, "fanart", $"./{imageSubfolder}/{fanartFile}");

            // Additional media for folder entry
            var imageFile = $"{storeName}-image.png";
            CopyDefaultImage(imageFile, destImageFolder);
            UpdateElement(folderElement, "image", $"./{imageSubfolder}/{imageFile}");

            var thumbFile = $"{storeName}-thumb.png";
            CopyDefaultImage(thumbFile, destImageFolder);
            UpdateElement(folderElement, "thumbnail", $"./{imageSubfolder}/{thumbFile}");

            var videoFile = $"{storeName}-video.mp4";
            CopyDefaultBinary(videoFile, destVideoFolder);
            UpdateElement(folderElement, "video", $"./{videoSubfolder}/{videoFile}");

            // Descriptive text per store
            string storeDisplay = storeName switch
            {
                "steam" => "Steam",
                "epic" => "Epic Games",
                "gog" => "GOG",
                "amazon" => "Amazon Games",
                _ => storeName
            };
            UpdateElement(folderElement, "desc", $"Jeux {storeDisplay} non installés.");

            var releaseDate = (storeName == "steam") ? "12 September 2003" : "6 December 2018";
            UpdateElement(folderElement, "releasedate", FormatReleaseDate(releaseDate));
        }

        private void CopyDefaultImage(string fileName, string destFolder)
        {
            try
            {
                var sourceExePath = AppContext.BaseDirectory;
                var sourceImagePath = Path.Combine(sourceExePath, "img", fileName);
                var destImagePath = Path.Combine(destFolder, fileName);

                if (!File.Exists(sourceImagePath))
                {
                    _logger.Log($"  [Gamelist] Source image not found, cannot copy: {sourceImagePath}");
                    return;
                }

                if (File.Exists(destImagePath))
                {
                    return; // Already exists
                }

                _logger.Log($"  [Gamelist] Copying '{sourceImagePath}' to '{destImagePath}'");
                Directory.CreateDirectory(destFolder);
                File.Copy(sourceImagePath, destImagePath);
            }
            catch (Exception ex)
            {
                _logger.Log($"  [Gamelist] Error copying default image: {ex.Message}");
            }
        }

        private void CopyDefaultBinary(string fileName, string destFolder)
        {
            try
            {
                var sourceExePath = AppContext.BaseDirectory;
                var sourcePath = Path.Combine(sourceExePath, "img", fileName);
                var destPath = Path.Combine(destFolder, fileName);

                if (!File.Exists(sourcePath))
                {
                    _logger.Log($"  [Gamelist] Source asset not found, cannot copy: {sourcePath}");
                    return;
                }

                if (File.Exists(destPath))
                {
                    return; // Already exists
                }

                _logger.Log($"  [Gamelist] Copying '{sourcePath}' to '{destPath}'");
                Directory.CreateDirectory(destFolder);
                File.Copy(sourcePath, destPath);
            }
            catch (Exception ex)
            {
                _logger.Log($"  [Gamelist] Error copying default asset: {ex.Message}");
            }
        }

        private string FormatReleaseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
            {
                return null;
            }

            DateTime parsedDate;
            if (DateTime.TryParse(dateString, out parsedDate))
            {
                return parsedDate.ToString("yyyyMMddTHHmmss");
            }

            string[] formats = { "dd MMM yyyy", "dd MMMM yyyy" };
            if (DateTime.TryParseExact(dateString, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsedDate))
            {
                return parsedDate.ToString("yyyyMMddTHHmmss");
            }

            if (DateTime.TryParseExact(dateString, formats, new System.Globalization.CultureInfo("fr-FR"), System.Globalization.DateTimeStyles.None, out parsedDate))
            {
                return parsedDate.ToString("yyyyMMddTHHmmss");
            }

            return null;
        }
    }
}
