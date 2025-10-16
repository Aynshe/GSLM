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

        public void UpdateOrAddGameEntry(string romsPath, LauncherGameInfo game, GameDetails details)
        {
            var gamelistPath = Path.Combine(romsPath, "gamelist.xml");
            XDocument doc;
            XElement gameListElement;

            try
            {
                if (File.Exists(gamelistPath))
                {
                    doc = XDocument.Load(gamelistPath);
                    gameListElement = doc.Root;
                    if (gameListElement?.Name != "gameList")
                    {
                        _logger.Log($"[Gamelist] Root element in {gamelistPath} is not <gameList>. Creating a new gamelist.");
                        doc = new XDocument(new XElement("gameList"));
                        gameListElement = doc.Root;
                    }
                }
                else
                {
                    doc = new XDocument(new XElement("gameList"));
                    gameListElement = doc.Root;
                }

                string sanitizedName = StringUtils.SanitizeFileName(game.Name);
                string fileExtension = GetFileExtension(game);
                string fileName = $"{sanitizedName}{fileExtension}";
                string relativePath = GetRelativePath(game, fileName);

                var existingGame = gameListElement.Elements("game")
                                                  .FirstOrDefault(g => StringUtils.NormalizeName(g.Element("name")?.Value) == StringUtils.NormalizeName(game.Name));

                if (existingGame != null)
                {
                    UpdateElement(existingGame, "path", relativePath); // Ensure path is up-to-date
                    UpdateGameData(existingGame, game, details);
                }
                else
                {
                    var newGameElement = new XElement("game");
                    newGameElement.Add(new XElement("path", relativePath));
                    UpdateGameData(newGameElement, game, details);
                    gameListElement.Add(newGameElement);
                }

                doc.Save(gamelistPath);
                _logger.Log($"[Gamelist] Updated entry for '{game.Name}' in {gamelistPath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"[Gamelist] Failed to update gamelist at {gamelistPath}: {ex.Message}");
            }
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
                string sanitizedName = StringUtils.SanitizeFileName(game.Name);
                string fileExtension = GetFileExtension(game);
                string fileName = $"{sanitizedName}{fileExtension}";
                string relativePath = GetRelativePath(game, fileName);

                var gameElement = gameList.Elements("game").FirstOrDefault(g => StringUtils.NormalizeName(g.Element("name")?.Value) == StringUtils.NormalizeName(game.Name));

                if (gameElement == null)
                {
                    gameElement = new XElement("game");
                    gameList.Add(gameElement);
                }

                UpdateElement(gameElement, "path", relativePath); // Ensure path is always correct
                GameDetails details = allGameDetails.TryGetValue(game.Id, out var d) ? d : null;
                UpdateGameData(gameElement, game, details);
            }

            if (systemName == "windows")
            {
                CreateFolderEntry(gameList, roms_path, "./Not Installed", "Not Installed", "Game not installed.", "xbox");
            }
            else
            {
                CreateFolderEntry(gameList, roms_path, "./Not Installed", "Not Installed", $"Game {systemName} not installed.", systemName.ToLower());
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

        private void UpdateGameData(XElement gameElement, LauncherGameInfo game, GameDetails details)
        {
            UpdateElement(gameElement, "name", game.Name);
            UpdateElement(gameElement, "releasedate", "20000101T000000"); // Default release date

            if (details != null)
            {
                if (!string.IsNullOrEmpty(details.Description)) UpdateElement(gameElement, "desc", details.Description);
                if (!string.IsNullOrEmpty(details.Developer)) UpdateElement(gameElement, "developer", details.Developer);
                if (!string.IsNullOrEmpty(details.Publisher)) UpdateElement(gameElement, "publisher", details.Publisher);
                if (!string.IsNullOrEmpty(details.ReleaseDate)) UpdateElement(gameElement, "releasedate", details.ReleaseDate);

                foreach (var media in details.MediaUrls)
                {
                    UpdateElement(gameElement, media.Key, media.Value);
                }
            }
        }

        private string GetRelativePath(LauncherGameInfo game, string fileName)
        {
            if (game.LauncherUrl != null && game.LauncherUrl.StartsWith("internal://xboxcloudgaming-launch/"))
            {
                return $"./Cloud Games/{fileName}";
            }
            return game.IsInstalled ? $"./{fileName}" : $"./Not Installed/{fileName}";
        }

        private string GetFileExtension(LauncherGameInfo game)
        {
            if (game.LauncherUrl != null && game.LauncherUrl.StartsWith("internal://"))
            {
                return ".bat";
            }

            if (game.Launcher == "GOG")
            {
                return ".lnk";
            }

            if (game.Launcher == "Xbox")
            {
                return (game.IsInstalled && game.LauncherUrl.StartsWith("msgamelaunch://")) ? ".url" : ".bat";
            }

            bool useBatForEpic = game.Launcher == "Epic" && !game.IsInstalled && _config.GetBoolean("epic_use_bat_for_not_installed", true);
            bool useBatForAmazon = game.Launcher == "Amazon" && !game.IsInstalled && _config.GetBoolean("amazon_use_bat_for_not_installed", true);

            return (useBatForEpic || useBatForAmazon) ? ".bat" : ".url";
        }
    }
}
