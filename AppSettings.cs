using System;
using System.IO;
using System.Text.Json;

namespace DoscarVgaDriver
{
    public class AppSettings
    {
        public string PortName { get; set; } = "COM11";
        public int BaudRate { get; set; } = 9600;
        public int CharsPerLine { get; set; } = 20;
        public string HeaderText { get; set; } = "¡Gracias por su visita!";

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
