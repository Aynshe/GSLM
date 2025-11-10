using SharpDX.XInput;
using SharpDX.DirectInput;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GameStoreLibraryManager.Common
{
    public class GamepadListener : IDisposable
    {
        public event EventHandler ComboDetected;

        private enum InputMode { XInput, DInput, None }
        private InputMode _currentMode = InputMode.None;

        private Controller _xinputController;
        private DirectInput _directInput;
        private Joystick _dinputDevice;

        private bool _isRunning = false;
        private Task _pollingTask;
        private CancellationTokenSource _cancellationTokenSource;

        private bool _selectPressed = false;
        private DateTime _lastSelectPressTime = DateTime.MinValue;
        private int _selectPressCount = 0;

        private const int DoubleClickTimeMs = 500; // 500ms for a double click
        private const int LongPressTimeMs = 1000; // 1 second for a long press

        public GamepadListener() { }

        public void Initialize(IntPtr windowHandle)
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
                        _dinputDevice.SetCooperativeLevel(windowHandle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        _dinputDevice.Properties.BufferSize = 128;
                        _dinputDevice.Acquire();
                        _currentMode = InputMode.DInput;
                    }
                }
                catch
                {
                    _currentMode = InputMode.None;
                }
            }
        }

        public void Start()
        {
            if (_isRunning || _currentMode == InputMode.None) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _pollingTask = Task.Run(() =>
            {
                if (_currentMode == InputMode.XInput) PollXInput(_cancellationTokenSource.Token);
                else if (_currentMode == InputMode.DInput) PollDInput(_cancellationTokenSource.Token);
            });
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _cancellationTokenSource?.Cancel();
            // _pollingTask?.Wait(); // This is a blocking call and can cause a deadlock on the UI thread.
            _isRunning = false;

            _dinputDevice?.Unacquire();
            _dinputDevice?.Dispose();
            _directInput?.Dispose();
        }

        private void PollXInput(CancellationToken token)
        {
            DateTime startPressedTime = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                if (!_xinputController.IsConnected)
                {
                    Thread.Sleep(1000); 
                    continue;
                }

                var state = _xinputController.GetState();
                var buttons = state.Gamepad.Buttons;

                if (buttons.HasFlag(GamepadButtonFlags.Back))
                {
                    if (!_selectPressed)
                    {
                        _selectPressed = true;
                        var now = DateTime.Now;
                        if ((now - _lastSelectPressTime).TotalMilliseconds < DoubleClickTimeMs)
                        {
                            _selectPressCount++;
                        }
                        else
                        {
                            _selectPressCount = 1;
                        }
                        _lastSelectPressTime = now;
                    }
                }
                else
                {
                    _selectPressed = false;
                }

                // Start button logic
                if (_selectPressCount >= 2) // After a double click on select
                {
                    if (buttons.HasFlag(GamepadButtonFlags.Start))
                    {
                        if (startPressedTime == DateTime.MinValue)
                        {
                            startPressedTime = DateTime.Now;
                        }
                        else if ((DateTime.Now - startPressedTime).TotalMilliseconds >= LongPressTimeMs)
                        {
                            // Combo detected
                            ComboDetected?.Invoke(this, EventArgs.Empty);
                            // Reset state to avoid repeated triggers
                            _selectPressCount = 0;
                            startPressedTime = DateTime.MinValue;
                        }
                    }
                    else
                    {
                        startPressedTime = DateTime.MinValue; // Reset if Start is released
                    }
                }

                // Reset select count if time window is exceeded
                if (_selectPressCount > 0 && (DateTime.Now - _lastSelectPressTime).TotalMilliseconds >= DoubleClickTimeMs)
                {
                     // Don't reset if we are in the middle of a potential long press
                    if(startPressedTime == DateTime.MinValue)
                    {
                        _selectPressCount = 0;
                    }
                }

                Thread.Sleep(50); 
            }
        }

        private void PollDInput(CancellationToken token)
        {
            DateTime startPressedTime = DateTime.MinValue;
            const int selectButtonIndex = 8;
            const int startButtonIndex = 9;
            const int aButtonIndex = 0;
            const int bButtonIndex = 1;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _dinputDevice.Poll();
                    var joyState = _dinputDevice.GetCurrentState();

                    bool isSelectDown = joyState.Buttons[selectButtonIndex];
                    bool isStartDown = joyState.Buttons[startButtonIndex];

                    // Select button logic
                    if (isSelectDown)
                    {
                        if (!_selectPressed)
                        {
                            _selectPressed = true;
                            var now = DateTime.Now;
                            if ((now - _lastSelectPressTime).TotalMilliseconds < DoubleClickTimeMs) _selectPressCount++;
                            else _selectPressCount = 1;
                            _lastSelectPressTime = now;
                        }
                    }
                    else
                    {
                        _selectPressed = false;
                    }

                    // Start button logic
                    if (_selectPressCount >= 2)
                    {
                        if (isStartDown)
                        {
                            if (startPressedTime == DateTime.MinValue) startPressedTime = DateTime.Now;
                            else if ((DateTime.Now - startPressedTime).TotalMilliseconds >= LongPressTimeMs)
                            {
                                ComboDetected?.Invoke(this, EventArgs.Empty);
                                _selectPressCount = 0;
                                startPressedTime = DateTime.MinValue;
                            }
                        }
                        else
                        {
                            startPressedTime = DateTime.MinValue;
                        }
                    }

                    if (_selectPressCount > 0 && (DateTime.Now - _lastSelectPressTime).TotalMilliseconds >= DoubleClickTimeMs)
                    {
                        if (startPressedTime == DateTime.MinValue) _selectPressCount = 0;
                    }

                }
                catch (SharpDX.SharpDXException)
                {
                    try { _dinputDevice.Acquire(); } catch { } // Try to reacquire
                    Thread.Sleep(1000);
                    continue;
                }
                Thread.Sleep(50);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
