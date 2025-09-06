using System.Threading.Tasks;
using GameStoreLibraryManager.HfsPlay;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;
using GameStoreLibraryManager.Common;
using System.Net.Http.Headers;

namespace GameStoreLibraryManager.Epic
{
    public class EpicStoreScraper
    {
        private readonly HttpClient _httpClient;
        private const string ApiUrl = "https://store.epicgames.com/graphql";

        public EpicStoreScraper()
        {
            _httpClient = new HttpClient();
        }

        public async Task<GameDetails> GetGameDetails(string catalogItemId, string accessToken, SimpleLogger logger)
        {
            if (string.IsNullOrEmpty(catalogItemId) || string.IsNullOrEmpty(accessToken))
            {
                logger.Log("  [Epic Scraper] ERROR: CatalogItemId or AccessToken is null or empty. Cannot scrape.");
                return null;
            }

            try
            {
                var query = @"
                    query getCatalogItem($id: String!, $locale: String, $country: String!) {
                      Catalog {
                        catalogItem(id: $id, locale: $locale, country: $country) {
                          id
                          title
                          description
                          releaseDate
                          customAttributes {
                            key
                            value
                          }
                          keyImages {
                            type
                            url
                          }
                        }
                      }
                    }";

                var requestBody = new
                {
                    query,
                    variables = new { id = catalogItemId, locale = "en-US", country = "US" }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.Log($"  [Epic Scraper] ERROR: API request failed with status code {response.StatusCode}.");
                    logger.Log($"  [Epic Scraper] Response: {json}");
                    return null;
                }

                var gqlResponse = JsonConvert.DeserializeObject<GraphQLResponse>(json);
                var item = gqlResponse?.Data?.Catalog?.CatalogItem;

                if (item != null)
                {
                    var gameDetails = new GameDetails
                    {
                        Description = item.Description,
                        Developer = item.CustomAttributes?.FirstOrDefault(a => a.Key == "developerName")?.Value,
                        Publisher = item.CustomAttributes?.FirstOrDefault(a => a.Key == "publisherName")?.Value,
                        ReleaseDate = item.ReleaseDate,
                        MediaUrls = new Dictionary<string, string>()
                    };

                    var imageMappings = new Dictionary<string, string>
                    {
                        { "OfferImageWide", "fanart" },
                        { "OfferImageTall", "image" },
                        { "DieselGameBoxLogo", "marquee" },
                        { "DieselStoreFrontWide", "fanart" },
                        { "DieselStoreFrontTall", "image" }
                    };

                    foreach (var keyImage in item.KeyImages)
                    {
                        if (imageMappings.TryGetValue(keyImage.Type, out var mediaType))
                        {
                            if (!gameDetails.MediaUrls.ContainsKey(mediaType))
                            {
                                gameDetails.MediaUrls[mediaType] = keyImage.Url;
                            }
                        }
                    }

                    return gameDetails;
                }
                else
                {
                    logger.Log("  [Epic Scraper] ERROR: Failed to parse item from GraphQL response or item was null.");
                    logger.Log($"  [Epic Scraper] Raw JSON: {json}");
                }
            }
            catch (Exception ex)
            {
                logger.Log($"  [Epic Scraper] CRITICAL: An exception occurred: {ex.Message}");
                logger.Log($"  [Epic Scraper] Stack Trace: {ex.StackTrace}");
            }

            return null;
        }
    }
}
