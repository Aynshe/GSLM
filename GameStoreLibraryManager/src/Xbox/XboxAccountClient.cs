using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using GameStoreLibraryManager.Auth;
using GameStoreLibraryManager.Common;
using GameStoreLibraryManager.Xbox.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace GameStoreLibraryManager.Xbox
{
    public class XboxAccountClient
    {
        public const string ClientId = "1f40bbde-7a93-4314-bb8f-91d0ab2f07cc";
        private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";
        private const string Scope = "Xboxlive.signin Xboxlive.offline_access";

        private readonly string _liveTokensPath;
        private readonly string _xstsLoginTokensPath;
        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly HttpClient _httpClient;

        public XboxAccountClient(Config config, SimpleLogger logger)
        {
            _config = config;
            _logger = logger;
            _liveTokensPath = Path.Combine(PathManager.ApiKeyPath, "xbox_live.json");
            _xstsLoginTokensPath = Path.Combine(PathManager.ApiKeyPath, "xbox_xsts.json");
            _httpClient = new HttpClient();
        }

        public async Task Login()
        {
            if (File.Exists(_liveTokensPath)) File.Delete(_liveTokensPath);
            if (File.Exists(_xstsLoginTokensPath)) File.Delete(_xstsLoginTokensPath);


            var codePath = Path.Combine(PathManager.ApiKeyPath, "xbox.code");
            if (!File.Exists(codePath))
            {
                var errorMessage = "[Xbox] Authentication failed: Authorization code not found after UI completion.";
                _logger.Log(errorMessage);
                throw new Exception(errorMessage);
            }

            var authorizationCode = File.ReadAllText(codePath).Trim();
            File.Delete(codePath);

            _logger.Log("[Xbox] Authorization code loaded. Requesting OAuth token...");
            var tokenResponse = await RequestOAuthToken(authorizationCode);
            _logger.Log("[Xbox] OAuth token received.");
            var liveLoginData = new AuthenticationData
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresIn = tokenResponse.ExpiresIn,
                CreationDate = DateTime.Now
            };

            _logger.Log($"[Xbox] Saving Live token to {_liveTokensPath}");
            SecureStore.WriteString(_liveTokensPath, JsonConvert.SerializeObject(liveLoginData), _config.GetBoolean("enable_dpapi_protection", false));
            _logger.Log("[Xbox] Live token saved. Proceeding to XSTS authentication.");
            await Authenticate(liveLoginData.AccessToken);
            _logger.Log("[Xbox] XSTS authentication complete. Login process finished.");

            _logger.Log("[Xbox] Verifying token file creation...");
            if (!File.Exists(_liveTokensPath) || !File.Exists(_xstsLoginTokensPath))
            {
                string missingFiles = "";
                if (!File.Exists(_liveTokensPath)) missingFiles += " xbox_live.json";
                if (!File.Exists(_xstsLoginTokensPath)) missingFiles += " xbox_xsts.json";

                var errorMessage = $"[Xbox] CRITICAL: Login process completed but token files were not created. Missing:{missingFiles.Trim()}";
                _logger.Log(errorMessage);
                throw new Exception(errorMessage);
            }
            _logger.Log("[Xbox] Token files verified successfully.");
        }

        private async Task<RefreshTokenResponse> RequestOAuthToken(string authorizationCode)
        {
            var requestData = HttpUtility.ParseQueryString(string.Empty);
            requestData.Add("grant_type", "authorization_code");
            requestData.Add("code", authorizationCode);
            return await ExecuteTokenRequest(requestData);
        }

        private async Task<RefreshTokenResponse> ExecuteTokenRequest(System.Collections.Specialized.NameValueCollection requestData)
        {
            requestData.Add("scope", Scope);
            requestData.Add("client_id", ClientId);
            requestData.Add("redirect_uri", RedirectUri);
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(
                    "https://login.live.com/oauth20_token.srf",
                    new StringContent(requestData.ToString(), Encoding.ASCII, "application/x-www-form-urlencoded"));

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Log($"[Xbox] Token request failed. Status: {response.StatusCode}, Body: {errorContent}");
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<RefreshTokenResponse>(content);
            }
        }

        private async Task Authenticate(string accessToken)
        {
            using (var client = new HttpClient())
            {
                var jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include };

                var authRequestData = new AuthenticationRequest
                {
                    Properties = new AuthenticationRequest.AuthRequestProperties { RpsTicket = $"d={accessToken}" }
                };
                var authPostContent = JsonConvert.SerializeObject(authRequestData, jsonSettings);

                _logger.Log("[Xbox] Authenticating with user.auth.xboxlive.com...");
                var authResponse = await client.PostAsync(@"https://user.auth.xboxlive.com/user/authenticate", new StringContent(authPostContent, Encoding.UTF8, "application/json"));
                if (!authResponse.IsSuccessStatusCode)
                {
                    var errorContent = await authResponse.Content.ReadAsStringAsync();
                    _logger.Log($"[Xbox] user.auth.xboxlive.com authentication failed. Status: {authResponse.StatusCode}, Body: {errorContent}");
                    authResponse.EnsureSuccessStatusCode();
                }

                var authResponseContent = await authResponse.Content.ReadAsStringAsync();
                var authTokens = JsonConvert.DeserializeObject<AuthorizationData>(authResponseContent);

                if (string.IsNullOrEmpty(authTokens.Token))
                {
                   _logger.Log("[Xbox] Critical error: Intermediate token from user.auth.xboxlive.com is null or empty.");
                   throw new Exception("Failed to get intermediate token.");
                }

                var atrzRequestData = new XSTSAuthorizationRequest
                {
                    Properties = new XSTSProperties
                    {
                        UserTokens = new List<string> { authTokens.Token }
                    }
                };
                var atrzPostContent = JsonConvert.SerializeObject(atrzRequestData, jsonSettings);

                _logger.Log($"[Xbox] Authorizing with xsts.auth.xboxlive.com...");
                var atrzResponse = await client.PostAsync(@"https://xsts.auth.xboxlive.com/xsts/authorize", new StringContent(atrzPostContent, Encoding.UTF8, "application/json"));
                if (!atrzResponse.IsSuccessStatusCode)
                {
                    var errorContent = await atrzResponse.Content.ReadAsStringAsync();
                    _logger.Log($"[Xbox] xsts.auth.xboxlive.com authorization failed. Status: {atrzResponse.StatusCode}, Body: {errorContent}");
                    atrzResponse.EnsureSuccessStatusCode();
                }

                var atrzResponseContent = await atrzResponse.Content.ReadAsStringAsync();
                SecureStore.WriteString(_xstsLoginTokensPath, atrzResponseContent, _config.GetBoolean("enable_dpapi_protection", false));
            }
        }

        private async Task<AuthenticationData> RefreshAndGetLiveTokens()
        {
            var tokensJson = SecureStore.ReadString(_liveTokensPath);
            if (string.IsNullOrEmpty(tokensJson))
            {
                throw new Exception("Not logged in.");
            }

            var tokens = JsonConvert.DeserializeObject<AuthenticationData>(tokensJson);

            if (tokens.IsExpired)
            {
                _logger.Log("[Xbox] Live token is expired, refreshing...");
                var response = await RefreshOAuthToken(tokens.RefreshToken);
                tokens.AccessToken = response.AccessToken;
                tokens.RefreshToken = response.RefreshToken;
                tokens.ExpiresIn = response.ExpiresIn;
                tokens.CreationDate = DateTime.Now;
                SecureStore.WriteString(_liveTokensPath, JsonConvert.SerializeObject(tokens), _config.GetBoolean("enable_dpapi_protection", false));
                _logger.Log("[Xbox] Live token refreshed.");
            }

            return tokens;
        }

        private async Task<AuthorizationData> RefreshAndGetXstsTokens()
        {
            if (!File.Exists(_xstsLoginTokensPath))
            {
                throw new Exception("Not logged in.");
            }

            var tokens = GetSavedXstsTokens();
            if (tokens == null || tokens.NotAfter < DateTime.UtcNow)
            {
                _logger.Log("[Xbox] XSTS token is missing or expired, attempting refresh.");
                var liveTokens = await RefreshAndGetLiveTokens();
                await Authenticate(liveTokens.AccessToken);

                tokens = GetSavedXstsTokens();
                if (tokens == null || tokens.NotAfter < DateTime.UtcNow)
                {
                    throw new Exception("Still not authenticated after token refresh.");
                }
            }
            return tokens;
        }

        private async Task<RefreshTokenResponse> RefreshOAuthToken(string refreshToken)
        {
            var requestData = HttpUtility.ParseQueryString(string.Empty);
            requestData.Add("grant_type", "refresh_token");
            requestData.Add("refresh_token", refreshToken);
            return await ExecuteTokenRequest(requestData);
        }

        public async Task<List<Title>> GetLibraryTitlesAsync()
        {
            var tokens = await RefreshAndGetXstsTokens();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
                client.DefaultRequestHeaders.Add("Authorization", $"XBL3.0 x={tokens.DisplayClaims.Xui[0].Userhash};{tokens.Token}");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US");

                var url = $"https://titlehub.xboxlive.com/users/xuid({tokens.DisplayClaims.Xui[0].XboxUserId})/titles/titlehistory/decoration/detail";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Log($"[Xbox] Title history request failed. Status: {response.StatusCode}, Body: {errorContent}");
                    response.EnsureSuccessStatusCode();
                }

                var responseData = JsonConvert.DeserializeObject<TitleHistoryResponse>(await response.Content.ReadAsStringAsync());
                return responseData?.titles ?? new List<Title>();
            }
        }

        public async Task<Title> GetTitleInfoAsync(string pfn)
        {
            var tokens = await RefreshAndGetXstsTokens();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-xbl-contract-version", "2");
                client.DefaultRequestHeaders.Add("Authorization", $"XBL3.0 x={tokens.DisplayClaims.Xui[0].Userhash};{tokens.Token}");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US");

                var postData = new { pfns = new[] { pfn }, windowsPhoneProductIds = new string[] { } };
                var content = new StringContent(JsonConvert.SerializeObject(postData), Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://titlehub.xboxlive.com/titles/batch/decoration/detail", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseData = JsonConvert.DeserializeObject<TitleHistoryResponse>(await response.Content.ReadAsStringAsync());
                    return responseData?.titles?.FirstOrDefault();
                }
                return null;
            }
        }

        private AuthorizationData GetSavedXstsTokens()
        {
            try
            {
                var content = SecureStore.ReadString(_xstsLoginTokensPath);
                if (string.IsNullOrEmpty(content)) return null;
                return JsonConvert.DeserializeObject<AuthorizationData>(content);
            }
            catch (Exception e)
            {
                _logger.Log($"[Xbox] Failed to load saved XSTS tokens: {e.Message}");
                return null;
            }
        }

        public async Task<List<GamePassCatalogProduct>> GetGamePassCatalogAsync(string catalogId, string region, string language = "en-us")
        {
            var url = $"https://catalog.gamepass.com/sigls/v2?id={catalogId}&language={language}&market={region}";

            using (var client = new HttpClient())
            {
                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var catalog = JsonConvert.DeserializeObject<List<GamePassCatalogProduct>>(content);
                        _logger.Log($"[Xbox] Retrieved {catalog?.Count ?? 0} games from Game Pass catalog (region: {region}).");
                        return catalog ?? new List<GamePassCatalogProduct>();
                    }
                    else
                    {
                        _logger.Log($"[Xbox] Game Pass catalog request failed. Status: {response.StatusCode}");
                        return new List<GamePassCatalogProduct>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Xbox] Error fetching Game Pass catalog: {ex.Message}");
                    return new List<GamePassCatalogProduct>();
                }
            }
        }

        public async Task<Dictionary<string, string>> GetGameTitlesAsync(string[] productIds, string market, string language)
        {
            var result = new Dictionary<string, string>();
            const int batchSize = 10; // Taille des lots pour éviter les URLs trop longues

            if (productIds == null || productIds.Length == 0)
            {
                return result;
            }

            for (int i = 0; i < productIds.Length; i += batchSize)
            {
                try
                {
                    var batch = productIds.Skip(i).Take(batchSize).ToArray();
                    var ids = string.Join(",", batch);
                    var url = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={ids}&market={market}&languages={language}";

                    _logger.Log($"[Xbox] Récupération des noms de jeux {i+1}-{Math.Min(i+batchSize, productIds.Length)}/{productIds.Length}...");

                    var response = await _httpClient.GetStringAsync(url);
                    var catalog = JsonConvert.DeserializeObject<DisplayCatalogResponse>(response);

                    if (catalog?.Products != null)
                    {
                        foreach (var product in catalog.Products)
                        {
                            if (product == null) continue;

                            var title = product.GetTitle();
                            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(product.ProductId))
                            {
                                result[product.ProductId] = title;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Xbox] Erreur lors de la récupération d'un lot de noms: {ex.Message}");
                }
            }

            _logger.Log($"[Xbox] {result.Count} noms de jeux récupérés sur {productIds.Length} demandés");
            return result;
        }

        public async Task<Dictionary<string, Product>> GetProductDetailsAsync(string[] productIds, string market, string language)
        {
            var result = new Dictionary<string, Product>();
            const int batchSize = 10; // Batch size to avoid overly long URLs

            if (productIds == null || !productIds.Any())
            {
                return result;
            }

            for (int i = 0; i < productIds.Length; i += batchSize)
            {
                try
                {
                    var batch = productIds.Skip(i).Take(batchSize).ToArray();
                    var ids = string.Join(",", batch);
                    var url = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={ids}&market={market}&languages={language}";

                    _logger.Log($"[Xbox] Fetching product details {i + 1}-{Math.Min(i + batchSize, productIds.Length)}/{productIds.Length}...");

                    var response = await _httpClient.GetStringAsync(url);
                    var catalog = JsonConvert.DeserializeObject<DisplayCatalogResponse>(response);

                    if (catalog?.Products != null)
                    {
                        foreach (var product in catalog.Products)
                        {
                            if (product != null && !string.IsNullOrEmpty(product.ProductId))
                            {
                                result[product.ProductId] = product;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Xbox] Error fetching a batch of product details: {ex.Message}");
                }
            }

            _logger.Log($"[Xbox] {result.Count} product details retrieved out of {productIds.Length} requested.");
            return result;
        }

        public async Task<JObject> GetGamePassCatalogV1Async(string region, string language)
        {
            var url = $"https://catalog.gamepass.microsoft.com/v1.0/{region}/products";
            _logger.Log($"[Xbox] Fetching Game Pass V1 catalog from: {url}");

            using (var client = new HttpClient())
            {
                try
                {
                    var response = await client.GetStringAsync(url);
                    return JObject.Parse(response);
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Xbox] Error fetching Game Pass V1 catalog: {ex.Message}");
                    return null;
                }
            }
        }
    }
}