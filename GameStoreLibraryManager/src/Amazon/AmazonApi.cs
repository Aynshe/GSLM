using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using GameStoreLibraryManager.Common;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Net;
using GameStoreLibraryManager.Auth;

namespace GameStoreLibraryManager.Amazon
{
    public class AmazonApi
    {
        private const string LoginUrl = @"https://www.amazon.com/ap/signin?openid.ns=http://specs.openid.net/auth/2.0&openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select&openid.identity=http://specs.openid.net/auth/2.0/identifier_select&openid.mode=checkid_setup&openid.oa2.scope=device_auth_access&openid.ns.oa2=http://www.amazon.com/ap/ext/oauth/2&openid.oa2.response_type=code&openid.oa2.code_challenge_method=S256&openid.oa2.client_id=device:3733646238643238366332613932346432653737653161663637373636363435234132554d56484f58375550345637&language=en_US&marketPlaceId=ATVPDKIKX0DER&openid.return_to=https://www.amazon.com&openid.pape.max_auth_age=0&openid.assoc_handle=amzn_sonic_games_launcher&pageId=amzn_sonic_games_launcher&openid.oa2.code_challenge={0}";
        private const string RegisterUrl = @"https://api.amazon.com/auth/register";
        private const string EntitlementsUrl = @"https://gaming.amazon.com/api/distribution/entitlements";

        private readonly SimpleLogger _logger;
        private AmazonToken _token;
        private readonly HttpClient _client; // This client is now mostly unused, except for RefreshAsync if it's not using its own.

        private readonly Config _config; // Added this field earlier

        public AmazonApi(SimpleLogger logger, Config config) // Modified constructor earlier
        {
            _logger = logger;
            _config = config;
            _client = new HttpClient(); // Still instantiate, but its use is minimized
        }

        public async Task<bool> Authenticate()
        {
            var tokenPath = Path.Combine(PathManager.ApiKeyPath, "amazon.token");
            bool protect = _config.GetBoolean("enable_dpapi_protection", false);

            if (File.Exists(tokenPath))
            {
                var tokenJson = SecureStore.ReadString(tokenPath);
                _token = !string.IsNullOrWhiteSpace(tokenJson) ? JsonConvert.DeserializeObject<AmazonToken>(tokenJson) : null;
                // migrate plaintext to DPAPI if enabled
                if (protect && !SecureStore.IsProtectedFile(tokenPath) && _token != null)
                {
                    SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), true);
                }
            }

            if (_token != null && _token.IsExpired)
            {
                _logger.Log("[Amazon] Access token expired, attempting to refresh...");
                if (!await RefreshAsync())
                {
                    _logger.Log("[Amazon] Token refresh failed. Please re-authenticate.");
                    _token = null; // Invalidate the token
                    File.Delete(tokenPath);
                }
                else
                {
                    _logger.Log("[Amazon] Token refreshed successfully.");
                }
            }

