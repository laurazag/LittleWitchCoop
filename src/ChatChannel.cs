using System.Collections.Generic;
using System.IO;

namespace LittleWitchCoop
{
    // Confirma e repete mensagens até a recepção, inclusive sobre os pacotes P2P não confiáveis.
    internal sealed class ChatChannel
    {
        private readonly object gate = new object();
        private readonly Queue<ChatMessage> outgoing = new Queue<ChatMessage>();
        private readonly Queue<ChatMessage> incoming = new Queue<ChatMessage>();
        private uint nextId;
        private uint sentId;
        private uint receivedId;
        public bool CanSend { get { lock (gate) { return outgoing.Count < 32 && nextId < uint.MaxValue; } } }

        public bool Send(string author, string text, bool isEmote = false, string senderId = "")
        {
            author = ChatMessage.Clean(author, 40);
            text = ChatMessage.Clean(text, 512);
            if (author.Length == 0 || text.Length == 0) return false;
            if (isEmote && !EmoteChoices.Contains(text)) return false;
            lock (gate)
            {
                if (outgoing.Count >= 32 || nextId == uint.MaxValue) return false;
                outgoing.Enqueue(new ChatMessage { Id = ++nextId, Author = author, Text = text, IsEmote = isEmote, SenderId = senderId });
                return true;
            }
        }

        public bool TryReceive(out ChatMessage message)
        {
            lock (gate)
            {
                message = incoming.Count == 0 ? null : incoming.Dequeue();
                return message != null;
            }
        }

        public void Write(BinaryWriter writer)
        {
            lock (gate)
            {
                writer.Write(receivedId);
                writer.Write(outgoing.Count > 0);
                if (outgoing.Count == 0) return;
                ChatMessage message = outgoing.Peek();
                sentId = message.Id;
                message.Write(writer);
            }
        }

        public void Read(BinaryReader reader)
        {
            uint acknowledged = reader.ReadUInt32();
            ChatMessage message = reader.ReadBoolean() ? ChatMessage.Read(reader) : null;
            lock (gate)
            {
                if (acknowledged > sentId) throw new InvalidDataException("Confirmação de chat inválida.");
                while (outgoing.Count > 0 && outgoing.Peek().Id <= acknowledged) outgoing.Dequeue();
                if (message == null || message.Id <= receivedId) return;
                if (message.Id != receivedId + 1 || incoming.Count >= 64)
                    throw new InvalidDataException("Sequência de chat inválida.");
                receivedId = message.Id;
                incoming.Enqueue(message);
            }
        }
    }
}

