using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GameStoreLibraryManager.Auth
{
    public class AuthBrowserForm : Form
    {
        private readonly string _storeName;
        private readonly string _startUrl;
        private readonly Regex _codeRegex; // optional: detect code in URL
        private readonly Regex _contentRegex; // optional: detect code/content within page HTML/text
        private readonly bool _steamPasteMode;
        private readonly string _postLoginRedirectUrl; // optional: navigate here once logged in
        private bool _postLoginRedirectTried = false;

        private readonly WebView2 _webView;
        private readonly TextBox _pasteBox;
        private readonly Button _saveButton;
        private readonly Label _hintLabel;

        private string _lastNavigatedUrl;

        public event Action<string> CodeDetected; // emits URL or pasted value

        public AuthBrowserForm(string storeName, string startUrl, Regex codeRegex = null, bool steamPasteMode = false, Regex contentRegex = null, string postLoginRedirectUrl = null)
        {
            _storeName = storeName;
            _startUrl = startUrl;
            _codeRegex = codeRegex;
            _contentRegex = contentRegex;
            _steamPasteMode = steamPasteMode;
            _postLoginRedirectUrl = postLoginRedirectUrl;

            Text = $"{storeName} Auth - Embedded Browser";
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;

            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            Controls.Add(_webView);

            // Steam paste bar (top panel)
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8), BackColor = Color.FromArgb(245,245,245) };
            _hintLabel = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _pasteBox = new TextBox { Dock = DockStyle.Right, Width = 420 };
            _saveButton = new Button { Text = "Save", Dock = DockStyle.Right, Width = 100 };
            _saveButton.Click += (_, __) => OnSaveClicked();
            topPanel.Controls.Add(_hintLabel);
            topPanel.Controls.Add(_saveButton);
            topPanel.Controls.Add(_pasteBox);

            if (_steamPasteMode)
            {
                Controls.Add(topPanel);
                _hintLabel.Text = "Steam: connect and request your API key on this page, then paste the key here and click Save.";
            }
            else
            {
                topPanel.Visible = false;
            }

            Load += async (_, __) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            // Keep popups and target=_blank inside the same WebView
            _webView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                try
                {
                    var target = e.Uri;
                    if (!string.IsNullOrEmpty(target))
                    {
                        e.Handled = true;
                        _webView.CoreWebView2.Navigate(target);
                    }
                }
                catch { }
            };

            if (!string.IsNullOrWhiteSpace(_startUrl))
            {
                _webView.Source = new Uri(_startUrl);
            }
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _lastNavigatedUrl = e.Uri;
            TryDetectCodeFromUrl(e.Uri);
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastNavigatedUrl))
            {
                TryDetectCodeFromUrl(_lastNavigatedUrl);
            }

            // Also try to detect from page content if a content regex is provided (e.g., Steam API key on page)
            if (_contentRegex != null)
            {
                _ = TryDetectCodeFromContentAsync();
            }

            // Auto-redirect after login for providers like Epic
            if (!_postLoginRedirectTried && !string.IsNullOrEmpty(_postLoginRedirectUrl) && !string.IsNullOrEmpty(_lastNavigatedUrl))
            {
                try
                {
                    var uri = new Uri(_lastNavigatedUrl, UriKind.Absolute);
                    if (uri.Host.Contains("epicgames.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var isLoginPage = uri.AbsolutePath.Contains("/id/login", StringComparison.OrdinalIgnoreCase);
                        var alreadyOnRedirect = _lastNavigatedUrl.StartsWith(_postLoginRedirectUrl, StringComparison.OrdinalIgnoreCase);
                        if (!isLoginPage && !alreadyOnRedirect)
                        {
                            _postLoginRedirectTried = true;
                            _webView.CoreWebView2.Navigate(_postLoginRedirectUrl);
                        }
                    }
                }
                catch { }
            }
        }

        private void TryDetectCodeFromUrl(string url)
        {
            if (_steamPasteMode) return;
            if (string.IsNullOrEmpty(url) || _codeRegex == null) return;
            var m = _codeRegex.Match(url);
            if (m.Success)
            {
                CodeDetected?.Invoke(url);
            }
        }

        private async Task TryDetectCodeFromContentAsync()
        {
            try
            {
                // Read the full page text and HTML to improve chances
                var bodyTextJson = await _webView.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                var htmlJson = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement ? document.documentElement.outerHTML : ''");
                string Unwrap(string jsJson) => string.IsNullOrEmpty(jsJson) ? string.Empty : System.Text.Json.JsonSerializer.Deserialize<string>(jsJson) ?? string.Empty;
                var bodyText = Unwrap(bodyTextJson);
                var html = Unwrap(htmlJson);

                string candidate = null;
                string ExtractFirstNonEmptyGroup(Match m)
                {
                    if (!m.Success) return null;
                    for (int i = 1; i < m.Groups.Count; i++)
                    {
                        var g = m.Groups[i]?.Value;
                        if (!string.IsNullOrWhiteSpace(g)) return g;
                    }
                    return m.Value;
                }

                var m1 = _contentRegex.Match(bodyText ?? string.Empty);
                candidate = ExtractFirstNonEmptyGroup(m1);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    var m2 = _contentRegex.Match(html ?? string.Empty);
                    candidate = ExtractFirstNonEmptyGroup(m2);
                }

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    CodeDetected?.Invoke(candidate.Trim());
                }
            }
            catch { }
        }

        private void OnSaveClicked()
        {
            if (!_steamPasteMode) return;
            var val = _pasteBox.Text?.Trim();
            if (!string.IsNullOrEmpty(val))
            {
                CodeDetected?.Invoke(val);
            }
        }
    }
}
