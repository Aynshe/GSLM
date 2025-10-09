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
        private readonly string _startUrl = "https://www.xbox.com/play";
        private WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly string _cookieStorePath;

        public XboxCloudGamingForm(bool fullscreen = true)
        {
            _fullscreen = fullscreen;
            Text = "Xbox Cloud Gaming";
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

            _userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameStoreLibraryManager", "XboxCloudGamingProfile");
            Directory.CreateDirectory(_userDataFolder);
            _cookieStorePath = Path.Combine(PathManager.ApiKeyPath, "xbox_cloud.cookies");

            Load += OnLoadAsync;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private async void OnLoadAsync(object sender, EventArgs e)
        {
            _webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = false };
            Controls.Add(_webView);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            // Try to restore cookies before first navigation
            try { RestoreCookies(); } catch { }

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.Source = new Uri(_startUrl);

            // Save cookies on close
            FormClosing += async (_, __) =>
            {
                try { await SaveCookiesAsync(); } catch { }
            };
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