using System;

namespace LittleWitchCoop
{
    internal static class EmoteChoices
    {
        public static readonly string[] Icons = { "❤️", "😂", "👋", "👏", "😱", "😴", "🌸", "✨" };
        public static readonly string[] Names = { "Carinho", "Risada", "Oi!", "Palminhas", "Surpresa", "Soninho", "Uma flor", "Magia" };
        public static readonly string[] SoundFiles = { "carinho.wav", "risada.wav", "oi.wav", "palminhas.wav", "surpresa.wav", "soninho.wav", "flor.wav", "magia.wav" };

        public static bool Contains(string value)
        {
            return Array.IndexOf(Icons, value) >= 0;
        }
    }
}


