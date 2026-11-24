using System;

namespace LittleWitchCoop
{
    internal enum RemovalReason { None, Kicked, Banned }
    internal sealed class RoomRemovalException : InvalidOperationException
    {
        public readonly RemovalReason Reason;
        public RoomRemovalException(RemovalReason reason) : base(reason == RemovalReason.Banned ? L10n.T("Você foi banido pelo host.") : L10n.T("Você foi expulso pelo host. Pode entrar novamente se quiser.")) { Reason = reason; }
    }
    internal interface ICoopRoom : IDisposable
    {
        string Status { get; }
        bool Connected { get; }
        bool Finished { get; }
        string PeerName { get; }
        string PeerId { get; }
        bool IsHost { get; }
        RoomPeer[] Peers { get; }
        int Capacity { get; }
        RemovalReason CloseReason { get; }
        bool RemovePeer(string expectedId, bool ban);
        PlayerSnapshot Remote { get; }
        void Publish(PlayerSnapshot snapshot);
        bool SendChat(string author, string text);
        bool SendEmote(string author, string emoji);
        bool TryReceiveChat(out ChatMessage message);
    }
}

