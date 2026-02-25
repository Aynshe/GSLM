using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Menu
{
    [ToolboxItem(true)]
    public class SettingRow : UserControl
    {
        protected Label label;
        protected Panel controlPanel;
        private Color _focusColor = Color.FromArgb(100, 80, 80, 80);
        private Color _hoverColor = Color.FromArgb(50, 80, 80, 80);
        private bool _isFocused = false;

        [Category("Appearance")]
        public string SettingText
        {
            get => label.Text;
            set => label.Text = value;
        }

        [Category("Data")]
        public string ConfigKey { get; set; }
        
        [Category("Data")]
        public string Category { get; set; }

        public SettingRow()
        {
            this.Height = 40;
            this.Size = new Size(750, 40);
            this.BackColor = Color.Transparent;
            this.Margin = new Padding(0);
            this.Dock = DockStyle.Top;

            label = new Label
            {
                Location = new Point(10, 0),
                Size = new Size(400, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom
            };

            controlPanel = new Panel
            {
                Location = new Point(410, 0),
                Size = new Size(330, 40),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
            };

            this.Controls.Add(label);
            this.Controls.Add(controlPanel);

            this.MouseEnter += (s, e) => UpdateHighlight();
            this.MouseLeave += (s, e) => UpdateHighlight();
            label.MouseEnter += (s, e) => UpdateHighlight();
        }

        protected void RegisterControl(Control c)
        {
            c.GotFocus += (s, e) => { _isFocused = true; UpdateHighlight(); };
            c.LostFocus += (s, e) => { _isFocused = false; UpdateHighlight(); };
            c.MouseEnter += (s, e) => UpdateHighlight();
        }

        private void UpdateHighlight()
        {
            if (!this.IsHandleCreated) return;
            if (_isFocused) this.BackColor = _focusColor;
            else if (this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition))) this.BackColor = _hoverColor;
            else this.BackColor = Color.Transparent;
        }
    }
}
