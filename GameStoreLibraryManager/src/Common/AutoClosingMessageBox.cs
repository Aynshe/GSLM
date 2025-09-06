using System;
using System.Drawing;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Common
{
    public class AutoClosingMessageBox : Form
    {
        private System.Windows.Forms.Timer _timer;

        public AutoClosingMessageBox(string message, string title)
        {
            this.Text = title;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 400;
            this.Height = 150;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;
            this.Padding = new Padding(20);

            var messageLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            this.Controls.Add(messageLabel);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 3000; // 3 seconds
            _timer.Tick += (sender, e) => {
                _timer.Stop();
                this.Close();
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
        }

        /// <summary>
        /// Shows a message box that automatically closes after a few seconds.
        /// </summary>
        /// <param name="owner">The parent window.</param>
        /// <param name="message">The message to display.</param>
        /// <param name="title">The title of the message box.</param>
        public static void Show(IWin32Window owner, string message, string title)
        {
            using (var form = new AutoClosingMessageBox(message, title))
            {
                form.ShowDialog(owner);
            }
        }
    }
}
