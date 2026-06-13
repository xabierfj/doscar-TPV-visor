using System;
using System.IO.Ports;
using System.Windows;

namespace DoscarVgaDriver
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private VisorWindow _visor;

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Cargar();

            CmbComPort.ItemsSource = SerialPort.GetPortNames();
            CmbComPort.Text = _settings.PortName;
            CmbBaudios.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
            CmbBaudios.SelectedItem = _settings.BaudRate;
            TxtCharacters.Text = _settings.CharsPerLine.ToString();
            Loaded += (_, _) => OpenVisor();
        }

        private void OpenVisor()
        {
            if (_visor != null) return;
            _visor = new VisorWindow(_settings);
            _visor.Closed += (_, _) =>
            {
                _visor = null;
                BtnFullScreen.IsChecked = false;
            };
            _visor.Show();
            BtnFullScreen.IsChecked = false;
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
                BtnFullScreen.IsChecked = false;
                return;
            }
            BtnFullScreen.IsChecked = _visor.ToggleFullScreen();
        }

        private void Visor_Restart(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndSave()) return;
            _visor?.Close();
            OpenVisor();
        }

        protected override void OnClosed(EventArgs e)
        {
            _visor?.Close();
            base.OnClosed(e);
        }
    }
}
