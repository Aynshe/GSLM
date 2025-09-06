using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using GameStoreLibraryManager.Common;

namespace GameStoreLibraryManager.Gog
{
    public class GogApi
    {
        private const string TokenUrl = "https://auth.gog.com/token";
        private const string OwnedGamesUrl = "https://embed.gog.com/user/data/games";
        private const string GameDetailsUrl = "https://embed.gog.com/account/gameDetails/{0}.json";
        private const string ClientId = "46899977096215655";
        private const string ClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
        private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";

        private readonly HttpClient _client;
        private readonly SimpleLogger _logger;
        private GogToken _token;

        public GogApi(SimpleLogger logger)
        {
            _logger = logger;
            _client = new HttpClient();
        }

        public static string GetAuthenticationUrl()
        {
            return $"https://login.gog.com/auth?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&layout=galaxy&brand=gog";
        }

        public async Task<bool> Authenticate(bool isInteractive)
        {
            var tokenPath = Path.Combine(PathManager.ApiKeyPath, "gog.token");
            bool protect = false;
            try { protect = new Config().GetBoolean("enable_dpapi_protection", false); } catch { }
            var codePath = Path.Combine(PathManager.ApiKeyPath, "gog.code");

            if (File.Exists(codePath))
            {
                try
                {
                    var content = File.ReadAllText(codePath).Trim();
                    string code = null;
                    try
                    {
                        var uri = new Uri(content);
                        var queryParams = HttpUtility.ParseQueryString(uri.Query);
                        code = queryParams["code"];
                    }
                    catch
                    {
                        // Not a URL, assume it's the raw code
                        code = content;
                    }

                    if (!string.IsNullOrEmpty(code))
                    {
                        var token = await GetTokenFromAuthCode(code);
                        if (token != null)
                        {
                            _token = token;
                            SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), protect);
                            _logger.Log("[GOG] Successfully authenticated using authorization code and saved token.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[GOG] Failed to process gog.code file: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(codePath); } catch { }
                }
            }

            if (_token == null && File.Exists(tokenPath))
            {
                var tokenJson = SecureStore.ReadString(tokenPath);
                _token = JsonConvert.DeserializeObject<GogToken>(tokenJson);
                // Migrate plaintext to DPAPI if enabled
                if (_token != null && protect && !SecureStore.IsProtectedFile(tokenPath))
                {
                    try { SecureStore.WriteString(tokenPath, tokenJson, true); } catch { }
                }
            }

            if (_token != null && _token.IsExpired)
            {
                _logger.Log("[GOG] Access token expired, refreshing...");
                var newToken = await GetTokenFromRefreshToken(_token.RefreshToken);
                if (newToken != null)
                {
                    _token = newToken;
                    SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), protect);
                    _logger.Log("[GOG] Token refreshed and saved successfully.");
                }
                else
                {
                    _token = null;
                }
            }

            if (_token != null)
            {
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
            }
            else
            {
                if (isInteractive)
                {
                    _logger.Log("[GOG] GOG authentication required.");
                    _logger.Log($"1. Create a file named 'gog.code' in the '{PathManager.ApiKeyPath}' directory.");
                    _logger.Log("2. Open the following URL in your browser, log in if necessary:");
                    _logger.Log($"   {GetAuthenticationUrl()}");
                    _logger.Log("3. After logging in, you will be redirected. Copy the ENTIRE URL from your browser's address bar.");
                    _logger.Log("4. Paste this full URL into the 'gog.code' file and save it.");
                    _logger.Log("5. Rerun this application.");
                }
                return false;
            }

            return true;
        }

        private async Task<GogToken> GetTokenFromAuthCode(string authCode)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", authCode },
                { "redirect_uri", RedirectUri },
                { "client_id", ClientId },
                { "client_secret", ClientSecret }
            };
            return await PostTokenRequest(body);
        }

        private async Task<GogToken> GetTokenFromRefreshToken(string refreshToken)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "client_id", ClientId },
                { "client_secret", ClientSecret }
            };
            return await PostTokenRequest(body);
        }

        private async Task<GogToken> PostTokenRequest(Dictionary<string, string> body)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
                var postData = string.Join("&", body.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                request.Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

                var response = await new HttpClient().SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<GogToken>(responseString);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Log($"[GOG] Token request failed with status {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.Log("[GOG] PostTokenRequest failed: " + ex.Message);
            }
            return null;
        }

        public async Task<List<long>> GetOwnedGameIdsAsync()
        {
            try
            {
                var response = await _client.GetAsync(OwnedGamesUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Log($"[GOG] Error fetching owned game IDs. Status: {response.StatusCode}. Response: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                    return new List<long>();
                }
                var responseString = await response.Content.ReadAsStringAsync();
                var ownedIdsResponse = JsonConvert.DeserializeObject<GogOwnedIdsResponse>(responseString);
                return ownedIdsResponse?.Owned ?? new List<long>();
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG] Error fetching owned game IDs: {ex.Message}");
                return new List<long>();
            }
        }

        public async Task<GogGameDetails> GetGameDetailsAsync(long gameId)
        {
            try
            {
                var url = string.Format(GameDetailsUrl, gameId);
                var response = await _client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    if (responseString.Trim().StartsWith("["))
                    {
                        _logger.Log($"[GOG] Game details for ID {gameId} returned an empty array, skipping.");
                        return null;
                    }
                    return JsonConvert.DeserializeObject<GogGameDetails>(responseString);
                }
                else
                {
                    _logger.Log($"[GOG] Error fetching game details for ID {gameId}. Status: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[GOG] Error fetching game details for ID {gameId}: {ex.Message}");
                return null;
            }
        }
    }
}
