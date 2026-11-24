using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LittleWitchCoop
{
    // Um marcador de um caractere permite editar e selecionar cada emoji como uma unidade.
    // Somente a apresentação usa marcadores. Rede e área de transferência usam Unicode.
    internal sealed class EmojiCatalog : IDisposable
    {
        internal sealed class Glyph
        {
            public char Marker;
            public string Unicode;
            public string File;
        }

        private sealed class Node
        {
            public readonly Dictionary<char, Node> Children = new Dictionary<char, Node>();
            public Glyph Glyph;
        }

        private readonly ZipArchive archive;
        private readonly Node root = new Node();
        private readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>();
        public int Count { get { return glyphs.Count; } }

        public EmojiCatalog(Stream stream)
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = new List<ZipArchiveEntry>(archive.Entries);
            entries.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            foreach (ZipArchiveEntry entry in entries)
            {
                if (!entry.Name.EndsWith(".png", StringComparison.Ordinal)) continue;
                if (glyphs.Count >= 0x1900) throw new InvalidDataException("Catálogo de emojis grande demais.");
                string[] points = Path.GetFileNameWithoutExtension(entry.Name).Split('-');
                var unicode = new StringBuilder();
                foreach (string point in points)
                {
                    int code = int.Parse(point, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (code != 0xFE0F) unicode.Append(char.ConvertFromUtf32(code));
                }
                string value = unicode.ToString();
                var glyph = new Glyph { Marker = (char)(0xE000 + glyphs.Count), Unicode = value, File = entry.FullName };
                if (value.Length == 1) glyph.Unicode += "\uFE0F";
                glyphs.Add(glyph.Marker, glyph);
                Node node = root;
                foreach (char character in value)
                {
                    Node child;
                    if (!node.Children.TryGetValue(character, out child))
                    {
                        child = new Node();
                        node.Children.Add(character, child);
                    }
                    node = child;
                }
                node.Glyph = glyph;
            }
        }

        public string Encode(string value)
        {
            var encoded = new StringBuilder();
            for (int i = 0; i < value.Length;)
            {
                Node node = root;
                Glyph match = null;
                int end = i;
                for (int j = i; j < value.Length && j - i < 64; j++)
                {
                    if (value[j] == '\uFE0F' && j > i)
                    {
                        if (node.Glyph != null) { match = node.Glyph; end = j + 1; }
                        continue;
                    }
                    Node child;
                    if (!node.Children.TryGetValue(value[j], out child)) break;
                    node = child;
                    if (node.Glyph != null) { match = node.Glyph; end = j + 1; }
                }
                // Respeita a apresentação textual explicitamente solicitada pelo Unicode.
                if (match != null && (end >= value.Length || value[end] != '\uFE0E'))
                {
                    encoded.Append(match.Marker);
                    i = end;
                }
                else encoded.Append(value[i++]);
            }
            return encoded.ToString();
        }

        public string Decode(string value)
        {
            var decoded = new StringBuilder();
            foreach (char character in value)
            {
                Glyph glyph;
                decoded.Append(glyphs.TryGetValue(character, out glyph) ? glyph.Unicode : character.ToString());
            }
            return decoded.ToString();
        }

        public bool TryGet(char marker, out Glyph glyph) { return glyphs.TryGetValue(marker, out glyph); }

        public byte[] ImageBytes(Glyph glyph)
        {
            using (Stream input = archive.GetEntry(glyph.File).Open())
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        public void Dispose() { archive.Dispose(); }
    }
}

