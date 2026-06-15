using System;

namespace DoscarVgaDriver
{
    // Simple in-process pub/sub so the visor can stream debug output to the
    // console window without holding a direct reference to it.
    public static class DebugLog
    {
        public static event Action<string> MessageLogged;

        public static void Write(string message) => MessageLogged?.Invoke(message);
    }
}