            if (_token == null)
            {
                bool enableTokenGeneration = _config.GetBoolean("amazon_enable_token_generation", false);
                if (!enableTokenGeneration)
                {
                    _logger.Log("[Amazon] Authentication token not found. To generate one, set 'amazon_enable_token_generation = true' in the config file and restart.");
                    return false;
                }

                // Preferred: use embedded WebView2 UI to capture code/login URL
                string codeFile = Path.Combine(PathManager.ApiKeyPath, "amazon.code");
                string urlFile = Path.Combine(PathManager.ApiKeyPath, "amazon_login.url");
                string pkceFile = Path.Combine(PathManager.ApiKeyPath, "amazon.pkce");

                // Helper local function
                async Task<bool> TryExchangeFromFiles()
                {
                    try
                    {
                        string candidate = null;
                        if (File.Exists(codeFile))
                        {
                            candidate = File.ReadAllText(codeFile).Trim();
                        }
                        else if (File.Exists(urlFile))
                        {
                            candidate = File.ReadAllText(urlFile).Trim();
                        }

                        if (!string.IsNullOrEmpty(candidate))
                        {
                            string authCodeFromFiles = null;
                            try
                            {
                                var uri = new Uri(candidate, UriKind.RelativeOrAbsolute);
                                if (!uri.IsAbsoluteUri)
                                {
                                    // If file only contains the code value
                                    authCodeFromFiles = candidate;
                                }
                                else
                                {
                                    var query = HttpUtility.ParseQueryString(uri.Query);
                                    authCodeFromFiles = query["openid.oa2.authorization_code"] ?? query["code"];
                                }
                            }
                            catch
                            {
                                // Not a URL, treat as raw code
                                authCodeFromFiles = candidate;
                            }

                            if (!string.IsNullOrEmpty(authCodeFromFiles))
                            {
                                // Read PKCE verifier generated by the embedded UI launcher
                                string codeVerifierLocal = null;
                                try
                                {
                                    if (File.Exists(pkceFile))
                                    {
                                        codeVerifierLocal = File.ReadAllText(pkceFile).Trim();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Log($"[Amazon] Failed to read PKCE verifier file: {ex.Message}");
                                }

                                if (string.IsNullOrEmpty(codeVerifierLocal))
                                {
                                    _logger.Log("[Amazon] PKCE verifier not found. Please rerun the embedded auth UI.");
                                    return false;
                                }

                                _logger.Log("[Amazon] Authorization code detected from files, exchanging for token...");
                                var token = await GetTokenFromAuthCode(authCodeFromFiles, codeVerifierLocal);
                                if (token != null)
                                {
                                    _token = token;
                                    SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), protect);
                                    _logger.Log("[Amazon] Successfully authenticated and saved token.");
                                    try { if (File.Exists(codeFile)) File.Delete(codeFile); } catch { }
                                    try { if (File.Exists(urlFile)) File.Delete(urlFile); } catch { }
                                    try { if (File.Exists(pkceFile)) File.Delete(pkceFile); } catch { }
                                    return true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"[Amazon] Failed reading code/url files: {ex.Message}");
                    }
                    return false;
                }

                // 1) If files already exist (from previous run or manual), try them first
                if (!await TryExchangeFromFiles())
                {
                    // 2) Launch internal UI to capture code, then try again
                    try
                    {
                        _logger.Log("[Amazon] Opening embedded authentication window...");
                        AuthUiLauncher.Run("amazon");
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"[Amazon] Embedded auth UI failed to launch: {ex.Message}");
                    }

                    if (!await TryExchangeFromFiles())
                    {
                        // 3) Fallback to previous LocalAuthServer + system browser flow
                        const string redirectUri = "http://localhost:58673/";
                        string authCode = null;
                        var codeVerifier = GenerateCodeVerifier();
                        try
                        {
                            using (var server = new LocalAuthServer(redirectUri))
                            {
                                var codeChallenge = EncodeBase64Url(GetSHA256HashByte(codeVerifier));
                                var authUrl = LoginUrl.Replace("openid.return_to=https://www.amazon.com", $"openid.return_to={HttpUtility.UrlEncode(redirectUri)}");
                                var fullLoginUrl = string.Format(authUrl, codeChallenge);

                                _logger.Log("[Amazon] User interaction required for authentication (fallback).");
                                _logger.Log("[Amazon] Opening the login page in your default browser...");
                                OpenBrowser(fullLoginUrl);

                                var callbackParams = await server.ListenForCallbackAsync();
                                authCode = callbackParams?["openid.oa2.authorization_code"];
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Log($"[Amazon] Failed to complete automated authentication flow: {ex.Message}");
                            return false;
                        }

                        if (!string.IsNullOrEmpty(authCode))
                        {
                            _logger.Log("[Amazon] Authorization code received, exchanging for token...");
                            _token = await GetTokenFromAuthCode(authCode, codeVerifier);
                            if (_token != null)
                            {
                                SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), protect);
                                _logger.Log("[Amazon] Successfully authenticated and saved token.");
                                // Clean up old files if they exist
                                var codePath = Path.Combine(PathManager.ApiKeyPath, "amazon.code");
                                var loginUrlPath = Path.Combine(PathManager.ApiKeyPath, "amazon_login.url");
                                if (File.Exists(codePath)) File.Delete(codePath);
                                if (File.Exists(loginUrlPath)) File.Delete(loginUrlPath);
                            }
                        }
                        else
                        {
                            _logger.Log("[Amazon] Could not extract authorization code from the authentication callback.");
                        }
                    }
                }
            }

            return _token != null;
        }

        private async Task<AmazonToken> GetTokenFromAuthCode(string authCode, string codeVerifier)
        {
            var reqData = new DeviceRegistrationRequest
            {
                AuthData = new AuthData
                {
                    UseGlobalAuthentication = false,
                    AuthorizationCode = authCode,
                    CodeVerifier = codeVerifier,
                    CodeAlgorithm = "SHA-256", // Corrected this earlier
                    ClientId = "3733646238643238366332613932346432653737653161663637373636363435234132554d56484f58375550345637",
                    ClientDomain = "DeviceLegacy"
                },
                RegistrationData = new RegistrationData
                {
                    AppName = "AGSLauncher for Windows",
                    AppVersion = "1.0.0",
                    DeviceModel = "Windows",
                    DeviceSerial = GetMachineGuid().ToString("N"),
                    DeviceType = "A2UMVHOX7UP4V7",
                    Domain = "Device",
                    OsVersion = Environment.OSVersion.Version.ToString(4)
                },
                RequestedExtensions = new List<string> { "customer_info", "device_info" },
                RequestedTokenType = new List<string> { "bearer", "mac_dms" }
            };

            var requestJson = JsonConvert.SerializeObject(reqData);
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var req = new HttpRequestMessage(HttpMethod.Post, RegisterUrl)
            {
                Content = requestContent
            };
            req.Headers.Add("User-Agent", "AGSLauncher/1.0.0");

            // Use a fresh HttpClient for this request to avoid carrying over any state
            using var client = new HttpClient(); // This was the key change for this method
            var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.Log($"[Amazon] Token exchange failed {res.StatusCode}: {body}");
                return null;
            }

            var responseData = JsonConvert.DeserializeObject<DeviceRegistrationResponse>(body);
            var bearerToken = responseData?.Response?.Success?.Tokens?.Bearer;

            if (bearerToken == null)
            {
                _logger.Log($"[Amazon] Token exchange failed: Could not parse bearer token from response body: {body}");
                return null;
            }

            return new AmazonToken
            {
                AccessToken = bearerToken.AccessToken,
                RefreshToken = bearerToken.RefreshToken,
                ExpiresIn = bearerToken.ExpiresIn,
                CreatedAt = DateTime.Now
            };
        }

        public async Task<List<Entitlement>> GetAccountEntitlements()
        {
            var entitlements = new List<Entitlement>();
            string nextToken = null;
            var triedRefresh = false; // This variable is still here, but its logic is simplified

            // Simplified token validation and refresh logic
            // ValidateTokenAsync uses its own HttpClient
            if (!await ValidateTokenAsync())
            {
                _logger.Log("[Amazon] Token invalid, attempting refresh...");
                // RefreshAsync uses its own HttpClient
                if (!await RefreshAsync())
                {
                    _logger.Log("[Amazon] Token refresh failed, re-authenticating...");
                    // Force re-auth by deleting token and calling Authenticate
                    var tokenPath = Path.Combine(PathManager.ApiKeyPath, "amazon.token");
                    if(File.Exists(tokenPath)) File.Delete(tokenPath);
                    _token = null;
                    // Authenticate uses the manual flow
                    if (!await Authenticate())
                    {
                        _logger.Log("[Amazon] Re-authentication failed. Aborting.");
                        return entitlements;
                    }
                }
            }

            // Log key JWT claims to verify token trust context (non-sensitive)
            try
            {
                if (!string.IsNullOrEmpty(_token?.AccessToken))
                {
                    var parts = _token.AccessToken.Split('.');
                    if (parts.Length >= 2)
                    {
                        string payload = parts[1];
                        payload = payload.Replace('-', '+').Replace('_', '/');
                        switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
                        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                        dynamic obj = JsonConvert.DeserializeObject(json);
                        _logger.Log($"[Amazon] Token claims: iss={{obj?.iss}}, aud={{obj?.aud}}");
                    }
                }
            }
            catch { }

            // Use a new HttpClient instance for the entitlements call to match Playnite's behavior
            // and avoid any potential state contamination from other API calls.
            using (var client = new HttpClient()) // Key change for this method
            {
                client.DefaultRequestHeaders.Add("x-amzn-token", _token.AccessToken);

                do
                {
                    var reqData = new EntitlementsRequest
                    {
                        Operation = "GetEntitlements",
                        ClientId = "Sonic",
                        SyncPoint = 0,
                        MaxResults = 500,
                        KeyId = "d5dc8b8b-86c8-4fc4-ae93-18c0def5314d",
                        HardwareHash = Guid.NewGuid().ToString("N"),
                        DisableStateFilter = true,
                        NextToken = nextToken
                    };

                    var payloadJson = JsonConvert.SerializeObject(reqData, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    var requestContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                    requestContent.Headers.TryAddWithoutValidation("Content-Encoding", "amz-1.0"); // Re-added this earlier

                    var request = new HttpRequestMessage(HttpMethod.Post, EntitlementsUrl) { Content = requestContent };
                    request.Headers.Add("User-Agent", "com.amazon.agslauncher.win/3.0.9495.3"); // Reverted this earlier
                    request.Headers.Add("X-Amz-Target", "com.amazon.animusdistributionservice.entitlement.AnimusEntitlementsService.GetEntitlements");
                    request.Headers.TryAddWithoutValidation("Expect", "100-continue");


                    var response = await client.SendAsync(request); // Use the local client
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var entlsData = JsonConvert.DeserializeObject<EntitlementsResponse>(responseString);
                        nextToken = entlsData?.NextToken;
                        if (entlsData?.Entitlements != null)
                        {
                            _logger.Log($"[Amazon] Received {entlsData.Entitlements.Count} entitlements.");
                            entitlements.AddRange(entlsData.Entitlements);
                        }
                        else
                        {
                            _logger.Log("[Amazon] Entitlements property was null in the response.");
                        }
                        _logger.Log($"[Amazon] NextToken: {(string.IsNullOrEmpty(nextToken) ? "null or empty" : "present")}");
                    }
                    else
                    {
                        _logger.Log($"[Amazon] Entitlements call failed: {response.StatusCode}");
                        _logger.Log($"[Amazon] Response: {responseString}");

                        if (response.StatusCode == HttpStatusCode.BadRequest && !triedRefresh)
                        {
                            _logger.Log("[Amazon] Retrying with a refreshed token once.");
                            triedRefresh = true;
                            if (await RefreshAsync())
                            {
                                // Remove old token and add new one for the next loop iteration.
                                client.DefaultRequestHeaders.Remove("x-amzn-token");
                                client.DefaultRequestHeaders.Add("x-amzn-token", _token.AccessToken);
                                continue; // Retry the do-while loop
                            }
                        }
                        break; // Exit do-while loop on other errors
                    }
                } while (!string.IsNullOrEmpty(nextToken));
            }

            return entitlements;
        }

        private async Task<bool> ValidateTokenAsync()
        {
            if (_token == null) return false;
            try
            {
                using var client = new HttpClient();
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.amazon.com/user/profile");
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_token.AccessToken}");
                var res = await client.SendAsync(req);
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Amazon] Error validating token: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RefreshAsync()
        {
            if (_token?.RefreshToken == null) return false;

            var payload = new
            {
                app_name = "AGSLauncher",
                app_version = "3.0.9495.3",
                source_token = _token.RefreshToken,
                requested_token_type = "access_token",
                source_token_type = "refresh_token"
            };

            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.amazon.com/auth/token");
            req.Headers.TryAddWithoutValidation("User-Agent", "AGSLauncher/1.0.0");
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) { _logger.Log($"[Amazon] Refresh failed {res.StatusCode}: {body}"); return false; }

            dynamic r = JsonConvert.DeserializeObject(body);
            var newAccess = (string)r?.access_token;
            if (string.IsNullOrEmpty(newAccess)) return false;

            _token.AccessToken = newAccess;
            _token.CreatedAt = DateTime.Now; // Reset the creation time

            try
            {
                var tokenPath = Path.Combine(PathManager.ApiKeyPath, "amazon.token");
                bool protect = _config.GetBoolean("enable_dpapi_protection", false);
                SecureStore.WriteString(tokenPath, JsonConvert.SerializeObject(_token), protect);
            }
            catch (Exception ex)
            {
                _logger.Log($"[Amazon] Failed to save refreshed token: {ex.Message}");
            }

            return true;
        }

        private string EncodeBase64Url(byte[] input) => Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private byte[] GetSHA256HashByte(string input) => SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(input));
        private string GenerateCodeVerifier()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-~";
            var length = 128;
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[length];
                rng.GetBytes(bytes);
                var result = new StringBuilder(length);
                foreach (var b in bytes)
                {
                    result.Append(chars[b % chars.Length]);
                }
                return result.ToString();
            }
        }

        private void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // This is a workaround for .NET Core issue https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        public static Guid GetMachineGuid()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Guid.NewGuid();
            }

            RegistryKey root = Registry.LocalMachine;
            try
            {
                using (var cryptography = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    var machineGuid = cryptography?.GetValue("MachineGuid") as string;
                    if (!string.IsNullOrEmpty(machineGuid))
                    {
                        return Guid.Parse(machineGuid);
                    }
                }
            }
            catch (Exception)
            {
                // Log the exception? For now, we'll fall back.
            }

            return Guid.NewGuid();
        }
    }
}
