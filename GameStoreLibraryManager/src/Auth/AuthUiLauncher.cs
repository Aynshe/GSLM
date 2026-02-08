using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using GameStoreLibraryManager.Common;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using GameStoreLibraryManager.Xbox;

namespace GameStoreLibraryManager.Auth
{
    public static class AuthUiLauncher
    {
        public static int Run(string store)
        {
            // Run WinForms on STA thread from console app
            int exitCode = 0;
            var t = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var (form, onCode) = BuildFormForStore(store);
                    if (form == null)
                    {
                        MessageBox.Show($"Unknown store: {store}", "Auth UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        exitCode = 2;
                        return;
                    }

                    form.CodeDetected += urlOrValue =>
                    {
                        try
                        {
                            onCode?.Invoke(urlOrValue);
                            form.Invoke(new Action(() => form.Close()));
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to save code: {ex.Message}", "Auth UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    };

                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Auth UI fatal error: {ex.Message}", "Auth UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    exitCode = 1;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return exitCode;
        }

        private static (AuthBrowserForm form, Action<string> onCode) BuildFormForStore(string store)
        {
            store = store?.Trim().ToLowerInvariant();
            switch (store)
            {
                case "steam":
                    {
                        var apiUrl = "https://steamcommunity.com/dev/apikey";
                        // Auto-extract a 32-char key from page content
                        var contentRegex = new Regex(@"\b([0-9A-Fa-f]{32})\b", RegexOptions.IgnoreCase);
                        var form = new AuthBrowserForm("Steam", apiUrl, codeRegex: null, steamPasteMode: false, contentRegex: contentRegex);
                        return (form, val =>
                        {
                            var target = Path.Combine(PathManager.ApiKeyPath, "steam.apikey");
                            Directory.CreateDirectory(PathManager.ApiKeyPath);
                            File.WriteAllText(target, val.Trim());
                        });
                    }
                case "amazon":
                    {
                        // Generate PKCE verifier/challenge and persist verifier for later token exchange
                        var verifier = GenerateCodeVerifier();
                        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
                        Directory.CreateDirectory(PathManager.ApiKeyPath);
                        File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "amazon.pkce"), verifier);

                        // Detect redirect URL containing authorization code
                        var codeRegex = new Regex(@"openid\.oa2\.authorization_code=([^&]+)", RegexOptions.IgnoreCase);
                        var form = new AuthBrowserForm("Amazon", BuildAmazonLoginUrl(challenge), codeRegex);
                        return (form, url =>
                        {
                            var match = codeRegex.Match(url);
                            var code = match.Success ? match.Groups[1].Value : null;
                            if (!string.IsNullOrEmpty(code))
                            {
                                File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "amazon.code"), code);
                            }
                            File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "amazon_login.url"), url);
                        });
                    }
                case "gog":
                    {
                        // GOG OAuth implicit/page-based flows often include 'code=' in redirect
                        var start = "https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client";
                        var codeRegex = new Regex(@"[?&]code=([^&]+)", RegexOptions.IgnoreCase);
                        var form = new AuthBrowserForm("GOG", start, codeRegex);
                        return (form, url =>
                        {
                            var match = codeRegex.Match(url);
                            var code = match.Success ? match.Groups[1].Value : null;
                            if (!string.IsNullOrEmpty(code))
                            {
                                File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "gog.code"), code);
                            }
                        });
                    }
                case "epic":
                    {
                        // Open Epic login first; after login, auto-navigate to redirect to capture ?code=
                        var loginUrl = "https://www.epicgames.com/id/login";
                        var redirectUrl = "https://www.epicgames.com/id/api/redirect?clientId=34a02cf8f4414e29b15921876da36f9a&responseType=code";
                        var codeRegex = new Regex(@"[?&]code=([^&]+)", RegexOptions.IgnoreCase);
                        // Also scan page content for a JSON blob containing authorizationCode or a redirectUrl with ?code=
                        var contentRegex = new Regex(@"(?:\""(?:authorizationCode|exchangeCode)\""\s*:\s*\""([^\""]+)\"")|(?:code=([A-Za-z0-9_\-]+))", RegexOptions.IgnoreCase);
                        var form = new AuthBrowserForm("Epic Games", loginUrl, codeRegex, steamPasteMode: false, contentRegex: contentRegex, postLoginRedirectUrl: redirectUrl);
                        return (form, val =>
                        {
                            // val may be the full URL OR the raw code (from content extraction)
                            string code = null;
                            var m = codeRegex.Match(val);
                            if (m.Success)
                            {
                                code = m.Groups[1].Value;
                            }
                            if (string.IsNullOrEmpty(code))
                            {
                                // Treat as raw code
                                code = val?.Trim();
                            }

                            Directory.CreateDirectory(PathManager.ApiKeyPath);
                            if (!string.IsNullOrEmpty(code))
                            {
                                File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "epic.code"), code);
                            }
                        });
                    }
                case "xbox":
                    {
                        var query = HttpUtility.ParseQueryString(string.Empty);
                        query.Add("client_id", XboxAccountClient.ClientId);
                        query.Add("response_type", "code");
                        query.Add("approval_prompt", "auto");
                        query.Add("scope", "Xboxlive.signin Xboxlive.offline_access");
                        query.Add("redirect_uri", "https://login.live.com/oauth20_desktop.srf");

                        var loginUrl = @"https://login.live.com/oauth20_authorize.srf?" + query.ToString();
                        var codeRegex = new Regex(@"[?&]code=([^&]+)", RegexOptions.IgnoreCase);

                        var form = new AuthBrowserForm("Xbox / Microsoft", loginUrl, codeRegex);

                        return (form, url =>
                        {
                            var match = codeRegex.Match(url);
                            var code = match.Success ? match.Groups[1].Value : null;
                            if (!string.IsNullOrEmpty(code))
                            {
                                Directory.CreateDirectory(PathManager.ApiKeyPath);
                                File.WriteAllText(Path.Combine(PathManager.ApiKeyPath, "xbox.code"), code);
                            }
                        });
                    }
                case "steamgriddb_profile":
                    {
                        var profileApiUrl = "https://www.steamgriddb.com/profile/api";
                        var form = new AuthBrowserForm("SteamGridDB Profile", profileApiUrl, codeRegex: null, steamPasteMode: false, contentRegex: null);
                        return (form, null); // No action on code detection
                    }
                case "steamgriddb":
                    {
                        var loginUrl = "https://www.steamgriddb.com/login";
                        var profileApiUrl = "https://www.steamgriddb.com/profile/api";
                        // SteamGridDB API keys are typically 32-char hex strings
                        var contentRegex = new Regex(@"\b([0-9a-f]{32})\b", RegexOptions.IgnoreCase);
                        var form = new AuthBrowserForm("SteamGridDB", loginUrl, codeRegex: null, steamPasteMode: false, contentRegex: contentRegex, postLoginRedirectUrl: profileApiUrl);
                        return (form, val =>
                        {
                            var target = Path.Combine(PathManager.ApiKeyPath, "steamgriddb.apikey");
                            Directory.CreateDirectory(PathManager.ApiKeyPath);
                            File.WriteAllText(target, val.Trim());
                        });
                    }
                default:
                    return (null, null);
            }
        }

        private static string BuildAmazonLoginUrl(string codeChallenge)
        {
            var baseUrl = "https://www.amazon.com/ap/signin?openid.ns=http://specs.openid.net/auth/2.0&openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select&openid.identity=http://specs.openid.net/auth/2.0/identifier_select&openid.mode=checkid_setup&openid.oa2.scope=device_auth_access&openid.ns.oa2=http://www.amazon.com/ap/ext/oauth/2&openid.oa2.response_type=code&openid.oa2.code_challenge_method=S256&openid.oa2.client_id=device:3733646238643238366332613932346432653737653161663637373636363435234132554d56484f58375550345637&language=en_US&marketPlaceId=ATVPDKIKX0DER&openid.return_to=https://www.amazon.com&openid.pape.max_auth_age=0&openid.assoc_handle=amzn_sonic_games_launcher&pageId=amzn_sonic_games_launcher&openid.oa2.code_challenge=";
            return baseUrl + codeChallenge;
        }

        private static string GenerateCodeVerifier()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-~";
            var length = 128;
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            var sb = new StringBuilder(length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        private static string Base64Url(byte[] input) => Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}