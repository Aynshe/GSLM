using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameStoreLibraryManager.Menu
{
    public class ModernToggleSwitch : UserControl
    {
        private bool _checked = false;
        public bool Checked
        {
            get => _checked;
            set
            {
                _checked = value;
                this.Invalidate(); // Redraw the control when the state changes
            }
        }

        private bool _isFocused = false;

        public ModernToggleSwitch()
        {
            this.DoubleBuffered = true;
            this.Width = 80;
            this.Height = 30;
            this.Cursor = Cursors.Hand;
            this.Click += (sender, e) => { Checked = !Checked; };

            this.GotFocus += (s, e) => { _isFocused = true; this.Invalidate(); };
            this.LostFocus += (s, e) => { _isFocused = false; this.Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Material Design Colors
            Color trackOffColor = Color.FromArgb(120, 120, 120); // Dark Gray
            Color trackOnColor = Color.FromArgb(76, 175, 80); // Material Green for "On" state
            Color thumbColor = Color.FromArgb(245, 245, 245);   // White/Off-white

            // Track
            var trackHeight = this.Height / 2;
            var backRect = new Rectangle(0, (this.Height - trackHeight) / 2, this.Width - 1, trackHeight);
            using (var path = GetPillShape(backRect))
            {
                using (var backBrush = new SolidBrush(Checked ? trackOnColor : trackOffColor))
                {
                    e.Graphics.FillPath(backBrush, path);
                }
            }

            // Thumb
            int thumbSize = this.Height - 4; // Make it almost the full height
            int thumbY = (this.Height - thumbSize) / 2;
            var thumbRect = Checked ? new Rectangle(this.Width - thumbSize - 2, thumbY, thumbSize, thumbSize)
                                     : new Rectangle(2, thumbY, thumbSize, thumbSize);
            using (var thumbBrush = new SolidBrush(thumbColor))
            {
                e.Graphics.FillEllipse(thumbBrush, thumbRect);
            }

            // Text on Thumb
            string text = Checked ? "On" : "Off";
            // Use a slightly darker text for "On" for better contrast against the green track
            Color textColor = Checked ? Color.Black : Color.FromArgb(64, 64, 64);
            using (Font textFont = new Font(this.Font.FontFamily, 7, FontStyle.Bold))
            {
                SizeF textSize = e.Graphics.MeasureString(text, textFont);
                PointF textLocation = new PointF(
                    thumbRect.X + (thumbRect.Width - textSize.Width) / 2,
                    thumbRect.Y + (thumbRect.Height - textSize.Height) / 2
                );

                using (var textBrush = new SolidBrush(textColor))
                {
                    e.Graphics.DrawString(text, textFont, textBrush, textLocation);
                }
            }


            // Draw focus rectangle if focused
            if (_isFocused)
            {
                var focusRect = this.ClientRectangle;
                focusRect.Inflate(-2, -2);
                ControlPaint.DrawFocusRectangle(e.Graphics, focusRect);
            }
        }

        private GraphicsPath GetPillShape(Rectangle rect)
        {
            var path = new GraphicsPath();
            int diameter = rect.Height;
            var arcRectLeft = new Rectangle(rect.Left, rect.Top, diameter, diameter);
            var arcRectRight = new Rectangle(rect.Right - diameter, rect.Top, diameter, diameter);
            path.AddArc(arcRectLeft, 90, 180);
            path.AddArc(arcRectRight, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
