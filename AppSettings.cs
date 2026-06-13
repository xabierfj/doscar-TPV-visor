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

        private static string RutaArchivo => Path.Combine(AppContext.BaseDirectory, "config.json");

        public static AppSettings Cargar()
        {
            try
            {
                if (File.Exists(RutaArchivo))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(RutaArchivo)) ?? new AppSettings();
            }
            catch
            {
                // Config ilegible o corrupta: arrancar con valores por defecto
            }
            return new AppSettings();
        }

        public void Guardar()
        {
            File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
