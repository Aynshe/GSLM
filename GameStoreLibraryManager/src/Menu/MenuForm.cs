using System;
using System.Drawing;
using System.Windows.Forms;
using GameStoreLibraryManager.Common;
using System.Collections.Generic;
using System.Linq;
using SharpDX.XInput;

namespace GameStoreLibraryManager.Menu
{
    public class MenuForm : Form
    {
        private FlowLayoutPanel _mainPanel;
        private Button _saveButton;
        private Button _cancelButton;
        private Label _categoryLabel;
        private Config _config;
        private List<Control> _navigableControls;
        private Panel _focusedRow;
        private Panel _hoveredRow;
        private Control _regionPanel; // Référence au panneau de sélection de région
        private readonly Color _focusColor = Color.FromArgb(100, 80, 80, 80);
        private readonly Color _hoverColor = Color.FromArgb(50, 80, 80, 80);
        private System.Windows.Forms.Timer _gamepadTimer;
        private Controller _controller;
        private Gamepad _previousState;

        public MenuForm()
        {
            this.Text = "Settings";
            this.Width = 800;
            this.Height = 600;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;
            this.Opacity = 0.95;

            InitializeControls();

            this.Load += MenuForm_Load;
        }

        private Point _dragStartPoint;

        private void InitializeControls()
        {
            // Title Bar Panel
            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            var titleLabel = new Label
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            titlePanel.Controls.Add(titleLabel);
            titlePanel.MouseDown += TitlePanel_MouseDown;
            titlePanel.MouseMove += TitlePanel_MouseMove;
            titlePanel.MouseUp += TitlePanel_MouseUp;
            titleLabel.MouseDown += TitlePanel_MouseDown;
            titleLabel.MouseMove += TitlePanel_MouseMove;
            titleLabel.MouseUp += TitlePanel_MouseUp;

            _mainPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            _mainPanel.MouseLeave += (s, e) => ClearHoverHighlight();


            // Modern Buttons
            _saveButton = new Button { Text = "Save", Width = 120, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(138, 43, 226), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            _saveButton.FlatAppearance.BorderSize = 0;
            _saveButton.Click += SaveButton_Click;

            _cancelButton = new Button { Text = "Cancel", Width = 120, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), Font = new Font("Segoe UI", 10F) };
            _cancelButton.FlatAppearance.BorderSize = 0;
            _cancelButton.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            // Category Label
            _categoryLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12F, FontStyle.Italic),
                ForeColor = Color.Silver,
                AutoSize = false
            };

