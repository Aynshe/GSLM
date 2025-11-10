using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using GameStoreLibraryManager.Common;

namespace GameStoreLibraryManager.Xbox
{
    public class XboxCloudGamingForm : Form
    {
        private readonly bool _fullscreen;
        private string _startUrl = "https://www.xbox.com/play";
        private WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly string _cookieStorePath;
        private readonly string _gameId;
        private XboxAccountClient _apiClient;
        private Config _config;
        private SimpleLogger _logger;
        private readonly HashSet<string> _processedProductIds = new HashSet<string>();
        private MediaScraper _mediaScraper;
        private GamepadListener _gamepadListener;

        public XboxCloudGamingForm(bool fullscreen = true, string gameId = null)
        {
            _fullscreen = fullscreen;
            _gameId = gameId;
            Text = "Xbox Cloud Gaming";
            if (!string.IsNullOrEmpty(_gameId))
            {
                _startUrl = $"https://www.xbox.com/play/launch/{_gameId}";
            }
            if (_fullscreen)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = Screen.PrimaryScreen.WorkingArea;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                Size = new Size(1280, 800);
                MinimumSize = new Size(960, 600);
            }
            KeyPreview = true;

            _config = new Config();
            _logger = new SimpleLogger("xbox_cloud_form.log");
            _apiClient = new XboxAccountClient(_config, _logger);
            _mediaScraper = new MediaScraper(_config, _logger);
            _userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameStoreLibraryManager", "XboxCloudGamingProfile");
            Directory.CreateDirectory(_userDataFolder);
            _cookieStorePath = Path.Combine(PathManager.ApiKeyPath, "xbox_cloud.cookies");

            _gamepadListener = new GamepadListener();
            _gamepadListener.ComboDetected += OnGamepadComboDetected;

            Load += OnLoadAsync;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += async (s, e) => 
            {
                _gamepadListener?.Stop();
                _gamepadListener?.Dispose();
                try { await SaveCookiesAsync(); } catch { }
            };
        }

        private void OnGamepadComboDetected(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnGamepadComboDetected(sender, e)));
                return;
            }

            _gamepadListener.Stop();
            using (var overlay = new ExitOverlayForm())
            {
                overlay.Owner = this;
                if (overlay.ShowDialog() == DialogResult.OK)
                {
                    this.Close();
                }
            }
            _gamepadListener.Start();
        }

        private async void OnLoadAsync(object sender, EventArgs e)
        {
            _gamepadListener.Initialize(this.Handle);
            _gamepadListener.Start();
            _webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = false };
            Controls.Add(_webView);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            // Try to restore cookies before first navigation
            try { RestoreCookies(); } catch { }

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.Source = new Uri(_startUrl);

            _webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        }

        private async void CoreWebView2_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (!_config.GetBoolean("xbox_enable_dynamic_cloud_shortcuts", true))
            {
                return;
            }

            var url = _webView.Source.ToString();
            if (!url.Contains("/play/launch/")) return;

            // The Product ID is the last segment of the URL path, before any query string.
            var productId = url.Split('?')[0].Split('#')[0].Split('/').LastOrDefault();

            // Real ProductIDs are 12-character alphanumeric strings. This avoids acting on game slugs like 'fortnite'.
            if (productId != null && productId.Length == 12 && productId.All(c => char.IsLetterOrDigit(c)))
            {
                // Avoid creating duplicate shortcuts during the same session.
                if (_processedProductIds.Contains(productId))
                {
                    return;
                }

                _logger.Log($"Detected cloud game launch for valid ProductId: {productId}");
                try
                {
                    var region = _config.GetString("xbox_gamepass_region", "US");
                    var language = System.Globalization.CultureInfo.CurrentCulture.Name;
                    var details = await _apiClient.GetProductDetailsAsync(new[] { productId }, region, language);

                    if (details.TryGetValue(productId, out var productData))
                    {
                        var title = productData.GetTitle();
                        if (!string.IsNullOrEmpty(title))
                        {
                            _logger.Log($"Retrieved title: '{title}' for ProductId: {productId}");
                            var gameInfo = new LauncherGameInfo
                            {
                                Id = $"{productId}-cloud",
                                Name = title,
                                Launcher = "Xbox",
                                IsInstalled = false, // Cloud games are never "installed"
                                LauncherUrl = $"internal://xboxcloudgaming-launch/{productId}"
                            };

                            ShortcutManager.CreateShortcut(gameInfo, _config);
                            _processedProductIds.Add(productId); // Mark as handled for this session.
                            _logger.Log($"Successfully created shortcut for '{title}'.");

                            if (_config.GetBoolean("scrape_media", false))
                            {
                                _logger.Log($"Scraping media for '{title}'...");
                                var romsPath = PathManager.XboxRomsPath; // Scrape to the root windows folder
                                var gameDetails = await _mediaScraper.ScrapeGameAsync(gameInfo, romsPath);
                                if (gameDetails != null)
                                {
                                    var gamelistGenerator = new GamelistGenerator(_config, _logger);
                                    gamelistGenerator.UpdateOrAddGameEntry(romsPath, gameInfo, gameDetails);
                                }
                            }
                        }
                        else
                        {
                            _logger.Log($"Could not extract title for ProductId: {productId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to create shortcut for ProductId {productId}: {ex.Message}");
                }
            }
        }

        private void RestoreCookies()
        {
            if (!File.Exists(_cookieStorePath)) return;
            var json = SecureStore.ReadString(_cookieStorePath);
            if (string.IsNullOrWhiteSpace(json)) return;
            var cookies = JsonSerializer.Deserialize<XboxCloudCookie[]>(json);
            if (cookies == null || cookies.Length == 0) return;

            foreach (var c in cookies)
            {
                try
                {
                    var cookie = _webView.CoreWebView2.CookieManager.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                    if (c.Expires.HasValue) cookie.Expires = c.Expires.Value.UtcDateTime;
                    cookie.IsHttpOnly = c.IsHttpOnly;
                    cookie.IsSecure = c.IsSecure;
                    _webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                }
                catch { }
            }
        }

        private async Task SaveCookiesAsync()
        {
            var list = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.xbox.com");
            var toSave = list.Select(c => new XboxCloudCookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                Expires = c.Expires,
                IsHttpOnly = c.IsHttpOnly,
                IsSecure = c.IsSecure,
                IsSession = c.IsSession
            }).ToArray();

            var json = JsonSerializer.Serialize(toSave);
            SecureStore.WriteString(_cookieStorePath, json, protect: true);
        }

        private class XboxCloudCookie
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTimeOffset? Expires { get; set; }
            public bool IsHttpOnly { get; set; }
            public bool IsSecure { get; set; }
            public bool IsSession { get; set; }
        }
    }
}