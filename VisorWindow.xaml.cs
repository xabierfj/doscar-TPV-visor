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
        // Doscar frames every command as 0x04 0x01 <cmd> 0x17, followed by a 20-char padded text payload for cursor commands (P1 = line 1, PE = line 2).
        // The separator between command and payload is ETB (0x17), not CR (0x0D).
        private const string FramePrefix = "\u0004\u0001";
        private const int PayloadLength = 20;
        private const char CommandSeparator = '';

        private SerialPort _serialPort;
        private AppSettings _settings;
        private readonly StringBuilder _bufferBuilder = new StringBuilder();
        private string _line1 = string.Empty;

        public VisorWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            ConfigureKioskMode();
            PortInitialisation();
        }

        private void ConfigureKioskMode()
        {
            var secondary = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary);
            if (secondary == null) return;

            ShowInTaskbar = false;
            SetThreadExecutionState(EsContinuous | EsDisplayRequired);
            Loaded += (_, _) => EnterFullScreen(secondary);
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
            _serialPort = new SerialPort(_settings.PortName, _settings.BaudRate, Parity.None, 8, StopBits.One);
            _serialPort.Encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
            _serialPort.DataReceived += Port_DataReceived;
            try
            {
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void PortClose()
        {
            if (_serialPort == null) return;
            _serialPort.DataReceived -= Port_DataReceived;
            if (_serialPort.IsOpen) _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
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

                Console.WriteLine(BitConverter.ToString(bytes, 0, read));
                _bufferBuilder.Append(_serialPort.Encoding.GetString(bytes, 0, read));
                ProcessBuffer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
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
                Console.WriteLine($"Visor: [{line1}] / [{line2}]");
                if (line1 == "TOTAL")
                {
                    TxtTotalPrice.Text = $"{line2} €";
                    SetPanel(TotalPanel);
                }
                else if (line1 == "Gracias" || (line1.Length == 0 && line2.Length == 0))
                {
                    SetPanel(WelcomePanel);
                }
                else
                {
                    TxtProduct.Text = line1;
                    TxtPrize.Text = line2;
                    SetPanel(ProductPanel);
                }
            });
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
