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

            var systemName = new DirectoryInfo(roms_path).Name;

            CleanGamelist(gameList);

            foreach (var game in games)
            {
                var gameId = game.Id;
                var sanitizedName = StringUtils.SanitizeFileName(game.Name);
                var isLuna = game.Launcher == "Amazon" && string.Equals(game.Id, "LUNA", StringComparison.OrdinalIgnoreCase);
                var displayName = isLuna ? "." + game.Name : game.Name;
                var fileNameBase = isLuna ? "." + sanitizedName : sanitizedName;

                string fileExtension;
                if (isLuna || game.Name == ".GSLM Settings") fileExtension = ".bat";
                else if (game.Launcher == "GOG") fileExtension = ".lnk";
                else if (game.Launcher == "Xbox" && game.IsInstalled && !game.LauncherUrl.StartsWith("msgamelaunch")) fileExtension = ".bat";
                else fileExtension = ".url";

                string path = game.IsInstalled ? $"./{fileNameBase}{fileExtension}" : $"./Not Installed/{fileNameBase}{fileExtension}";

                string finalName = allGameDetails.TryGetValue(gameId, out var gameDetails) && !string.IsNullOrEmpty(gameDetails.Name) ? gameDetails.Name : displayName;
                var gameElement = gameList.Elements("game").FirstOrDefault(g => StringUtils.NormalizeName(g.Element("name")?.Value) == StringUtils.NormalizeName(finalName));

                if (gameElement == null)
                {
                    gameElement = new XElement("game");
                    gameList.Add(gameElement);
                }

                UpdateElement(gameElement, "path", path);
                UpdateElement(gameElement, "name", finalName);

                if (gameDetails != null)
                {
                    UpdateElement(gameElement, "desc", gameDetails.Description);
                    UpdateElement(gameElement, "developer", gameDetails.Developer);
                    UpdateElement(gameElement, "publisher", gameDetails.Publisher);
                    UpdateElement(gameElement, "releasedate", FormatReleaseDate(gameDetails.ReleaseDate));
                    if (gameDetails.MediaUrls != null)
                    {
                        UpdateElement(gameElement, "image", gameDetails.MediaUrls.GetValueOrDefault("image"));
                        UpdateElement(gameElement, "video", gameDetails.MediaUrls.GetValueOrDefault("video"));
                        UpdateElement(gameElement, "marquee", gameDetails.MediaUrls.GetValueOrDefault("marquee"));
                        UpdateElement(gameElement, "thumbnail", gameDetails.MediaUrls.GetValueOrDefault("thumb"));
                        UpdateElement(gameElement, "fanart", gameDetails.MediaUrls.GetValueOrDefault("fanart"));
                    }
                }
            }

            if (systemName == "windows")
            {
                CreateFolderEntry(gameList, roms_path, "./Not Installed", "Not Installed", "Jeux non installés.", "xbox");
            }
            else
            {
                CreateFolderEntry(gameList, roms_path, "./Not Installed", "Not Installed", $"Jeux {systemName} non installés.", systemName.ToLower());
            }

            return doc;
        }

        private void CleanGamelist(XElement gameList)
        {
            if (!_config.GetBoolean("create_gslm_shortcut", true))
            {
                gameList.Elements("game").Where(g => g.Element("name")?.Value == ".GSLM Settings").Remove();
            }
            if (!_config.GetBoolean("enable_luna", false))
            {
                gameList.Elements("game").Where(g => g.Element("name")?.Value == ".Amazon Luna").Remove();
            }
        }

        private void CreateFolderEntry(XElement gameList, string roms_path, string relativePath, string displayName, string description, string themeName)
        {
            var fullPath = Path.GetFullPath(Path.Combine(roms_path, relativePath));
            if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);

            var folderElement = gameList.Elements("folder").FirstOrDefault(f => f.Element("path")?.Value == relativePath);
            if (folderElement == null)
            {
                folderElement = new XElement("folder");
                gameList.Add(folderElement);
            }

            UpdateElement(folderElement, "path", relativePath);
            UpdateElement(folderElement, "name", displayName);
            UpdateElement(folderElement, "desc", description);

            var imageDir = Path.Combine(roms_path, "images");
            var videoDir = Path.Combine(roms_path, "videos");

            CopyDefaultAsset($"{themeName}-image.png", imageDir);
            UpdateElement(folderElement, "image", $"./images/{themeName}-image.png");

            CopyDefaultAsset($"{themeName}-marquee.png", imageDir);
            UpdateElement(folderElement, "marquee", $"./images/{themeName}-marquee.png");

            CopyDefaultAsset($"{themeName}-fanart.png", imageDir);
            UpdateElement(folderElement, "fanart", $"./images/{themeName}-fanart.png");

            CopyDefaultAsset($"{themeName}-video.mp4", videoDir);
            UpdateElement(folderElement, "video", $"./videos/{themeName}-video.mp4");
        }

        private void UpdateElement(XElement parent, string elementName, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var element = parent.Element(elementName);
            if (element == null)
            {
                element = new XElement(elementName);
                parent.Add(element);
            }
            element.Value = value;
        }

        private void CopyDefaultAsset(string fileName, string destFolder)
        {
            try
            {
                var sourcePath = Path.Combine(AppContext.BaseDirectory, "img", fileName);
                if (!File.Exists(sourcePath)) return;

                var destPath = Path.Combine(destFolder, fileName);
                if (File.Exists(destPath)) return;

                Directory.CreateDirectory(destFolder);
                File.Copy(sourcePath, destPath, true);
            }
            catch (Exception ex)
            {
                _logger.Log($"[Gamelist] Error copying default asset '{fileName}': {ex.Message}");
            }
        }

        private string FormatReleaseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString)) return null;
            if (DateTime.TryParse(dateString, out var parsedDate))
            {
                return parsedDate.ToString("yyyyMMddTHHmmss");
            }
            string[] formats = { "dd MMM yyyy", "dd MMMM yyyy" };
            if (DateTime.TryParseExact(dateString, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsedDate))
            {
                return parsedDate.ToString("yyyyMMddTHHmmss");
            }
            return null;
        }
    }
}