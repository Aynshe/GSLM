using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;

namespace GameStoreLibraryManager.Menu
{
    [ToolboxItem(true)]
    public class SettingComboRow : SettingRow
    {
        private ComboBox _comboBox;

        public ComboBox ComboBox => _comboBox;

        public SettingComboRow()
        {
            _comboBox = new ComboBox
            {
                Location = new Point(70, 7),
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10F),
                Anchor = AnchorStyles.Right
            };
            this.controlPanel.Controls.Add(_comboBox);
            RegisterControl(_comboBox);
        }

        [Category("Appearance")]
        public int SelectedIndex
        {
            get => _comboBox.SelectedIndex;
            set => _comboBox.SelectedIndex = value;
        }
    }
}
