using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX.DirectInput;
using SharpDX.XInput;

namespace GameStoreLibraryManager.Common
{
    public class ExitOverlayForm : Form
    {
        private Button _quitButton;
        private Button _cancelButton;
        private Button _selectedButton;
        
        private enum InputMode { XInput, DInput, None }
        private InputMode _currentMode = InputMode.None;
        private Controller _xinputController;
        private DirectInput _directInput;
        private Joystick _dinputDevice;
        private CancellationTokenSource _cts;
        private bool _inputHandled = false;

        public ExitOverlayForm()
        {
            InitializeComponent();
            
            Load += (s, e) => 
            {
                InitializeGamepad();
                StartPolling();
            };
            FormClosing += (s, e) => StopPolling();
        }

        private void InitializeComponent()
        {
            _quitButton = new Button();
            _cancelButton = new Button();
            var panel = new FlowLayoutPanel();
            var label = new Label();

            // Form properties
            this.Text = "Quitter ?";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ClientSize = new Size(400, 180);
            this.Padding = new Padding(20);
            this.TopMost = true;

            // Label
            label.Text = "Voulez-vous vraiment quitter ?";
            label.Font = new Font("Segoe UI", 14F);
            label.ForeColor = Color.White;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Height = 80;

            // Panel for buttons
            panel.Dock = DockStyle.Bottom;
            panel.Height = 60;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.Padding = new Padding(10);

            // Quit Button
            _quitButton.Text = "Quitter";
            _quitButton.DialogResult = DialogResult.OK;
            _quitButton.Size = new Size(150, 40);
            _quitButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            _quitButton.BackColor = Color.DarkRed;
            _quitButton.ForeColor = Color.White;
            _quitButton.FlatStyle = FlatStyle.Flat;
            _quitButton.FlatAppearance.BorderSize = 0;
            _quitButton.Click += (s, e) => { this.Close(); };

            // Cancel Button
            _cancelButton.Text = "Annuler";
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Size = new Size(150, 40);
            _cancelButton.Font = new Font("Segoe UI", 12F);
            _cancelButton.BackColor = Color.Gray;
            _cancelButton.ForeColor = Color.White;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.FlatAppearance.BorderSize = 0;
            _cancelButton.Click += (s, e) => { this.Close(); };
            
            panel.Controls.Add(_cancelButton);
            panel.Controls.Add(_quitButton);
            this.Controls.Add(label);
            this.Controls.Add(panel);

            this.AcceptButton = _quitButton;
            this.CancelButton = _cancelButton;
            
            _selectedButton = _quitButton;
            UpdateSelectionVisuals();
        }

        private void InitializeGamepad()
        {
            _xinputController = new Controller(UserIndex.One);
            if (_xinputController.IsConnected)
            {
                _currentMode = InputMode.XInput;
            }
            else
            {
                try
                {
                    _directInput = new DirectInput();
                    Guid? joystickGuid = _directInput.GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly).FirstOrDefault()?.InstanceGuid;
                    if (!joystickGuid.HasValue || joystickGuid.Value == Guid.Empty)
                    {
                        joystickGuid = _directInput.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly).FirstOrDefault()?.InstanceGuid;
                    }

                    if (joystickGuid.HasValue && joystickGuid.Value != Guid.Empty)
                    {
                        _dinputDevice = new Joystick(_directInput, joystickGuid.Value);
                        _dinputDevice.SetCooperativeLevel(this.Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        _dinputDevice.Properties.BufferSize = 128;
                        _dinputDevice.Acquire();
                        _currentMode = InputMode.DInput;
                    }
                }
                catch { _currentMode = InputMode.None; }
            }
        }

        private void StartPolling()
        {
            if (_currentMode == InputMode.None) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => PollGamepadInputs(_cts.Token));
        }

        private void StopPolling()
        {
            _cts?.Cancel();
            _dinputDevice?.Unacquire();
            _dinputDevice?.Dispose();
            _directInput?.Dispose();
        }

        private void PollGamepadInputs(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool leftRightPressed = false;
                bool aPressed = false;
                bool bPressed = false;

                if (_currentMode == InputMode.XInput && _xinputController.IsConnected)
                {
                    var state = _xinputController.GetState();
                    var buttons = state.Gamepad.Buttons;
                    if (buttons.HasFlag(GamepadButtonFlags.DPadLeft) || buttons.HasFlag(GamepadButtonFlags.DPadRight) || Math.Abs(state.Gamepad.LeftThumbX) > 16000) leftRightPressed = true;
                    if (buttons.HasFlag(GamepadButtonFlags.A)) aPressed = true;
                    if (buttons.HasFlag(GamepadButtonFlags.B)) bPressed = true;
                }
                else if (_currentMode == InputMode.DInput)
                {
                    try
                    {
                        _dinputDevice.Poll();
                        var data = _dinputDevice.GetBufferedData();
                        foreach (var state in data)
                        {
                            if (state.Offset >= JoystickOffset.Buttons0 && state.Offset <= JoystickOffset.Buttons127 && (state.Value & 0x80) != 0)
                            {
                                int buttonIndex = (int)(state.Offset - JoystickOffset.Buttons0);
                                if (buttonIndex == 0) aPressed = true;
                                if (buttonIndex == 1) bPressed = true;
                            }
                            else if (state.Offset == JoystickOffset.X && (state.Value < 16000 || state.Value > 49000)) leftRightPressed = true;
                            else if (state.Offset == JoystickOffset.PointOfViewControllers0 && (state.Value == 27000 || state.Value == 9000)) leftRightPressed = true;
                        }
                    }
                    catch { Thread.Sleep(100); continue; }
                }

                if (leftRightPressed)
                {
                    if (!_inputHandled)
                    {
                        this.Invoke((MethodInvoker)delegate { ToggleSelection(); });
                        _inputHandled = true;
                    }
                }
                else
                {
                    _inputHandled = false;
                }

                if (aPressed) { this.Invoke((MethodInvoker)delegate { _selectedButton.PerformClick(); }); break; }
                if (bPressed) { this.Invoke((MethodInvoker)delegate { _cancelButton.PerformClick(); }); break; }
                
                Thread.Sleep(120);
            }
        }

        private void ToggleSelection()
        {
            _selectedButton = (_selectedButton == _quitButton) ? _cancelButton : _quitButton;
            UpdateSelectionVisuals();
        }

        private void UpdateSelectionVisuals()
        {
            _quitButton.FlatAppearance.BorderSize = (_selectedButton == _quitButton) ? 2 : 0;
            _quitButton.FlatAppearance.BorderColor = Color.White;
            _cancelButton.FlatAppearance.BorderSize = (_selectedButton == _cancelButton) ? 2 : 0;
            _cancelButton.FlatAppearance.BorderColor = Color.White;
        }
    }
}
