using System;
using System.IO;
using System.Text;

namespace LittleWitchCoop
{
    // Publicações imutáveis depois de enviadas à thread de rede.
    internal sealed class PlayerSnapshot
    {
        public string Scene = "";
        public SpritePose[] Sprites = new SpritePose[0];

        public void Write(BinaryWriter writer)
        {
            WriteText(writer, Scene);
            writer.Write((byte)Sprites.Length);
            foreach (SpritePose sprite in Sprites)
            {
                WriteText(writer, sprite.Key);
                foreach (float value in sprite.Transform) writer.Write(value);
                writer.Write(sprite.FlipX);
                writer.Write(sprite.FlipY);
                writer.Write(sprite.SortingLayer);
                writer.Write(sprite.SortingOrder);
            }
            writer.Flush();
        }

        public static PlayerSnapshot Read(BinaryReader reader)
        {
            var state = new PlayerSnapshot { Scene = ReadText(reader) };
            int count = reader.ReadByte();
            if (count > 16) throw new InvalidDataException("Muitas camadas de personagem.");
            state.Sprites = new SpritePose[count];
            for (int i = 0; i < count; i++)
            {
                var sprite = new SpritePose { Key = ReadText(reader) };
                for (int j = 0; j < sprite.Transform.Length; j++)
                {
                    float value = reader.ReadSingle();
                    if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000)
                        throw new InvalidDataException("Transformação inválida.");
                    sprite.Transform[j] = value;
                }
                float rotationLength = 0;
                for (int j = 3; j < 7; j++) rotationLength += sprite.Transform[j] * sprite.Transform[j];
                if (rotationLength < 0.9f || rotationLength > 1.1f)
                    throw new InvalidDataException("Rotação inválida.");
                sprite.FlipX = reader.ReadBoolean();
                sprite.FlipY = reader.ReadBoolean();
                sprite.SortingLayer = reader.ReadInt32();
                sprite.SortingOrder = reader.ReadInt32();
                if (sprite.SortingOrder < -32768 || sprite.SortingOrder > 32767)
                    throw new InvalidDataException("Ordem de desenho inválida.");
                state.Sprites[i] = sprite;
            }
            return state;
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > 512) throw new InvalidDataException("Identificador muito longo.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > 512) throw new InvalidDataException("Identificador muito longo.");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
    }

    internal sealed class SpritePose
    {
        public string Key = "";
        // Posição, quaternion e escala em coordenadas do mundo.
        public float[] Transform = new float[10];
        public bool FlipX;
        public bool FlipY;
        public int SortingLayer;
        public int SortingOrder;
    }
}

