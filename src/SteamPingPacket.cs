using System;
using System.IO;

namespace LittleWitchCoop
{
    internal static class SteamPingPacket
    {
        private const int Magic = 0x4C575031;
        public const int Size = 21;
        public static byte[] Write(bool reply, ulong lobby, ulong nonce)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((byte)(reply ? 2 : 1));
                writer.Write(lobby);
                writer.Write(nonce);
                return stream.ToArray();
            }
        }
        public static bool TryRead(byte[] data, out bool reply, out ulong lobby, out ulong nonce)
        {
            reply = false; lobby = nonce = 0;
            if (data == null || data.Length != Size) return false;
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                if (reader.ReadInt32() != Magic) return false;
                byte kind = reader.ReadByte();
                if (kind != 1 && kind != 2) return false;
                reply = kind == 2;
                lobby = reader.ReadUInt64();
                nonce = reader.ReadUInt64();
                return lobby != 0 && nonce != 0;
            }
        }
    }
}

