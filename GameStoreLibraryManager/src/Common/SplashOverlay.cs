using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Common
{
    // Simple centered splash overlay with progress percentage
    public class SplashOverlay
    {
        private Thread uiThread;
        private Form form;
        private Label titleLabel;
        private Label percentLabel;
        private int currentPercent;
        private int targetPercent;
        private volatile bool shouldClose = false;
        private readonly string initialText;
        private readonly int initialPercent;

        private SplashOverlay(string text, int percent)
        {
            initialText = text ?? "Loading online store library...";
            initialPercent = Math.Max(0, Math.Min(100, percent));
        }

        public static SplashOverlay Start(string text, int percent = 0)
        {
            var splash = new SplashOverlay(text, percent);
            splash.uiThread = new Thread(splash.Run)
            {
                IsBackground = true
            };
            splash.uiThread.SetApartmentState(ApartmentState.STA);
            splash.uiThread.Start();
            // Give the UI a moment to initialize
            var start = DateTime.UtcNow;
            while (splash.form == null && (DateTime.UtcNow - start).TotalMilliseconds < 1000)
            {
                Thread.Sleep(10);
            }
            return splash;
        }

        private void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            form = CreateForm();
            titleLabel.Text = initialText;
            currentPercent = initialPercent;
            targetPercent = initialPercent;
            percentLabel.Text = currentPercent.ToString() + "%";

            Application.Run(form);
        }

        private Form CreateForm()
        {
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                BackColor = Color.FromArgb(30, 30, 30),
                Opacity = 0.85,
                Size = new Size(560, 160)
            };

            // center on primary screen
            var screen = Screen.PrimaryScreen.WorkingArea;
            f.Location = new Point(
                screen.Left + (screen.Width - f.Width) / 2,
                screen.Top + (screen.Height - f.Height) / 2);

            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(24)
            };

            titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 80,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };

            percentLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11f, FontStyle.Regular)
            };

            panel.Controls.Add(percentLabel);
            panel.Controls.Add(titleLabel);
            f.Controls.Add(panel);

            // Allow dragging the splash by mouse
            bool dragging = false; Point dragStart = Point.Empty;
            panel.MouseDown += (s, e) => { dragging = true; dragStart = e.Location; };
            panel.MouseMove += (s, e) => { if (dragging) f.Location = new Point(f.Left + e.X - dragStart.X, f.Top + e.Y - dragStart.Y); };
            panel.MouseUp += (s, e) => { dragging = false; };

            // Close loop
            var closeTimer = new System.Windows.Forms.Timer { Interval = 100 };
            closeTimer.Tick += (s, e) =>
            {
                if (shouldClose)
                {
                    closeTimer.Stop();
                    try { f.Close(); } catch { }
                }
            };
            closeTimer.Start();

            // Smooth progress animation timer
            var progressTimer = new System.Windows.Forms.Timer { Interval = 25 };
            progressTimer.Tick += (s, e) =>
            {
                if (currentPercent < targetPercent)
                {
                    currentPercent += Math.Max(1, (targetPercent - currentPercent) / 10); // ease towards target
                    if (currentPercent > targetPercent) currentPercent = targetPercent;
                    percentLabel.Text = currentPercent.ToString() + "%";
                }
            };
            progressTimer.Start();

            return f;
        }

        public void SetText(string text)
        {
            if (form == null || form.IsDisposed) return;
            try { form.BeginInvoke(new Action(() => titleLabel.Text = text)); } catch { }
        }

        public void SetProgress(int percent)
        {
            if (form == null || form.IsDisposed) return;
            percent = Math.Max(0, Math.Min(100, percent));
            try { form.BeginInvoke(new Action(() => targetPercent = percent)); } catch { }
        }

        public void Close()
        {
            shouldClose = true;
        }
    }
}
