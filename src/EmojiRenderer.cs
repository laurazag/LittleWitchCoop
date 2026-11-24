using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LittleWitchCoop
{
    internal sealed class EmojiRenderer : IDisposable
    {
        private readonly EmojiCatalog catalog;
        private readonly Dictionary<char, Texture2D> textures = new Dictionary<char, Texture2D>();
        private readonly Queue<char> loaded = new Queue<char>();
        private const char Padding = '\u2003';

        public EmojiRenderer()
        {
            catalog = new EmojiCatalog(Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleWitchCoop.Emojis"));
        }

        public string Encode(string value)
        {
            string markers = catalog.Encode(value);
            var display = new StringBuilder();
            foreach (char character in markers)
            {
                display.Append(character);
                EmojiCatalog.Glyph glyph;
                if (catalog.TryGet(character, out glyph)) display.Append(Padding);
            }
            return display.ToString();
        }

        public string Decode(string value)
        {
            var markers = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                markers.Append(value[i]);
                EmojiCatalog.Glyph glyph;
                if (catalog.TryGet(value[i], out glyph) && i + 1 < value.Length && value[i + 1] == Padding) i++;
            }
            return catalog.Decode(markers.ToString());
        }

        private Texture2D Texture(EmojiCatalog.Glyph glyph)
        {
            Texture2D texture;
            if (textures.TryGetValue(glyph.Marker, out texture)) return texture;
            var source = new Texture2D(2, 2);
            ImageConversion.LoadImage(source, catalog.ImageBytes(glyph), false);
            const int size = 16;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = source.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);
                    pixel.a = pixel.a >= 0.4f ? 1f : 0f;
                    pixels[y * size + x] = pixel;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            UnityEngine.Object.Destroy(source);
            texture.filterMode = FilterMode.Point;
            textures.Add(glyph.Marker, texture);
            loaded.Enqueue(glyph.Marker);
            if (loaded.Count > 128)
            {
                char oldest = loaded.Dequeue();
                UnityEngine.Object.Destroy(textures[oldest]);
                textures.Remove(oldest);
            }
            return texture;
        }

        public void Icon(Rect area, string unicode)
        {
            string encoded = Encode(unicode);
            EmojiCatalog.Glyph glyph;
            if (encoded.Length > 0 && catalog.TryGet(encoded[0], out glyph))
                GUI.DrawTexture(area, Texture(glyph), ScaleMode.ScaleToFit);
        }

        public void DrawInline(Rect area, string encoded, GUIStyle style, Color background, Rect visible, bool editable)
        {
            if (Event.current.type != EventType.Repaint || encoded.Length == 0) return;
            var content = new GUIContent(encoded);
            TextEditor editor = editable ? (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) : null;
            Vector2 offset = editor == null ? Vector2.zero : editor.scrollOffset;
            int first = Mathf.Max(0, style.GetCursorStringIndex(area, content, new Vector2(visible.xMin + offset.x, visible.yMin + offset.y)) - 1);
            int last = Mathf.Min(encoded.Length, style.GetCursorStringIndex(area, content, new Vector2(visible.xMax + offset.x, visible.yMax + offset.y)) + 1);
            for (int i = first; i < last; i++)
            {
                EmojiCatalog.Glyph glyph;
                if (!catalog.TryGet(encoded[i], out glyph)) continue;
                Vector2 point = style.GetCursorPixelPosition(area, content, i) - offset;
                int after = i + 1 < encoded.Length && encoded[i + 1] == Padding ? i + 2 : i + 1;
                Vector2 end = style.GetCursorPixelPosition(area, content, after) - offset;
                float advance = end.y == point.y ? end.x - point.x : style.fontSize;
                Rect slot = new Rect(point.x, point.y, Mathf.Max(advance, 8), style.lineHeight);
                if (!slot.Overlaps(visible)) continue;
                Color fill = background;
                if (editor != null && i >= Mathf.Min(editor.cursorIndex, editor.selectIndex) && i < Mathf.Max(editor.cursorIndex, editor.selectIndex))
                    fill = Color.Lerp(background, GUI.skin.settings.selectionColor, GUI.skin.settings.selectionColor.a);
                Color old = GUI.color;
                GUI.color = old * fill;
                GUI.DrawTexture(slot, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.DrawTexture(new Rect(slot.x, slot.y, slot.width, slot.height), Texture(glyph), ScaleMode.ScaleToFit);
            }
        }

        public bool CopySelection()
        {
            TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            int start = Mathf.Min(editor.cursorIndex, editor.selectIndex);
            int end = Mathf.Max(editor.cursorIndex, editor.selectIndex);
            if (editor.text == null || start < 0 || end > editor.text.Length || start == end) return false;
            ExpandSelection(editor.text, ref start, ref end);
            GUIUtility.systemCopyBuffer = Decode(editor.text.Substring(start, end - start));
            return true;
        }

        public string ReadEdited(string value)
        {
            string encoded = Encode(Decode(value));
            if (encoded != value)
            {
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                int cursor = Encode(Decode(value.Substring(0, Mathf.Clamp(editor.cursorIndex, 0, value.Length)))).Length;
                int selection = Encode(Decode(value.Substring(0, Mathf.Clamp(editor.selectIndex, 0, value.Length)))).Length;
                editor.text = encoded;
                editor.cursorIndex = cursor;
                editor.selectIndex = selection;
            }
            string decoded = Decode(encoded);
            return decoded.Length <= 512 ? decoded : ChatMessage.Clean(decoded, 512);
        }

        private bool IsMarker(string value, int index)
        {
            EmojiCatalog.Glyph glyph;
            return index >= 0 && index < value.Length && catalog.TryGet(value[index], out glyph);
        }

        private void ExpandSelection(string value, ref int start, ref int end)
        {
            if (start > 0 && start < value.Length && value[start] == Padding && IsMarker(value, start - 1)) start--;
            if (end > 0 && end < value.Length && IsMarker(value, end - 1) && value[end] == Padding) end++;
        }

        public void HandleEditing(Event current, ref string draft)
        {
            if (current.type != EventType.KeyDown) return;
            TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            string value = Encode(draft);
            int start = Mathf.Clamp(Mathf.Min(editor.cursorIndex, editor.selectIndex), 0, value.Length);
            int end = Mathf.Clamp(Mathf.Max(editor.cursorIndex, editor.selectIndex), start, value.Length);
            bool cut = (current.control || current.command) && current.keyCode == KeyCode.X;
            if (current.keyCode == KeyCode.Backspace || current.keyCode == KeyCode.Delete || cut)
            {
                if (start == end)
                {
                    if (cut) return;
                    if (current.keyCode == KeyCode.Backspace && start > 0 && value[start - 1] == Padding && IsMarker(value, start - 2)) start -= 2;
                    else if (current.keyCode == KeyCode.Delete && IsMarker(value, start)) end = Mathf.Min(value.Length, start + 2);
                    else return;
                }
                ExpandSelection(value, ref start, ref end);
                if (cut) GUIUtility.systemCopyBuffer = Decode(value.Substring(start, end - start));
                value = value.Remove(start, end - start);
                draft = Decode(value);
                editor.text = value;
                editor.cursorIndex = editor.selectIndex = start;
                current.Use();
            }
            else if (start == end && !current.control && !current.command)
            {
                int cursor = editor.cursorIndex;
                if (current.keyCode == KeyCode.LeftArrow && start > 0 && value[start - 1] == Padding && IsMarker(value, start - 2)) cursor -= 2;
                else if (current.keyCode == KeyCode.RightArrow && IsMarker(value, start)) cursor += 2;
                else return;
                editor.cursorIndex = cursor;
                if (!current.shift) editor.selectIndex = cursor;
                current.Use();
            }
        }

        public void Dispose()
        {
            foreach (Texture2D texture in textures.Values) UnityEngine.Object.Destroy(texture);
            catalog.Dispose();
        }
    }
}

