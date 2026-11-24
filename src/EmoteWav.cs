using System;
using System.IO;

namespace LittleWitchCoop
{
    internal static class EmoteWav
    {
        public static float[] Decode(byte[] bytes, out int channels, out int rate)
        {
            channels = 0; rate = 0;
            if (bytes.Length < 12 || bytes.Length > 2097152) throw new InvalidDataException("WAV deve ter ate 2 MB.");
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("RIFF ausente.");
                long end = 8L + reader.ReadUInt32();
                if (end > bytes.Length || end < 12 || new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAV incompleto.");
                int format = 0, bits = 0, alignment = 0, dataOffset = 0, dataLength = 0;
                while (reader.BaseStream.Position + 8 <= end)
                {
                    string id = new string(reader.ReadChars(4));
                    uint length = reader.ReadUInt32();
                    long start = reader.BaseStream.Position;
                    long next = start + length + (length & 1);
                    if (next > end) throw new InvalidDataException("Bloco WAV incompleto.");
                    if (id == "fmt ")
                    {
                        if (length < 16) throw new InvalidDataException("Formato WAV incompleto.");
                        format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                        reader.ReadUInt32(); alignment = reader.ReadUInt16(); bits = reader.ReadUInt16();
                    }
                    else if (id == "data") { dataOffset = (int)start; dataLength = (int)length; }
                    reader.BaseStream.Position = next;
                }
                if (format != 1 || bits != 16 || channels < 1 || channels > 2 || rate < 8000 || rate > 96000
                    || alignment != channels * 2 || dataLength == 0 || dataLength % alignment != 0
                    || dataLength / alignment > rate * 5) throw new InvalidDataException("Use WAV PCM 16 bits, mono/estereo, 8–96 kHz, ate 5 segundos.");
                reader.BaseStream.Position = dataOffset;
                var samples = new float[dataLength / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
                return samples;
            }
        }
    }
}

