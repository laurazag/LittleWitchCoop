using System;
using System.IO;
using System.Text;

namespace LittleWitchCoop
{
    internal sealed class ChatMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public uint Id;
        public string Author;
        public string Text;
        public bool IsEmote;
        public string SenderId = "";

        public static string Clean(string value, int limit)
        {
            var result = new StringBuilder();
            foreach (char character in value ?? "")
            {
                if (result.Length == limit) break;
                if (character == '\n' || character == '\r' || character == '\t') result.Append(' ');
                else if (!char.IsControl(character)) result.Append(character);
            }
            if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
            return Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.ToString())).Trim();
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Id);
            writer.Write((byte)(IsEmote ? 1 : 0));
            WriteText(writer, SenderId);
            WriteText(writer, Author);
            WriteText(writer, Text);
        }

        public static ChatMessage Read(BinaryReader reader)
        {
            uint id = reader.ReadUInt32();
            byte kind = reader.ReadByte();
            if (kind > 1) throw new InvalidDataException("Tipo de mensagem inválido.");
            var message = new ChatMessage {
                Id = id, IsEmote = kind == 1, SenderId = ReadText(reader, 96), Author = ReadText(reader, 40), Text = ReadText(reader, 512)
            };
            if (message.Id == 0 || message.Author.Length == 0 || message.Text.Length == 0)
                throw new InvalidDataException("Mensagem de chat inválida.");
            if (message.IsEmote && !EmoteChoices.Contains(message.Text))
                throw new InvalidDataException("Emote inválido.");
            return message;
        }

        internal static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        internal static string ReadText(BinaryReader reader, int limit)
        {
            int length = reader.ReadUInt16();
            if (length > limit * 4) throw new InvalidDataException("Mensagem longa demais.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string value = Utf8.GetString(bytes);
            if (value.Length > limit || value != Clean(value, limit)) throw new InvalidDataException("Texto de chat inválido.");
            return value;
        }
    }
}

