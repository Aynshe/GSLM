using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Common
{
    public class PrereqForm : Form
    {
        public PrereqForm(bool needDotnet, bool needWebView2)
        {
            Text = "Missing prerequisites";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 210);

            var lbl = new Label
            {
                AutoSize = false,
                Text = "Some prerequisites are missing for Game Store Library Manager to run properly:\n\n" +
                       (needDotnet ? "• .NET 8 Desktop Runtime\n" : "") +
                       (needWebView2 ? "• Microsoft Edge WebView2 Runtime\n" : "") +
                       "\nPlease install the missing components and restart the application.",
                Left = 12,
                Top = 12,
                Width = 536,
                Height = 120
            };
            Controls.Add(lbl);

            int btnTop = 140;
            int btnLeft = 12;

            if (needDotnet)
            {
                var btnDotnet = new Button
                {
                    Text = ".NET 8 Desktop Runtime",
                    Left = btnLeft,
                    Top = btnTop,
                    Width = 220,
                    Height = 30
                };
                btnDotnet.Click += (s, e) => OpenUrl("https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime");
                Controls.Add(btnDotnet);
                btnLeft += 230;
            }

            if (needWebView2)
            {
                var btnWebView2 = new Button
                {
                    Text = "WebView2 Runtime",
                    Left = btnLeft,
                    Top = btnTop,
                    Width = 220,
                    Height = 30
                };
                btnWebView2.Click += (s, e) => OpenUrl("https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section");
                Controls.Add(btnWebView2);
            }

            var btnClose = new Button
            {
                Text = "Close",
                Left = ClientSize.Width - 90,
                Top = ClientSize.Height - 40,
                Width = 75,
                Height = 28,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Clipboard.SetText(url);
                    MessageBox.Show("Failed to open browser. The URL has been copied to clipboard:\n" + url, "Open URL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            }
        }
    }
}
