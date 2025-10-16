using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using GameStoreLibraryManager.Common;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameStoreLibraryManager.Gog
{
    public class GogScraper
    {
        private readonly HttpClient _httpClient;
        private readonly SimpleLogger _logger;

        public GogScraper(SimpleLogger logger)
        {
            _httpClient = new HttpClient();
            _logger = logger;
        }

        public async Task<string> SearchGameIdAsync(string gameName)
        {
            try
            {
                var formattedGameName = HttpUtility.UrlEncode(gameName.Replace("-", " ").Replace("_", " ").Replace(":", ""));
                var searchUrl = $"https://www.gogdb.org/products?search={formattedGameName}";

                _logger.Log($"[GOG Scraper] Searching for '{gameName}' on gogdb.org: {searchUrl}");

                var response = await _httpClient.GetStringAsync(searchUrl);
                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(response);

                var rows = htmlDoc.DocumentNode.SelectNodes("//table[@id='product-table']/tr");
                if (rows == null)
                {
                    _logger.Log($"[GOG Scraper] No results found for '{gameName}'.");
                    return null;
                }

                foreach (var row in rows.Skip(1)) // Skip header row
                {
                    var nameNode = row.SelectSingleNode("./td[contains(@class, 'col-name')]/a");
                    var typeNode = row.SelectSingleNode("./td[@class='col-type']");
                    var idNode = row.SelectSingleNode("./td[@class='col-id']/a");

                    if (nameNode != null && typeNode != null && idNode != null)
                    {
                        var name = nameNode.InnerText.Trim();
                        var type = typeNode.InnerText.Trim();
                        var id = idNode.InnerText.Trim();

                        // Prioritize exact match and "Game" type
                        if (name.Equals(gameName, StringComparison.OrdinalIgnoreCase) && type.Equals("Game", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Log($"[GOG Scraper] Found exact match: '{name}' with ID '{id}'.");
                            return id;
                        }
                    }
                }

                // Fallback to first "Game" type result if no exact match is found
                foreach (var row in rows.Skip(1))
                {
                    var nameNode = row.SelectSingleNode("./td[contains(@class, 'col-name')]/a");
                    var typeNode = row.SelectSingleNode("./td[@class='col-type']");
                    var idNode = row.SelectSingleNode("./td[@class='col-id']/a");

                    if (nameNode != null && typeNode != null && idNode != null)
                    {
                        var name = nameNode.InnerText.Trim();
                        var type = typeNode.InnerText.Trim();
                        var id = idNode.InnerText.Trim();

                        if (type.Equals("Game", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Log($"[GOG Scraper] Found first game match: '{name}' with ID '{id}'.");
                            return id;
                        }
                    }
                }

                _logger.Log($"[GOG Scraper] No suitable 'Game' type found for '{gameName}'.");
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG Scraper] Error searching for game ID: {ex.Message}");
            }

            return null;
        }

        public async Task<GameDetails> GetGameDetailsAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;

            try
            {
                _logger.Log($"[GOG Scraper] Fetching details for GOG ID: {gameId}");
                var gameDetails = new GameDetails { MediaUrls = new Dictionary<string, string>() };

                // Fetch description and thumb
                var detailsUrl = $"https://api.gog.com/v2/games/{gameId}";
                var detailsResponse = await _httpClient.GetStringAsync(detailsUrl);
                var detailsJson = JObject.Parse(detailsResponse);

                var descriptionToken = detailsJson["description"];
                var description = "";
                if (descriptionToken is JObject)
                {
                    description = descriptionToken["full"]?.ToString();
                }
                else if (descriptionToken is JValue)
                {
                    description = descriptionToken.ToString();
                }

                if (!string.IsNullOrEmpty(description))
                {
                    // Truncate at the first newline or HTML tag
                    var match = Regex.Match(description, @"^([^<\n]*)");
                    gameDetails.Description = HttpUtility.HtmlDecode(match.Groups[1].Value.Trim());
                }

                var thumbUrl = detailsJson["_links"]?["boxArtImage"]?["href"]?.ToString();
                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    gameDetails.MediaUrls["thumb"] = thumbUrl;
                }

                // Fetch screenshot
                var productsUrl = $"https://api.gog.com/products/{gameId}?expand=screenshots";
                var productsResponse = await _httpClient.GetStringAsync(productsUrl);
                if (string.IsNullOrWhiteSpace(productsResponse) || productsResponse == "{}")
                {
                    _logger.Log($"[GOG Scraper] No product data found for GOG ID: {gameId}");
                }
                else
                {
                    var productsJson = JObject.Parse(productsResponse);
                    var screenshotNode = productsJson["screenshots"]?.FirstOrDefault();
                if (screenshotNode != null)
                {
                    var imageId = screenshotNode["image_id"]?.ToString();
                    if (!string.IsNullOrEmpty(imageId))
                    {
                        gameDetails.MediaUrls["image"] = $"https://images.gog-statics.com/{imageId}.jpg";
                    }
                }

                    gameDetails.Developer = productsJson["developers"]?.FirstOrDefault()?["name"]?.ToString();
                    gameDetails.Publisher = productsJson["publisher"]?.ToString();
                }

                if (gameDetails.MediaUrls.Count == 0 && string.IsNullOrEmpty(gameDetails.Description))
                {
                    _logger.Log($"[GOG Scraper] No media or description found for GOG ID: {gameId}");
                    return null;
                }

                return gameDetails;
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG Scraper] Error fetching game details for ID '{gameId}': {ex.Message}");
                return null;
            }
        }
    }
}