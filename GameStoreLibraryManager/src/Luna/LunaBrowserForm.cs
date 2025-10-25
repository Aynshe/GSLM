using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameStoreLibraryManager.Common;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GameStoreLibraryManager.Luna
{
    public class LunaBrowserForm : Form
    {
        private readonly bool _fullscreen;
        private readonly string _gameId;
        private WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly Config _config;
        private readonly SimpleLogger _logger;
        private readonly string _cookieStorePath;

        // State for dynamic shortcut creation
        private string _lastDetectedGameId;
        private string _lastDetectedGameName;
        private bool _isGameLaunched;
        private bool _enableDynamicShortcuts;

        public LunaBrowserForm(bool fullscreen = true, string gameId = null)
        {
            _fullscreen = fullscreen;
            _gameId = gameId;
            _logger = new SimpleLogger("luna_browser.log");
            _config = new Config();

            Text = "Amazon Luna";
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

            _userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameStoreLibraryManager", "LunaProfile");
            Directory.CreateDirectory(_userDataFolder);
            _cookieStorePath = Path.Combine(PathManager.ApiKeyPath, "luna.cookies");

            _enableDynamicShortcuts = _config.GetBoolean("luna_enable_dynamic_cloud_shortcuts", true);

            Load += OnLoadAsync;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += async (s, e) => { if (_webView?.CoreWebView2 != null) await SaveCookiesAsync(); };
        }

        private async void OnLoadAsync(object sender, EventArgs e)
        {
            _webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = false };
            Controls.Add(_webView);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            try { RestoreCookies(); } catch (Exception ex) { _logger.Log($"[ERROR] Failed to restore cookies: {ex.Message}"); }

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            if (_enableDynamicShortcuts)
            {
                _webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
            }

            var lunaDomain = _config.GetString("luna_domain", "luna.amazon.com");
            var startUrl = $"https://{lunaDomain}/";

            if (!string.IsNullOrEmpty(_gameId))
            {
                startUrl = $"https://{lunaDomain}/{_gameId}?autoplay=true";
                _logger.Log($"Direct launch requested for gameId: {_gameId}. URL: {startUrl}");
            }
            else
            {
                startUrl = $"https://{lunaDomain}/browse?quick_search=my_games&g=s";
            }

            _webView.Source = new Uri(startUrl);
        }

        private async void CoreWebView2_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            var url = _webView.Source.ToString();
            _logger.Log($"URL Changed: {url}");

            var gamePageRegex = new Regex(@"/game/([a-zA-Z0-9\-]+)/([A-Z0-9]{10})");
            var match = gamePageRegex.Match(url);

            if (match.Success)
            {
                _lastDetectedGameName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(match.Groups[1].Value.Replace('-', ' '));
                _lastDetectedGameId = match.Groups[2].Value;
                _isGameLaunched = false;
                _logger.Log($"Detected game page: Name='{_lastDetectedGameName}', ID='{_lastDetectedGameId}'");
                return;
            }

            if (url.Contains("/player"))
            {
                _isGameLaunched = true;
                _logger.Log("Detected player URL.");
            }

            if (!string.IsNullOrEmpty(_lastDetectedGameId) && _isGameLaunched)
            {
                _logger.Log($"Launch confirmed for {_lastDetectedGameName}. Creating shortcut...");
                await CreateShortcutAndScrapeAsync(_lastDetectedGameId, _lastDetectedGameName);
                _lastDetectedGameId = null;
                _lastDetectedGameName = null;
                _isGameLaunched = false;
            }
        }

        private async Task CreateShortcutAndScrapeAsync(string gameId, string gameName)
        {
            try
            {
                var gameInfo = new LauncherGameInfo
                {
                    Id = gameId,
                    Name = gameName,
                    IsInstalled = true,
                    Launcher = "Amazon",
                    LauncherUrl = $"internal://luna-launch/{gameId}"
                };

                ShortcutManager.CreateShortcut(gameInfo, _config);

                var mediaScraper = new MediaScraper(_config, _logger);
                var gameDetails = await mediaScraper.ScrapeGameAsync(gameInfo, PathManager.AmazonRomsPath);

                if (gameDetails == null)
                {
                    _logger.Log($"[WARN] Media scraping failed for '{gameName}'. Creating a basic gamelist entry.");
                    gameDetails = new GameDetails { Name = gameName };
                }

                var gamelistGenerator = new GamelistGenerator(_config, _logger);
                gamelistGenerator.UpdateOrAddGameEntry(PathManager.AmazonRomsPath, gameInfo, gameDetails);

                _logger.Log($"Successfully created shortcut and updated gamelist for '{gameName}'.");
            }
            catch (Exception ex)
            {
                _logger.Log($"[ERROR] Failed to create shortcut or scrape media for '{gameName}': {ex.Message}");
            }
        }

        private void RestoreCookies()
        {
            if (!File.Exists(_cookieStorePath)) return;
            var json = SecureStore.ReadString(_cookieStorePath);
            if (string.IsNullOrWhiteSpace(json)) return;
            var cookies = JsonSerializer.Deserialize<LunaCookie[]>(json);
            if (cookies == null || cookies.Length == 0) return;

            foreach (var c in cookies)
            {
                var cookie = _webView.CoreWebView2.CookieManager.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                if (c.Expires.HasValue) cookie.Expires = c.Expires.Value.UtcDateTime;
                cookie.IsHttpOnly = c.IsHttpOnly;
                cookie.IsSecure = c.IsSecure;
                _webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
            }
            _logger.Log($"Restored {cookies.Length} cookies for Luna.");
        }

        private async Task SaveCookiesAsync()
        {
            var lunaDomain = _config.GetString("luna_domain", "luna.amazon.com");
            var list = await _webView.CoreWebView2.CookieManager.GetCookiesAsync($"https://{lunaDomain}");
            var toSave = list.Select(c => new LunaCookie
            {
                Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path,
                Expires = c.Expires, IsHttpOnly = c.IsHttpOnly, IsSecure = c.IsSecure, IsSession = c.IsSession
            }).ToArray();

            var json = JsonSerializer.Serialize(toSave);
            SecureStore.WriteString(_cookieStorePath, json, protect: true);
            _logger.Log($"Saved {toSave.Length} cookies for Luna.");
        }

        private class LunaCookie
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