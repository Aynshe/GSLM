using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Common
{
    // Reusable small topmost notice window for install automation flows
    public sealed class InstallNoticeWindow : IDisposable
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly Thread _uiThread;
        private readonly ManualResetEvent _shown = new ManualResetEvent(false);
        private Form _form;
        private Label _label;
        private readonly TimeSpan? _autoClose;
        private readonly Func<bool> _keepShowingWhile;

        private InstallNoticeWindow(string text, TimeSpan? autoClose, Func<bool> keepShowingWhile)
        {
            _autoClose = autoClose;
            _keepShowingWhile = keepShowingWhile;
            _uiThread = new Thread(() =>
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                _form = CreateForm(text);
                // Setup auto-close timer if requested OR keep-showing predicate if provided
                var t = new System.Windows.Forms.Timer();
                t.Interval = 400; // check frequently without being heavy
                var start = DateTime.UtcNow;
                // The timer logic is removed because ShowDialog is blocking and lifecycle is now externally managed by Dispose.
                _shown.Set();
                _form.ShowDialog();
            })
            { IsBackground = true };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _shown.WaitOne(); // wait for form creation
        }

        public static InstallNoticeWindow ShowTopMost(string text, TimeSpan? autoClose = null, Func<bool> keepShowingWhile = null)
        {
            return new InstallNoticeWindow(text, autoClose, keepShowingWhile);
        }

        public void UpdateText(string newText)
        {
            if (_form == null || _form.IsDisposed || _label == null) return;
            try
            {
                _label.Invoke(new Action(() => { _label.Text = newText; }));
            }
            catch { }
        }

        public void MoveToCenter()
        {
            if (_form == null || _form.IsDisposed) return;
            try
            {
                _form.Invoke(new Action(() =>
                {
                    // Make window larger for centered message
                    _form.Size = new Size(500, 150);
                    if (_label != null) _label.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
                    // Manually center the form
                    var screen = Screen.PrimaryScreen.WorkingArea;
                    _form.Location = new Point(
                        screen.Left + (screen.Width - _form.Width) / 2,
                        screen.Top + (screen.Height - _form.Height) / 2
                    );
                }));
            }
            catch { }
        }

        public void BringToFront()
        {
            if (_form == null || _form.IsDisposed) return;
            try
            {
                _form.Invoke(new Action(() => {
                    _form.BringToFront();
                    SetForegroundWindow(_form.Handle);
                }));
            }
            catch { }
        }

        private Form CreateForm(string text)
        {
            var form = new TopMostForm
            {
                Text = string.Empty,
                Size = new Size(320, 90),
                StartPosition = FormStartPosition.Manual,
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(30, 30, 30),
                Opacity = 0.8,
                Padding = new Padding(16)
            };
            // Position in top-right corner
            var screen = Screen.PrimaryScreen.WorkingArea;
            form.Location = new Point(screen.Right - form.Width - 20, screen.Top + 20);
            _label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            form.Controls.Add(_label);
            return form;
        }

        public void Dispose()
        {
            try
            {
                _form?.Invoke(new Action(() => _form.Close()));
                _uiThread?.Join(1000);
            }
            catch { }
            _shown?.Dispose();
        }

        private class TopMostForm : Form
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW;
                    return cp;
                }
            }
        }
    }
}
