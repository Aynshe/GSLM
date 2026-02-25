using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;

namespace GameStoreLibraryManager.Menu
{
    [ToolboxItem(true)]
    public class SettingTextRow : SettingRow
    {
        private TextBox _textBox;

        public TextBox TextBox => _textBox;

        public SettingTextRow()
        {
            _textBox = new TextBox
            {
                Location = new Point(70, 7),
                Width = 250,
                Font = new Font("Segoe UI", 10F),
                Anchor = AnchorStyles.Right
            };
            this.controlPanel.Controls.Add(_textBox);
            RegisterControl(_textBox);
        }

        [Category("Appearance")]
        public string TextValue
        {
            get => _textBox.Text;
            set => _textBox.Text = value;
        }
    }
}
