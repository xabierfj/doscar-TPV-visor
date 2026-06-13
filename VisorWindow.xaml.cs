using System;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace DoscarVgaDriver
{
    public partial class VisorWindow : Window
    {
        // Doscar frames every command as 0x04 0x01 <cmd> 0x0D, followed by a 20-char padded text payload for cursor commands (P1 = line 1, PE = line 2).
        private const string FramePrefix = "\u0004\u0001";
        private const int PayloadLength = 20;

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

        // Fullscreen borderless on the secondary monitor (the 7" customer panel).
        // With a single monitor (development) it stays a normal window.
        private void ConfigureKioskMode()
        {
            var secondary = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary);
            if (secondary == null) return;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = secondary.Bounds.Left;
            Top = secondary.Bounds.Top;
            Width = secondary.Bounds.Width;
            Height = secondary.Bounds.Height;
            Loaded += (_, _) => WindowState = WindowState.Maximized;

            SetThreadExecutionState(EsContinuous | EsDisplayRequired);
        }

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint EsContinuous = 0x80000000;
        private const uint EsDisplayRequired = 0x00000002;

        // Dev helper: toggle borderless fullscreen on the current monitor so the
        // panel can be previewed without a secondary display. Returns the new state.
        private bool _isFullScreen;
        private WindowStyle _windowedStyle;
        private ResizeMode _windowedResizeMode;
        private WindowState _windowedState;

        public bool ToggleFullScreen()
        {
            if (_isFullScreen)
            {
                WindowStyle = _windowedStyle;
                ResizeMode = _windowedResizeMode;
                WindowState = _windowedState;
            }
            else
            {
                _windowedStyle = WindowStyle;
                _windowedResizeMode = ResizeMode;
                _windowedState = WindowState;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }

            _isFullScreen = !_isFullScreen;
            return _isFullScreen;
        }

        private void PortInitialisation()
        {
            _serialPort = new SerialPort(_settings.PortName, _settings.BaudRate, Parity.None, 8, StopBits.One);
            _serialPort.DataReceived += Port_DataReceived;
            try
            {
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
                _bufferBuilder.Append(_serialPort.ReadExisting());
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
                    var cr = content.IndexOf('\r');
                    if (cr < 0 || content.Length < cr + 1 + PayloadLength) return;
                    segment = content;
                    _bufferBuilder.Clear();
                }

                HandleSegment(segment);
            }
        }

        private void HandleSegment(string segment)
        {
            var cr = segment.IndexOf('\r');
            if (cr < 0) return;

            var command = segment.Substring(FramePrefix.Length, cr - FramePrefix.Length);
            var payload = segment.Substring(cr + 1).TrimEnd();

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