            // Bottom Panel
            var bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(30, 30, 30),
                ColumnCount = 3,
                RowCount = 1,
            };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Label column
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F)); // Cancel button
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F)); // Save button
            bottomPanel.Controls.Add(_categoryLabel, 0, 0);
            bottomPanel.Controls.Add(_cancelButton, 1, 0);
            bottomPanel.Controls.Add(_saveButton, 2, 0);

            // Add controls in correct order to fix docking/truncation
            // The Fill-docked control should be added first to ensure it fills the space
            // between the other docked controls, rather than being drawn underneath them.
            this.Controls.Add(_mainPanel);
            this.Controls.Add(titlePanel);
            this.Controls.Add(bottomPanel);
        }

        private void MenuForm_Load(object sender, EventArgs e)
        {
            _config = new Config();

            // Dictionnaire des régions supportées par Game Pass
            var gamePassRegions = new Dictionary<string, string>
            {
                { "US", "United States" },
                { "FR", "France" },
                { "GB", "United Kingdom" },
                { "DE", "Germany" },
                { "JP", "Japan" },
                { "CA", "Canada" },
                { "AU", "Australia" },
                { "BR", "Brazil" },
                { "MX", "Mexico" },
                { "ES", "Spain" },
                { "IT", "Italy" },
                { "KR", "South Korea" }
            };

            // Position the form on the correct screen before doing anything else
            int screenIndex = _config.GetInt("screen_index", 0);
            Screen[] screens = Screen.AllScreens;
            if (screenIndex >= 0 && screenIndex < screens.Length)
            {
                this.StartPosition = FormStartPosition.Manual;
                Screen screen = screens[screenIndex];
                this.Location = new Point(
                    screen.WorkingArea.Left + (screen.WorkingArea.Width - this.Width) / 2,
                    screen.WorkingArea.Top + (screen.WorkingArea.Height - this.Height) / 2
                );
            }
            // If the index is invalid, it will fall back to the Form's default StartPosition, which is CenterScreen.

            var settings = _config.GetSettings();
            _navigableControls = new List<Control>();
            string currentCategory = "Global";

            _mainPanel.SuspendLayout(); // Suspend layout

            // First Pass: Create and add all controls without setting width
            foreach (var setting in settings)
            {
                if (setting.Key == Config.SectionHeaderKey)
                {
                    currentCategory = setting.Comment;
                    var headerLabel = new Label
                    {
                        Text = setting.Comment,
                        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                        ForeColor = Color.FromArgb(138, 43, 226), // Purple
                        Padding = new Padding(0, 15, 0, 5),
                        Margin = new Padding(0),
                        AutoSize = true // Let label size itself initially
                    };
                    _mainPanel.Controls.Add(headerLabel);
                }
                else if (setting.Key == "hfsplay_scraper_media_types")
                {
                    var hfsplaySetting = settings.First(s => s.Key == "hfsplay_scraper_media_types");
                    CreateScraperCheckboxes(hfsplaySetting, currentCategory, _mainPanel);

                    var steamSetting = settings.First(s => s.Key == "steam_scraper_media_types");
                    CreateScraperCheckboxes(steamSetting, currentCategory, _mainPanel);

                    var gogSetting = settings.First(s => s.Key == "gog_scraper_media_types");
                    CreateScraperCheckboxes(gogSetting, currentCategory, _mainPanel);
                }
                else if (setting.Key.EndsWith("_scraper_media_types"))
                {
                    // Do nothing, handled above
                }
                else if (setting.Key == "xbox_gamepass_region")
                {
                    var settingPanel = new TableLayoutPanel
                    {
                        Height = 40,
                        ColumnCount = 2,
                        RowCount = 1,
                        Tag = currentCategory,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0)
                    };
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                    var label = new Label
                    {
                        Text = setting.Comment,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = new Font("Segoe UI", 10F)
                    };

                    var regionComboBox = new ComboBox
                    {
                        Tag = setting.Key,
                        Anchor = AnchorStyles.Right,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Width = 150,
                        Font = new Font("Segoe UI", 10F)
                    };

                    // Ajouter les régions à la liste déroulante
                    foreach (var region in gamePassRegions)
                    {
                        regionComboBox.Items.Add($"{region.Key} - {region.Value}");
                    }

                    // Sélectionner la région actuelle
                    string currentRegion = _config.GetString(setting.Key, "US").ToUpper();
                    bool regionFound = false;
                    
                    for (int i = 0; i < regionComboBox.Items.Count; i++)
                    {
                        string item = regionComboBox.Items[i].ToString();
                        if (item.StartsWith(currentRegion + " - "))
                        {
                            regionComboBox.SelectedIndex = i;
                            regionFound = true;
                            break;
                        }
                    }
                    
                    if (!regionFound && regionComboBox.Items.Count > 0)
                    {
                        // Si la région n'est pas trouvée, sélectionner la première de la liste
                        regionComboBox.SelectedIndex = 0;
                        // Mettre à jour la configuration avec la valeur par défaut
                        var defaultSettings = new Dictionary<string, string>
                        {
                            { setting.Key, regionComboBox.Items[0].ToString().Substring(0, 2) }
                        };
                        _config.SaveSettings(defaultSettings);
                    }

                    void OnRowEnter(object s, EventArgs ev) => SetHoverHighlight(settingPanel);
                    void OnRowLeave(object s, EventArgs ev) => ClearHoverHighlight();
                    settingPanel.MouseEnter += OnRowEnter;
                    settingPanel.MouseLeave += OnRowLeave;
                    label.MouseEnter += OnRowEnter;
                    regionComboBox.MouseEnter += OnRowEnter;

                    regionComboBox.GotFocus += (s, ev) => SetFocusHighlight(regionComboBox);
                    regionComboBox.LostFocus += (s, ev) => ClearFocusHighlight();
                    regionComboBox.SelectedIndexChanged += (s, ev) => UpdateCategoryLabel(settingPanel);

                    settingPanel.Controls.Add(label, 0, 0);
                    settingPanel.Controls.Add(regionComboBox, 1, 0);
                    _mainPanel.Controls.Add(settingPanel);
                    _navigableControls.Add(regionComboBox);

                    // Stocker la référence pour pouvoir la montrer/cacher
                    _regionPanel = settingPanel;
                }
                else if (setting.Key == "screen_index")
                {
                    var settingPanel = new TableLayoutPanel
                    {
                        Height = 40,
                        ColumnCount = 2,
                        RowCount = 1,
                        Tag = currentCategory,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0)
                    };
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                    var label = new Label
                    {
                        Text = setting.Comment,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = new Font("Segoe UI", 10F)
                    };

                    var screenComboBox = new ComboBox
                    {
                        Tag = setting.Key,
                        Anchor = AnchorStyles.Right,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Width = 350,
                        Font = new Font("Segoe UI", 10F)
                    };

                    for (int i = 0; i < screens.Length; i++)
                    {
                        screenComboBox.Items.Add($"Screen {i}: {screens[i].DeviceName} ({(screens[i].Primary ? "Primary" : "Secondary")})");
                    }
                    if (screenIndex >= 0 && screenIndex < screenComboBox.Items.Count)
                    {
                        screenComboBox.SelectedIndex = screenIndex;
                    }

                    void OnRowEnter(object s, EventArgs ev) => SetHoverHighlight(settingPanel);
                    void OnRowLeave(object s, EventArgs ev) => ClearHoverHighlight();
                    settingPanel.MouseEnter += OnRowEnter;
                    settingPanel.MouseLeave += OnRowLeave;
                    label.MouseEnter += OnRowEnter;
                    screenComboBox.MouseEnter += OnRowEnter;

                    screenComboBox.GotFocus += (s, ev) => SetFocusHighlight(screenComboBox);
                    screenComboBox.LostFocus += (s, ev) => ClearFocusHighlight();

                    screenComboBox.SelectedIndexChanged += (s, ev) => UpdateCategoryLabel(settingPanel);

                    settingPanel.Controls.Add(label, 0, 0);
                    settingPanel.Controls.Add(screenComboBox, 1, 0);
                    _mainPanel.Controls.Add(settingPanel);
                    _navigableControls.Add(screenComboBox);
                }
                else if (bool.TryParse(_config.GetString(setting.Key, setting.Value), out bool val))
                {
                    var settingPanel = new TableLayoutPanel
                    {
                        Height = 40,
                        ColumnCount = 2,
                        RowCount = 1,
                        Tag = currentCategory,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0)
                    };
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                    var label = new Label
                    {
                        Text = setting.Comment,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = new Font("Segoe UI", 10F)
                    };

                    var toggle = new ModernToggleSwitch
                    {
                        Checked = val,
                        Tag = setting.Key,
                        Anchor = AnchorStyles.Right
                    };

                    // La liste déroulante est maintenant toujours disponible
                    // On ne fait plus rien de spécial ici pour la désactiver

                    void OnRowEnter(object s, EventArgs ev) => SetHoverHighlight(settingPanel);
                    void OnRowLeave(object s, EventArgs ev) => ClearHoverHighlight();
                    settingPanel.MouseEnter += OnRowEnter;
                    settingPanel.MouseLeave += OnRowLeave;
                    label.MouseEnter += OnRowEnter;
                    toggle.MouseEnter += OnRowEnter;

                    toggle.GotFocus += (s, ev) => SetFocusHighlight(toggle);
                    toggle.LostFocus += (s, ev) => ClearFocusHighlight();

                    settingPanel.Controls.Add(label, 0, 0);
                    settingPanel.Controls.Add(toggle, 1, 0);
                    _mainPanel.Controls.Add(settingPanel);
                    _navigableControls.Add(toggle);
                }
            }

            var spacer = new Panel { Height = 80 }; // Spacer for bottom padding
            _mainPanel.Controls.Add(spacer);

            _mainPanel.ResumeLayout(true);

            int panelWidth = _mainPanel.ClientSize.Width - _mainPanel.Padding.Horizontal;
            foreach (Control control in _mainPanel.Controls)
            {
                if (control is TableLayoutPanel || (control is Label && control.Font.Bold))
                {
                    control.Width = panelWidth;
                }
            }

            _saveButton.GotFocus += (s, ev) => SetFocusHighlight(_saveButton);
            _saveButton.LostFocus += (s, ev) => ClearFocusHighlight();
            _cancelButton.GotFocus += (s, ev) => SetFocusHighlight(_cancelButton);
            _cancelButton.LostFocus += (s, ev) => ClearFocusHighlight();

            _navigableControls.Add(_saveButton);
            _navigableControls.Add(_cancelButton);

            if (_navigableControls.Any())
            {
                _navigableControls[0].Focus();
            }

            InitializeGamepad();
            this.FormClosing += (s, e) => _gamepadTimer?.Stop();
        }

        private void InitializeGamepad()
        {
            // Timer now always runs to detect hot-plugging.
            _gamepadTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _gamepadTimer.Tick += GamepadTimer_Tick;
            _gamepadTimer.Start();
        }

        private void GamepadTimer_Tick(object sender, EventArgs e)
        {
            // If controller is disconnected, try to find a new one.
            if (_controller == null || !_controller.IsConnected)
            {
                var controllers = new[] { new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three), new Controller(UserIndex.Four) };
                _controller = controllers.FirstOrDefault(c => c.IsConnected);

                if (_controller == null)
                {
                    return; // No controller found, wait for next tick.
                }
            }

            var state = _controller.GetState();
            var gamepad = state.Gamepad;

            bool dpadUp = gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadUp);
            bool dpadDown = gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadDown);
            bool stickUp = gamepad.LeftThumbY > Gamepad.LeftThumbDeadZone;
            bool stickDown = gamepad.LeftThumbY < -Gamepad.LeftThumbDeadZone;

            // Navigation
            if ((dpadUp && !_previousState.Buttons.HasFlag(GamepadButtonFlags.DPadUp)) || (stickUp && _previousState.LeftThumbY <= Gamepad.LeftThumbDeadZone))
            {
                Navigate(true);
            }
            else if ((dpadDown && !_previousState.Buttons.HasFlag(GamepadButtonFlags.DPadDown)) || (stickDown && _previousState.LeftThumbY >= -Gamepad.LeftThumbDeadZone))
            {
                Navigate(false);
            }

            // Actions
            if (gamepad.Buttons.HasFlag(GamepadButtonFlags.A) && !_previousState.Buttons.HasFlag(GamepadButtonFlags.A))
            {
                ActivateFocusedControl();
            }
            if (gamepad.Buttons.HasFlag(GamepadButtonFlags.Start) && !_previousState.Buttons.HasFlag(GamepadButtonFlags.Start))
            {
                _saveButton.PerformClick();
            }
            if (gamepad.Buttons.HasFlag(GamepadButtonFlags.B) && !_previousState.Buttons.HasFlag(GamepadButtonFlags.B))
            {
                _cancelButton.PerformClick();
            }

            _previousState = gamepad;
        }

        private void Navigate(bool up)
        {
            var currentControl = this.ActiveControl;
            if (currentControl == null || !_navigableControls.Contains(currentControl))
            {
                currentControl = _navigableControls.FirstOrDefault();
            }
            if (currentControl == null) return;

            int currentIndex = _navigableControls.IndexOf(currentControl);
            int nextIndex = currentIndex;

            if (up)
            {
                nextIndex = (currentIndex > 0) ? currentIndex - 1 : _navigableControls.Count - 1;
            }
            else // Down
            {
                nextIndex = (currentIndex < _navigableControls.Count - 1) ? currentIndex + 1 : 0;
            }

            _navigableControls[nextIndex].Focus();
        }

        private void SetFocusHighlight(Control control)
        {
            if (control == null) return;
            ClearFocusHighlight();

            if (control is Button button)
            {
                button.BackColor = _focusColor;
                _focusedRow = button.Parent as Panel;
            }
            else
            {
                var rowPanel = control.Parent as Panel;
                if (rowPanel == null) return;
                _focusedRow = rowPanel;
                _focusedRow.BackColor = _focusColor;
            }
            UpdateCategoryLabel(_focusedRow);
        }

        private void ClearFocusHighlight()
        {
            // Reset button colors
            _saveButton.BackColor = Color.FromArgb(138, 43, 226);
            _cancelButton.BackColor = Color.FromArgb(60, 60, 60);

            // Reset panel color
            if (_focusedRow != null && _focusedRow.Tag != null)
            {
                if (_focusedRow != _hoveredRow)
                {
                    _focusedRow.BackColor = Color.Transparent;
                }
            }
            _focusedRow = null;
        }

        private void SetHoverHighlight(Panel rowPanel)
        {
            if (rowPanel == null || rowPanel == _hoveredRow) return;
            ClearHoverHighlight();
            _hoveredRow = rowPanel;
            if (_hoveredRow != _focusedRow)
            {
                _hoveredRow.BackColor = _hoverColor;
            }
            UpdateCategoryLabel(rowPanel);
        }

        private void ClearHoverHighlight()
        {
            if (_hoveredRow != null)
            {
                if (_hoveredRow != _focusedRow)
                {
                    _hoveredRow.BackColor = Color.Transparent;
                }
                _hoveredRow = null;
            }
        }

        private void UpdateCategoryLabel(Panel rowPanel)
        {
            string categoryText = "";
            if (rowPanel != null)
            {
                var comboBox = rowPanel.Controls.OfType<ComboBox>().FirstOrDefault();
                if (comboBox != null && comboBox.Tag.ToString() == "screen_index")
                {
                    string category = rowPanel.Tag as string ?? "Display";
                    int selectedIndex = comboBox.SelectedIndex;
                    if (selectedIndex >= 0 && selectedIndex < Screen.AllScreens.Length)
                    {
                        var screen = Screen.AllScreens[selectedIndex];
                        categoryText = $"Category: {category}. Info: {screen.DeviceName}";
                    }
                    else
                    {
                        categoryText = $"Category: {category}";
                    }
                }
                else if (rowPanel.Tag is string category)
                {
                    categoryText = $"Category: {category}";
                }
                else if (rowPanel.Controls.OfType<Button>().Any())
                {
                    categoryText = "Actions";
                }
            }
            _categoryLabel.Text = categoryText;
        }

        private void ActivateFocusedControl()
        {
            var currentControl = this.ActiveControl;
            if (currentControl is ModernToggleSwitch toggle)
            {
                toggle.Checked = !toggle.Checked;
            }
            else if (currentControl is Button button)
            {
                button.PerformClick();
            }
            else if (currentControl is ComboBox comboBox)
            {
                comboBox.DroppedDown = !comboBox.DroppedDown;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Up) { Navigate(true); return true; }
            if (keyData == Keys.Down) { Navigate(false); return true; }

            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Enter)
            {
                ActivateFocusedControl();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CreateScraperCheckboxes(Config.ConfigOption setting, string category, Panel parentPanel)
        {
            var settingPanel = new TableLayoutPanel
            {
                Height = 40,
                ColumnCount = 2,
                RowCount = 1,
                Tag = category,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Dock = DockStyle.Top
            };
            settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            settingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = setting.Comment.Replace("Media types to scrape from ", "").Replace(", separated by commas.", ""),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10F)
            };

            var checkboxPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Tag = setting.Key,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            var allPossibleMediaTypes = new Dictionary<string, string[]>
            {
                { "hfsplay_scraper_media_types", new[] { "marquee", "image", "fanart", "video" } },
                { "steam_scraper_media_types", new[] { "marquee", "image", "fanart", "video" } },
                { "gog_scraper_media_types", new[] { "image", "thumb" } }
            };

            var mediaTypes = allPossibleMediaTypes.ContainsKey(setting.Key) ? allPossibleMediaTypes[setting.Key] : new string[0];
            var selectedMediaTypes = _config.GetString(setting.Key, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            foreach (var mediaType in mediaTypes)
            {
                var checkBox = new CheckBox
                {
                    Text = mediaType,
                    Tag = mediaType,
                    Checked = selectedMediaTypes.Contains(mediaType),
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = Color.White,
                    AutoSize = true
                };
                checkBox.CheckedChanged += OnScraperCheckboxChanged;
                checkboxPanel.Controls.Add(checkBox);
            }

            settingPanel.Controls.Add(label, 0, 0);
            settingPanel.Controls.Add(checkboxPanel, 1, 0);
            parentPanel.Controls.Add(settingPanel);
        }

        private void OnScraperCheckboxChanged(object sender, EventArgs e)
        {
            var changedCheckbox = (CheckBox)sender;
            if (!changedCheckbox.Checked) return;

            var mediaType = changedCheckbox.Tag.ToString();
            var parentPanel = (FlowLayoutPanel)changedCheckbox.Parent;
            var configKey = parentPanel.Tag.ToString();

            foreach (var control in _mainPanel.Controls)
            {
                if (control is TableLayoutPanel settingPanel)
                {
                    var flowPanel = settingPanel.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
                    if (flowPanel != null && flowPanel.Tag?.ToString() != configKey)
                    {
                        foreach (CheckBox cb in flowPanel.Controls.OfType<CheckBox>())
                        {
                            if (cb.Tag.ToString() == mediaType)
                            {
                                cb.Checked = false;
                            }
                        }
                    }
                }
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            var newSettings = new Dictionary<string, string>();
            CollectSettingsRecursively(_mainPanel, newSettings);
            _config.SaveSettings(newSettings);
            AutoClosingMessageBox.Show(this, "Settings saved successfully!", "Success");
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CollectSettingsRecursively(Control parent, Dictionary<string, string> settings)
        {
            foreach (Control control in parent.Controls)
            {
                if (control.Tag != null)
                {
                    string key = control.Tag.ToString();
                    if (_config.GetSettings().Any(s => s.Key == key))
                    {
                        if (control is ModernToggleSwitch toggle)
                        {
                            settings[key] = toggle.Checked.ToString().ToLower();
                        }
                        else if (control is ComboBox comboBox)
                        {
                            if (key == "screen_index")
                            {
                                settings[key] = comboBox.SelectedIndex.ToString();
                            }
                            else if (key == "xbox_gamepass_region" && comboBox.SelectedItem != null)
                            {
                                string selectedValue = comboBox.SelectedItem.ToString();
                                string regionCode = selectedValue.Length >= 2 ? selectedValue.Substring(0, 2) : "US";
                                settings[key] = regionCode;
                            }
                        }
                        else if (control is FlowLayoutPanel checkboxPanel && key.EndsWith("_scraper_media_types"))
                        {
                            var selectedTypes = checkboxPanel.Controls.OfType<CheckBox>()
                                .Where(cb => cb.Checked)
                                .Select(cb => cb.Tag.ToString());
                            settings[key] = string.Join(",", selectedTypes);
                        }
                    }
                }

                if (control.HasChildren)
                {
                    CollectSettingsRecursively(control, settings);
                }
                if (control is GroupBox)
                {
                    CollectSettingsRecursively(control, settings);
                }
            }
        }

        private void TitlePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStartPoint = new Point(e.X, e.Y);
            }
        }

        private void TitlePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStartPoint != Point.Empty)
            {
                Point p = PointToScreen(new Point(e.X, e.Y));
                Location = new Point(p.X - _dragStartPoint.X, p.Y - _dragStartPoint.Y);
            }
        }

        private void TitlePanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStartPoint = Point.Empty;
            }
        }

        private void UpdateRegionPanelState(bool isEnabled)
        {
            // Ne plus désactiver le panneau de région
            // La méthode est conservée au cas où elle serait utilisée ailleurs
        }
    }
}
