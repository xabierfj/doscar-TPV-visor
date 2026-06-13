using System;
using System.IO.Ports;
using System.Windows;

namespace DoscarVgaDriver
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private VisorWindow _visor;
        private DebugConsoleWindow _debugConsole;

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Cargar();

            CmbComPort.ItemsSource = SerialPort.GetPortNames();
            CmbComPort.Text = _settings.PortName;
            CmbBaudios.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
            CmbBaudios.SelectedItem = _settings.BaudRate;
            TxtCharacters.Text = _settings.CharsPerLine.ToString();
            TxtHeader.Text = _settings.HeaderText;
            TxtCurrency.Text = _settings.CurrencySymbol;

            CmbParity.ItemsSource = new[] { "None", "Even", "Odd", "Mark", "Space" };
            CmbParity.SelectedItem = _settings.Parity;
            CmbEncoding.ItemsSource = new[] { "ASCII", "ISO-8859-1", "UTF-8" };
            CmbEncoding.SelectedItem = _settings.Encoding;

            var monitors = BuildMonitorList();
            CmbMonitor.ItemsSource = monitors;
            // Item 0 is "Auto" (TargetMonitor -1); screen N maps to item N+1.
            var monitorIndex = _settings.TargetMonitor + 1;
            CmbMonitor.SelectedIndex = monitorIndex >= 0 && monitorIndex < monitors.Length ? monitorIndex : 0;

            ChkStartFullScreen.IsChecked = _settings.StartFullScreen;
            ChkDevMode.IsChecked = _settings.DevMode;

            TxtTotalKeyword.Text = _settings.TotalKeyword;
            TxtIdleKeyword.Text = _settings.IdleKeyword;
            ChkDebugLog.IsChecked = _settings.EnableDebugLog;
            Loaded += (_, _) =>
            {
                OpenVisor();
                if (_settings.EnableDebugLog) OpenDebugConsole();
            };
        }

        private static string[] BuildMonitorList()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var items = new string[screens.Length + 1];
            items[0] = "Automático";
            for (int i = 0; i < screens.Length; i++)
            {
                var b = screens[i].Bounds;
                var tag = screens[i].Primary ? " (principal)" : "";
                items[i + 1] = $"{i + 1}: {b.Width}x{b.Height}{tag}";
            }
            return items;
        }

        private void OpenVisor()
        {
            if (_visor != null) return;
            _visor = new VisorWindow(_settings);
            _visor.Closed += (_, _) =>
            {
                _visor = null;
                ChkFullScreen.IsChecked = false;
            };
            // Kiosk mode auto-engages fullscreen on the visor's Loaded (secondary
            // monitor present), so mirror the real state once it has loaded.
            _visor.Loaded += (_, _) => ChkFullScreen.IsChecked = _visor?.IsFullScreen ?? false;
            _visor.Show();
        }

        private bool ValidateAndSave()
        {
            if (string.IsNullOrWhiteSpace(CmbComPort.Text))
            {
                MessageBox.Show("Selecciona un puerto COM.", "Configuración");
                return false;
            }
            if (!int.TryParse(TxtCharacters.Text, out int characters) || characters < 1)
            {
                MessageBox.Show("Caracteres por línea debe ser un número mayor que 0.", "Configuración");
                return false;
            }

            _settings.PortName = CmbComPort.Text.Trim();
            if (CmbBaudios.SelectedItem is int baudios) _settings.BaudRate = baudios;
            _settings.CharsPerLine = characters;
            _settings.HeaderText = TxtHeader.Text;
            _settings.CurrencySymbol = TxtCurrency.Text;
            if (CmbParity.SelectedItem is string parity) _settings.Parity = parity;
            if (CmbEncoding.SelectedItem is string encoding) _settings.Encoding = encoding;
            // Item 0 is "Auto" (-1); screen N is at item N+1.
            _settings.TargetMonitor = CmbMonitor.SelectedIndex - 1;
            _settings.StartFullScreen = ChkStartFullScreen.IsChecked == true;
            _settings.DevMode = ChkDevMode.IsChecked == true;
            _settings.TotalKeyword = TxtTotalKeyword.Text.Trim();
            _settings.IdleKeyword = TxtIdleKeyword.Text.Trim();
            _settings.EnableDebugLog = ChkDebugLog.IsChecked == true;
            _settings.Guardar();
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndSave();
        }
        

        private void FullScreen_Toggle(object sender, RoutedEventArgs e)
        {
            if (_visor == null)
            {
                ChkFullScreen.IsChecked = false;
                return;
            }
            ChkFullScreen.IsChecked = _visor.ToggleFullScreen();
        }

        private void DebugLog_Toggle(object sender, RoutedEventArgs e)
        {
            _settings.EnableDebugLog = ChkDebugLog.IsChecked == true;
            if (_settings.EnableDebugLog)
                OpenDebugConsole();
            else
                _debugConsole?.Close();
        }

        private void OpenDebugConsole()
        {
            if (_debugConsole != null)
            {
                _debugConsole.Activate();
                return;
            }
            _debugConsole = new DebugConsoleWindow { Owner = this };
            // Closing the console window mirrors back to "logging off".
            _debugConsole.Closed += (_, _) =>
            {
                _debugConsole = null;
                ChkDebugLog.IsChecked = false;
                _settings.EnableDebugLog = false;
            };
            _debugConsole.Show();
        }

        private void Visor_Restart(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndSave()) return;
            _visor?.Close();
            OpenVisor();
        }

        protected override void OnClosed(EventArgs e)
        {
            _debugConsole?.Close();
            _visor?.Close();
            base.OnClosed(e);
        }
    }
}
