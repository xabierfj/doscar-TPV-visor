using System;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DoscarVgaDriver
{
    public partial class VisorWindow : Window
    {
        // Doscar frames every command as 0x04 0x01 <cmd> 0x17, followed by a 20-char
        // padded text payload for cursor commands (P1 = line 1, PE = line 2).
        // The separator between command and payload is ETB (0x17), not CR (0x0D).
        private const int PayloadLength = 20;
        private const string FramePrefix = "\u0004\u0001";
        private const char CommandSeparator = '';

        private SerialPort _serialPort;
        private AppSettings _settings;
        private readonly StringBuilder _bufferBuilder = new StringBuilder();
        private string _line1 = string.Empty;

        // The product currently shown; tracked so the pole display's continuous
        // re-sends of the same line don't trigger redundant UI updates.
        private string _lastItemKey = string.Empty;

        private readonly object _portLock = new object();
        private System.Windows.Threading.DispatcherTimer _reconnectTimer;
        private bool _connected;

        // Raised on the UI thread whenever the serial connection opens or drops.
        public event Action<bool> ConnectionChanged;
        public bool IsConnected => _connected;
        public DateTime? LastFrameUtc { get; private set; }

        public VisorWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            TxtWelcome.Text = _settings.HeaderText;
            ConfigureKioskMode();
            PortInitialisation();
        }

        private void ConfigureKioskMode()
        {
            if (!_settings.StartFullScreen) return;

            var target = ResolveTargetScreen();
            if (target == null) return;

            ShowInTaskbar = false;
            SetThreadExecutionState(EsContinuous | EsDisplayRequired);
            Loaded += (_, _) => EnterFullScreen(target);
        }

        // Configured monitor if valid, otherwise the first non-primary screen, otherwise the primary.
        private System.Windows.Forms.Screen ResolveTargetScreen()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (_settings.TargetMonitor >= 0 && _settings.TargetMonitor < screens.Length)
                return screens[_settings.TargetMonitor];
            return screens.FirstOrDefault(s => !s.Primary) ?? System.Windows.Forms.Screen.PrimaryScreen;
        }

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint EsContinuous = 0x80000000;
        private const uint EsDisplayRequired = 0x00000002;
        
        private bool _isFullScreen;
        private WindowStyle _windowedStyle;
        private ResizeMode _windowedResizeMode;
        private bool _windowedTopmost;
        private double _windowedLeft, _windowedTop, _windowedWidth, _windowedHeight;

        public bool IsFullScreen => _isFullScreen;

        public bool ToggleFullScreen()
        {
            if (_isFullScreen)
                ExitFullScreen();
            else
                EnterFullScreen(System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle));
            return _isFullScreen;
        }

        private void EnterFullScreen(System.Windows.Forms.Screen screen)
        {
            if (_isFullScreen) return;

            _windowedStyle = WindowStyle;
            _windowedResizeMode = ResizeMode;
            _windowedTopmost = Topmost;
            _windowedLeft = Left;
            _windowedTop = Top;
            _windowedWidth = Width;
            _windowedHeight = Height;

            // Explicit bounds are more reliable than WindowState.Maximized for a
            // borderless window. Topmost + Activate brings it above the config window.
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
            Topmost = true;
            Activate();

            _isFullScreen = true;
        }

        private void ExitFullScreen()
        {
            if (!_isFullScreen) return;

            WindowStyle = _windowedStyle;
            ResizeMode = _windowedResizeMode;
            Topmost = _windowedTopmost;
            WindowState = WindowState.Normal;
            Left = _windowedLeft;
            Top = _windowedTop;
            Width = _windowedWidth;
            Height = _windowedHeight;

            _isFullScreen = false;
        }

        private void PortInitialisation()
        {
            // Poll for the port so the display recovers on its own after a cable
            // drop or POS reboot instead of needing the app restarted.
            _reconnectTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _reconnectTimer.Tick += (_, _) =>
            {
                if (_serialPort == null || !_serialPort.IsOpen) TryOpenPort();
            };
            _reconnectTimer.Start();
            TryOpenPort();
        }

        private void TryOpenPort()
        {
            lock (_portLock)
            {
                if (_serialPort != null && _serialPort.IsOpen) return;
                try
                {
                    _serialPort = new SerialPort(_settings.PortName, _settings.BaudRate, ParseParity(_settings.Parity), 8, StopBits.One);
                    _serialPort.Encoding = ResolveEncoding(_settings.Encoding);
                    _serialPort.DataReceived += Port_DataReceived;
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    _serialPort.Open();
                    DebugLog.Write($"Puerto {_settings.PortName} abierto.");
                    SetConnected(true);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"Error abriendo el puerto: {ex.Message}");
                    DisposePort();
                    SetConnected(false);
                }
            }
        }

        private void DisposePort()
        {
            lock (_portLock)
            {
                if (_serialPort == null) return;
                _serialPort.DataReceived -= Port_DataReceived;
                try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        private void SetConnected(bool connected)
        {
            if (_connected == connected) return;
            _connected = connected;
            Dispatcher.InvokeAsync(() => ConnectionChanged?.Invoke(connected));
        }

        private static Parity ParseParity(string value)
            => Enum.TryParse<Parity>(value, true, out var parity) ? parity : Parity.None;

        private static System.Text.Encoding ResolveEncoding(string name)
        {
            try { return System.Text.Encoding.GetEncoding(name); }
            catch { return System.Text.Encoding.GetEncoding("ISO-8859-1"); }
        }

        private void PortClose()
        {
            _reconnectTimer?.Stop();
            _reconnectTimer = null;
            DisposePort();
            SetConnected(false);
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null) return;
            try
            {
                var count = _serialPort.BytesToRead;
                if (count == 0) return;

                var bytes = new byte[count];
                var read = _serialPort.Read(bytes, 0, count);
                LastFrameUtc = DateTime.UtcNow;

                if (_settings.EnableDebugLog)
                    DebugLog.Write(BitConverter.ToString(bytes, 0, read));
                _bufferBuilder.Append(_serialPort.Encoding.GetString(bytes, 0, read));
                ProcessBuffer();
            }
            catch (Exception ex)
            {
                // A read failure usually means the adapter was unplugged; drop the
                // port and let the reconnect timer reopen it.
                DebugLog.Write($"Conexión perdida: {ex.Message}");
                DisposePort();
                SetConnected(false);
            }
        }

        private void ProcessBuffer()
        {
            while (true)
            {
                var content = _bufferBuilder.ToString();
                var start = content.IndexOf(FramePrefix, StringComparison.Ordinal);
                if (start < 0) return;
                if (start > 0)
                {
                    _bufferBuilder.Remove(0, start);
                    continue;
                }

                var next = content.IndexOf(FramePrefix, FramePrefix.Length, StringComparison.Ordinal);
                string segment;
                if (next >= 0)
                {
                    segment = content.Substring(0, next);
                    _bufferBuilder.Remove(0, next);
                }
                else
                {
                    // Last segment: only complete once the padded payload is in.
                    var sep = content.IndexOf(CommandSeparator);
                    if (sep < 0 || content.Length < sep + 1 + PayloadLength) return;
                    segment = content;
                    _bufferBuilder.Clear();
                }

                HandleSegment(segment);
            }
        }

        private void HandleSegment(string segment)
        {
            var sep = segment.IndexOf(CommandSeparator);
            if (sep < 0) return;

            var command = segment.Substring(FramePrefix.Length, sep - FramePrefix.Length);
            var payload = segment.Substring(sep + 1).TrimEnd();

            switch (command)
            {
                case "P1":
                    _line1 = payload;
                    break;
                case "PE":
                    ShowScreen(_line1, payload);
                    break;
                // C1X (clear) and I7 (charset): nothing to render.
            }
        }

        private void ShowScreen(string line1, string line2)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_settings.EnableDebugLog)
                    DebugLog.Write($"Visor: [{line1}] / [{line2}]");
                if (line1 == _settings.TotalKeyword)
                {
                    TxtTotalPrice.Text = $"{line2} {_settings.CurrencySymbol}";
                    SetPanel(TotalPanel);
                }
                else if (line1 == _settings.IdleKeyword || (line1.Length == 0 && line2.Length == 0))
                {
                    ResetReceipt();
                    SetPanel(WelcomePanel);
                }
                else
                {
                    AddItem(line1, line2);
                    SetPanel(ProductPanel);
                }
            });
        }

        // Update the product only when it differs from the last one, so the pole
        // display's continuous re-sends of the same line don't cause flicker.
        private void AddItem(string name, string price)
        {
            var key = name + "" + price;
            if (key == _lastItemKey) return;
            _lastItemKey = key;
            TxtProductName.Text = name;
            TxtProductPrice.Text = $"{price} {_settings.CurrencySymbol}";
        }

        private void ResetReceipt()
        {
            TxtProductName.Text = string.Empty;
            TxtProductPrice.Text = string.Empty;
            _lastItemKey = string.Empty;
        }

        private void SetPanel(UIElement panel)
        {
            WelcomePanel.Visibility = panel == WelcomePanel ? Visibility.Visible : Visibility.Collapsed;
            ProductPanel.Visibility = panel == ProductPanel ? Visibility.Visible : Visibility.Collapsed;
            TotalPanel.Visibility = panel == TotalPanel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isFullScreen) ToggleFullScreen();
                else Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SetThreadExecutionState(EsContinuous);
            PortClose();
            base.OnClosed(e);
        }
    }
}
