using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GameStoreLibraryManager.Common;

namespace GameStoreLibraryManager.Epic
{
    public class EpicApi
    {
        private const string TokenUrl = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";
        private const string AssetsUrl = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/assets/Windows?label=Live";
        private const string CatalogUrl = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/{0}/bulk/items?id={1}&country=US&locale=en-US&includeMainGameDetails=true";
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string ClientSecret = "daafbccc737745039dffe53d94fc76cf";

        private static readonly HttpClient httpClient = new HttpClient();

        public EpicApi()
        {
        }

        private async Task<EpicToken> PostTokenRequest(Dictionary<string, string> body)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
                request.Headers.Add("Authorization", $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"))}");
                var postData = string.Join("&", body.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                request.Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<EpicToken>(responseString);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EPIC] PostTokenRequest failed: " + ex.Message);
                return null;
            }
            return null;
        }

        public async Task<EpicToken> AuthenticateWithAuthorizationCode(string authCode)
        {
            var body = new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", authCode }, { "token_type", "eg1" } };
            return await PostTokenRequest(body);
        }

        public async Task<EpicToken> AuthenticateWithRefreshToken(string refreshToken)
        {
            var body = new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", refreshToken }, { "token_type", "eg1" } };
            return await PostTokenRequest(body);
        }

        public async Task<List<EpicLibraryItem>> GetLibraryItems(string accessToken, string accountId)
        {
            var games = new List<EpicLibraryItem>();

            var assets = await GetAssets(accessToken);
            if (assets == null || assets.Count == 0)
            {
                Console.WriteLine("[EPIC] Found no assets on Epic account.");
                return games;
            }

            string cachePath = Path.Combine(PathManager.CachePath, "epic");
            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);

            var tasks = new List<Task>();
            var gameCollection = new System.Collections.Concurrent.ConcurrentBag<EpicLibraryItem>();

            foreach (var asset in assets)
            {
                tasks.Add(Task.Run(async () =>
                {
                    if (asset.@namespace == "ue") return;

                    var catalogItem = await GetCatalogItem(accessToken, asset.@namespace, asset.catalogItemId, cachePath);
                    if (catalogItem == null) return;

                    if (catalogItem.categories?.Any(a => a.path == "applications") != true) return;
                    if ((catalogItem.mainGameItem != null) && (catalogItem.categories?.Any(a => a.path == "addons/launchable") == false)) return;
                    if (catalogItem.categories?.Any(a => a.path == "digitalextras" || a.path == "plugins" || a.path == "plugins/engine") == true) return;

                    var newGame = new EpicLibraryItem
                    {
                        AppName = asset.appName,
                        CatalogItemId = asset.catalogItemId,
                        Namespace = asset.@namespace,
                        Metadata = new EpicGameMetadata
                        {
                            DisplayName = catalogItem.title
                        }
                    };
                    gameCollection.Add(newGame);
                }));
            }

            await Task.WhenAll(tasks);
            games.AddRange(gameCollection);

            return games;
        }

        private async Task<List<Asset>> GetAssets(string accessToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, AssetsUrl);
                request.Headers.Add("Authorization", $"bearer {accessToken}");
                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<Asset>>(responseString);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EPIC] GetAssets failed: " + ex.Message);
            }

            return new List<Asset>();
        }

        private async Task<CatalogItem> GetCatalogItem(string accessToken, string nameSpace, string id, string cachePath)
        {
            string cacheFile = null;
            if (!string.IsNullOrEmpty(cachePath))
            {
                cacheFile = Path.Combine(cachePath, $"{nameSpace}_{id}.json");
                if (File.Exists(cacheFile))
                {
                    try
                    {
                        var result = JsonConvert.DeserializeObject<Dictionary<string, CatalogItem>>(await File.ReadAllTextAsync(cacheFile));
                        if (result.TryGetValue(id, out var catalogItem))
                        {
                            return catalogItem;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EPIC] Failed to read cache file {cacheFile}: " + ex.Message);
                    }
                }
            }

            try
            {
                var url = string.Format(CatalogUrl, nameSpace, id);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"bearer {accessToken}");
                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(cacheFile))
                    {
                        await File.WriteAllTextAsync(cacheFile, json);
                    }

                    var result = JsonConvert.DeserializeObject<Dictionary<string, CatalogItem>>(json);
                    if (result.TryGetValue(id, out var catalogItem))
                    {
                        return catalogItem;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EPIC] GetCatalogItem for {id} failed: " + ex.Message);
            }

            return null;
        }
    }
}
