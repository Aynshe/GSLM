using System.Windows.Forms;
using System.ComponentModel;

namespace GameStoreLibraryManager.Menu
{
    [ToolboxItem(true)]
    public class SettingToggleRow : SettingRow
    {
        private ModernToggleSwitch _toggle;

        [Category("Appearance")]
        public bool Checked
        {
            get => _toggle.Checked;
            set => _toggle.Checked = value;
        }

        public SettingToggleRow()
        {
            _toggle = new ModernToggleSwitch
            {
                Location = new System.Drawing.Point(280, 5),
                Anchor = AnchorStyles.Right
            };
            this.controlPanel.Controls.Add(_toggle);
            RegisterControl(_toggle);
        }
        
        public Control InternalControl => _toggle;
    }
}
