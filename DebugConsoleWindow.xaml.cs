using System;
using System.Windows;

namespace DoscarVgaDriver
{
    public partial class DebugConsoleWindow : Window
    {
        public DebugConsoleWindow()
        {
            InitializeComponent();
            DebugLog.MessageLogged += OnMessageLogged;
        }

        // DebugLog.Write is raised from the serial background thread, so marshal to the UI.
        private void OnMessageLogged(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtOutput.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
                if (ChkAutoScroll.IsChecked == true) TxtOutput.ScrollToEnd();
            });
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => TxtOutput.Clear();

        protected override void OnClosed(EventArgs e)
        {
            DebugLog.MessageLogged -= OnMessageLogged;
            base.OnClosed(e);
        }
    }
}
