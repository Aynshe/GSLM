using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GameStoreLibraryManager.HfsPlay;
using Newtonsoft.Json;
using HtmlAgilityPack;
using System.Linq;

namespace GameStoreLibraryManager.HfsPlay
{
    public class HfsPlayScraper
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://db.hfsplay.fr";

        public HfsPlayScraper()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Lynx/2.8.9rel.1 libwww-FM/2.14 SSL-MM/1.4.1 OpenSSL/1.1.1k");
        }

        public async Task<HfsPlaySearchResult> SearchGame(string gameName)
        {
            var searchUrl = $"{BaseUrl}/livesearch/{gameName}";
            var response = await _httpClient.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<HfsPlaySearchResult>(json);
        }

        public async Task<GameDetails> GetGameDetails(int gameId, string gameSlug)
        {
            var gameDetails = new GameDetails();
            var gameUrl = $"{BaseUrl}/games/{gameId}-{gameSlug}";
            var response = await _httpClient.GetStringAsync(gameUrl);

            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(response);

            gameDetails.Description = htmlDoc.DocumentNode.SelectSingleNode("//div[@id='item-description']")?.InnerText.Trim();
            gameDetails.Developer = htmlDoc.DocumentNode.SelectSingleNode("//span[@name='developer']")?.InnerText.Trim();
            gameDetails.Publisher = htmlDoc.DocumentNode.SelectSingleNode("//span[@name='editor']")?.InnerText.Trim();

            var dateFields = new[] { "released_at_WORLD", "released_at_US", "released_at_PAL", "released_at_JPN" };
            foreach (var field in dateFields)
            {
                var dateNode = htmlDoc.DocumentNode.SelectSingleNode($"//span[@name='{field}']");
                if (dateNode != null)
                {
                    var dateText = dateNode.InnerText.Trim();
                    if (!string.IsNullOrEmpty(dateText) && dateText.ToUpper() != "N.C.")
                    {
                        gameDetails.ReleaseDate = dateText;
                        break;
                    }
                }
            }

            var mediaUrls = new Dictionary<string, string>();
            var mediaTypes = new Dictionary<string, string>
            {
                { "logo", "marquee" },
                { "cover3d", "thumb" },
                { "video", "video" },
                { "screenshot", "image" },
                { "wallpaper", "fanart" }
            };

            foreach (var mediaType in mediaTypes)
            {
                var mediaContainerNode = htmlDoc.DocumentNode.SelectSingleNode($"//div[@data-type='{mediaType.Key}']");
                if (mediaContainerNode != null && mediaContainerNode.InnerText.Contains("No media available"))
                {
                    continue;
                }

                var node = htmlDoc.DocumentNode.SelectSingleNode($"//div[@data-type='{mediaType.Key}']//div[contains(@class, 'show-media')]");
                if (node != null)
                {
                    var url = node.GetAttributeValue("data-src", string.Empty);
                    if (!string.IsNullOrEmpty(url) && !url.Contains("/images/medias/default.png"))
                    {
                        mediaUrls[mediaType.Value] = BaseUrl + url;
                    }
                }
                else
                {
                    // Fallback for video and other non-image media that use an <a> tag
                    node = htmlDoc.DocumentNode.SelectSingleNode($"//div[@data-type='{mediaType.Key}']//a[contains(@class, 'open-media')]");
                    if (node != null)
                    {
                        var url = node.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(url) && !url.Contains("/images/medias/default.png"))
                        {
                            mediaUrls[mediaType.Value] = BaseUrl + url;
                        }
                    }
                }
            }

            // Special case for the main image if no screenshot is found
            if (!mediaUrls.ContainsKey("image"))
            {
                var mainImageNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id='item-header']//img");
                if (mainImageNode != null)
                {
                    var url = mainImageNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(url))
                    {
                        mediaUrls["image"] = BaseUrl + url;
                    }
                }
            }

            gameDetails.MediaUrls = mediaUrls;
            return gameDetails;
        }
    }
}
