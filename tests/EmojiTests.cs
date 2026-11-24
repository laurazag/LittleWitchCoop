using System;
using System.IO;
using LittleWitchCoop;

class EmojiTests
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static int Main()
    {
        using (var catalog = new EmojiCatalog(File.OpenRead("assets/emojis.zip")))
        {
            Require(catalog.Count == 50, "The catalog must contain exactly 50 emoji images");
            foreach (string emoji in new[] { "😀", "👋", "👍", "❤️", "✨", "🥰", "🌸", "🍄", "🧙" })
            {
                string encoded = catalog.Encode(emoji);
                Require(encoded.Length == 1, "Emoji was split: " + emoji);
                Require(catalog.Decode(encoded).Replace("\uFE0F", "") == emoji.Replace("\uFE0F", ""), "Copy changed the emoji");
            }
            string phrase = "Oi, Ellie 👋! Coração ❤️ e flor 🌸. Acentos e espaços.";
            Require(catalog.Decode(catalog.Encode(phrase)).Replace("\uFE0F", "") == phrase.Replace("\uFE0F", ""), "Mixed text changed");
            Require(catalog.Encode("<b>floresta</b> 123 abc") == "<b>floresta</b> 123 abc", "Ordinary text changed");
            Require(catalog.Encode("❤\uFE0E") == "❤\uFE0E", "Explicit text presentation was replaced");
            for (int i = 0; i < catalog.Count; i++)
            {
                EmojiCatalog.Glyph glyph;
                Require(catalog.TryGet((char)(0xE000 + i), out glyph), "Missing catalog entry");
                Require(catalog.Encode(glyph.Unicode) == glyph.Marker.ToString(), "Ambiguous catalog entry " + glyph.File);
                byte[] image = catalog.ImageBytes(glyph);
                Require(image.Length > 8 && image[0] == 137 && image[1] == 80 && image[2] == 78 && image[3] == 71, "Invalid PNG");
            }
            Console.WriteLine("PASS exactly 50 PNGs, Unicode copy roundtrip, variation selectors and ordinary text");
        }
        return 0;
    }
}

