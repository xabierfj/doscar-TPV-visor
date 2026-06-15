using System;
using System.IO;
using System.Text.Json;

namespace DoscarVgaDriver
{
    public class AppSettings
    {
        public string PortName { get; set; } = "COM10";
        public int BaudRate { get; set; } = 9600;
        public int CharsPerLine { get; set; } = 20;

        // Welcome header shown on the idle panel.
        public string HeaderText { get; set; } = "ONGI ETORRI!";
        // Symbol appended to prices and the total.
        public string CurrencySymbol { get; set; } = "€";
        // Payload of line 1 (P1) that switches the visor to a given panel.
        public string TotalKeyword { get; set; } = "TOTAL";
        public string IdleKeyword { get; set; } = "Gracias";
        // When true, the raw frame bytes are logged to the console as hex.
        public bool EnableDebugLog { get; set; } = false;

        // Unlocks advanced/developer-only fields (panel keywords, etc.) in the config UI.
        public bool DevMode { get; set; } = false;

        // Index into Screen.AllScreens for the visor window. -1 = auto (first non-primary).
        public int TargetMonitor { get; set; } = -1;
        // Open the visor in fullscreen on launch.
        public bool StartFullScreen { get; set; } = true;

        // Serial parity: "None", "Even", "Odd", "Mark", "Space".
        public string Parity { get; set; } = "None";
        // Text encoding used to decode the serial stream.
        public string Encoding { get; set; } = "ISO-8859-1";

        // Per-user, always writable. The exe may live in Program Files, where
        // writing next to it would fail, so config lives in %AppData%\Doscar.
        private static string RutaArchivo
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Doscar");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.json");
            }
        }

        // Older builds stored config next to the exe; read it once for migration.
        private static string RutaLegado => Path.Combine(AppContext.BaseDirectory, "config.json");

        public static AppSettings Cargar()
        {
            foreach (var ruta in new[] { RutaArchivo, RutaLegado })
            {
                try
                {
                    if (File.Exists(ruta))
                        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ruta)) ?? new AppSettings();
                }
                catch
                {
                    // Config ilegible o corrupta: probar la siguiente ubicación / valores por defecto.
                }
            }
            return new AppSettings();
        }

        public bool Guardar()
        {
            try
            {
                File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Error guardando configuración: {ex.Message}");
                return false;
            }
        }
    }
}
