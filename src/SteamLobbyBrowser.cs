using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Steamworks;

namespace LittleWitchCoop
{
    internal sealed class SteamLobbyEntry
    {
        public CSteamID Lobby;
        public CSteamID Owner;
        public string Name;
        public int Members;
        public int Capacity;
        public bool IsLocal;
        public int Ping = -1;
        internal ulong Nonce;
        internal long ProbeStarted;
        internal float Deadline;
    }

    internal sealed class SteamLobbyBrowser : IDisposable
    {
        private const int Channel = 28;
        private readonly BanStore bans;
        private readonly CallResult<LobbyMatchList_t> found;
        private readonly Callback<LobbyDataUpdate_t> updated;
        private readonly IntPtr[] messages = new IntPtr[32];
        private readonly List<SteamLobbyEntry> entries = new List<SteamLobbyEntry>();
        private readonly Dictionary<ulong, float> closeAfter = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastReply = new Dictionary<ulong, float>();
        private CSteamID published;
        private ulong activePeer;
        private float now;
        private float searchStarted;
        private float nextSearch;
        private float nextProbe;
        private bool disposed;
        public bool Searching { get; private set; }
        public uint NativeMatches { get; private set; }
        public string Status { get; private set; }
        public IList<SteamLobbyEntry> Entries { get { return entries.AsReadOnly(); } }

        public SteamLobbyBrowser(BanStore store)
        {
            bans = store;
            Status = L10n.T("Busque salas públicas deste mod na Steam.");
            found = CallResult<LobbyMatchList_t>.Create(OnFound);
            updated = Callback<LobbyDataUpdate_t>.Create(data => { if (!disposed && data.m_bSuccess != 0) Add(new CSteamID(data.m_ulSteamIDLobby)); });
        }

        public void Publish(CSteamID lobby) { published = lobby; }
        public void Unpublish() { published = CSteamID.Nil; }
        public void PreserveConnection(CSteamID candidate) { if (candidate.IsValid()) closeAfter[candidate.m_SteamID] = now + 25; }

        public void Refresh()
        {
            if (disposed || Searching || now < nextSearch) return;
            nextSearch = now + 2;
            try
            {
                if (!SteamUser.BLoggedOn()) { Status = L10n.T("Entre na Steam para buscar salas."); return; }
                SteamNetworkingUtils.InitRelayNetworkAccess();
                // Validate mod metadata after fetching it, not only in the backend
                // search index (which can lag behind freshly published metadata).
                SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(100);
                UnityEngine.Debug.Log("[LittleWitchCoop] Lobby search AppID=" + SteamUtils.GetAppID() + " protocol=" + SteamRoom.Protocol);
                Searching = true;
                searchStarted = now;
                Status = L10n.T("Buscando salas na Steam...");
                found.Set(SteamMatchmaking.RequestLobbyList());
            }
            catch (Exception) { Searching = false; Status = L10n.T("A Steam não conseguiu buscar salas agora."); }
        }

        private void OnFound(LobbyMatchList_t result, bool failure)
        {
            Searching = false;
            if (disposed) return;
            if (failure) { Status = L10n.T("Falha na busca. Tente atualizar novamente."); return; }
            NativeMatches = result.m_nLobbiesMatching;
            entries.Clear();
            for (int i = 0; i < Math.Min(100, result.m_nLobbiesMatching); i++) Request(SteamMatchmaking.GetLobbyByIndex(i));
            int friends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < friends; i++)
            {
                FriendGameInfo_t game;
                if (SteamFriends.GetFriendGamePlayed(SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate), out game)
                    && game.m_gameID.AppID() == SteamUtils.GetAppID() && game.m_steamIDLobby.IsValid()) Request(game.m_steamIDLobby);
            }
            UnityEngine.Debug.Log("[LittleWitchCoop] Native lobby results=" + NativeMatches + " visible=" + entries.Count);
            // Steam may omit one's own lobby from the search. It is still a real
            // published lobby, so expose its current metadata locally as well.
            if (published.IsValid()) Add(published);
            nextProbe = now;
            Status = entries.Count == 0 ? L10n.T("Nenhuma sala pública encontrada. Você pode criar uma.") : entries.Count + L10n.T(" sala(s) encontrada(s).");
        }

        private void Add(CSteamID lobby)
        {
            if (!lobby.IsValid()) return;
            ulong ownerId; int capacity, members;
            if (!LobbyListing.TryRead(SteamMatchmaking.GetLobbyData(lobby, "lwcoop"),
                SteamMatchmaking.GetLobbyData(lobby, "lwcoop-public"),
                SteamMatchmaking.GetLobbyData(lobby, "lwcoop-owner"),
                SteamMatchmaking.GetLobbyData(lobby, "lwcoop-capacity"),
                SteamMatchmaking.GetLobbyData(lobby, "lwcoop-members"), SteamRoom.Protocol,
                out ownerId, out capacity, out members)) return;
            CSteamID owner = new CSteamID(ownerId);
            if (!owner.IsValid()) return;
            string name = ChatMessage.Clean(SteamMatchmaking.GetLobbyData(lobby, "lwcoop-name"), 48);
            foreach (var existing in entries) if (existing.Lobby == lobby)
            {
                existing.Owner = owner; existing.Name = name; existing.Capacity = capacity; existing.Members = members;
                return;
            }
            entries.Add(new SteamLobbyEntry {
                Lobby = lobby, Owner = owner, Name = name.Length == 0 ? "Clareira das bruxinhas" : name,
                Members = members, Capacity = capacity,
                IsLocal = owner == SteamUser.GetSteamID()
            });
            Status = entries.Count + L10n.T(" sala(s) encontrada(s).");
        }

        private void Request(CSteamID lobby)
        {
            if (!lobby.IsValid()) return;
            Add(lobby);
            SteamMatchmaking.RequestLobbyData(lobby);
        }

        public bool CanAcceptProbe(CSteamID candidate)
        {
            if (disposed || !candidate.IsValid() || candidate == SteamUser.GetSteamID()) return false;
            foreach (var entry in entries)
                if (entry.Owner == candidate && entry.Nonce != 0) return true;
            if (!published.IsValid() || bans.Contains("steam:" + candidate.m_SteamID)
                || (!closeAfter.ContainsKey(candidate.m_SteamID) && closeAfter.Count >= 64)) return false;
            closeAfter[candidate.m_SteamID] = now + 5;
            return true;
        }

        public void Tick(float time, ulong gameplayPeer)
        {
            if (disposed) return;
            now = time;
            activePeer = gameplayPeer;
            try
            {
                if (Searching && now - searchStarted > 15)
                {
                    found.Cancel(); Searching = false; Status = L10n.T("A busca demorou demais. Tente atualizar.");
                }
                Receive();
                foreach (var entry in entries)
                {
                    if (entry.IsLocal) continue;
                    if (entry.Nonce != 0 && now > entry.Deadline) { entry.Nonce = 0; continue; }
                    if (entry.ProbeStarted != 0 || now < nextProbe) continue;
                    entry.Nonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) | 1UL;
                    entry.ProbeStarted = Stopwatch.GetTimestamp();
                    entry.Deadline = now + 8;
                    nextProbe = now + 0.1f;
                    Send(entry.Owner, SteamPingPacket.Write(false, entry.Lobby.m_SteamID, entry.Nonce));
                    closeAfter[entry.Owner.m_SteamID] = now + 10;
                    break;
                }
                var expired = new List<ulong>();
                foreach (var item in closeAfter) if (now >= item.Value && item.Key != activePeer) expired.Add(item.Key);
                foreach (ulong id in expired)
                {
                    var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(id));
                    SteamNetworkingMessages.CloseSessionWithUser(ref identity);
                    closeAfter.Remove(id);
                    lastReply.Remove(id);
                }
            }
            catch (Exception) { Status = L10n.T("Não foi possível medir alguns pings. Você ainda pode tentar entrar."); }
        }

        private void Receive()
        {
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, messages, messages.Length);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SteamNetworkingMessage_t incoming = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                    if (incoming.m_cbSize != SteamPingPacket.Size) continue;
                    var data = new byte[SteamPingPacket.Size]; Marshal.Copy(incoming.m_pData, data, 0, data.Length);
                    bool reply; ulong lobby; ulong nonce;
                    if (!SteamPingPacket.TryRead(data, out reply, out lobby, out nonce)) continue;
                    CSteamID sender = incoming.m_identityPeer.GetSteamID();
                    if (reply)
                    {
                        foreach (var entry in entries)
                            if (entry.Owner == sender && entry.Lobby.m_SteamID == lobby && entry.Nonce == nonce && now <= entry.Deadline)
                            {
                                double elapsed = (Stopwatch.GetTimestamp() - entry.ProbeStarted) * 1000.0 / Stopwatch.Frequency;
                                entry.Ping = (int)Math.Max(1, Math.Min(9999, Math.Round(elapsed)));
                                entry.Nonce = 0;
                                break;
                            }
                    }
                    else
                    {
                        if (!published.IsValid() || published.m_SteamID != lobby || !CanAcceptProbe(sender)) continue;
                        float last;
                        if (lastReply.TryGetValue(sender.m_SteamID, out last) && now - last < 1) continue;
                        lastReply[sender.m_SteamID] = now;
                        Send(sender, SteamPingPacket.Write(true, lobby, nonce));
                    }
                }
            }
            finally { for (int i = 0; i < count; i++) SteamNetworkingMessage_t.Release(messages[i]); }
        }

        private static void Send(CSteamID target, byte[] data)
        {
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(target);
                SteamNetworkingMessages.SendMessageToUser(ref identity, buffer, (uint)data.Length, Constants.k_nSteamNetworkingSend_Reliable, Channel);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            disposed = true;
            found.Cancel(); found.Dispose();
            updated.Dispose();
            foreach (ulong id in closeAfter.Keys)
            {
                if (id == activePeer) continue;
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(id));
                SteamNetworkingMessages.CloseSessionWithUser(ref identity);
            }
            closeAfter.Clear();
        }
    }
}

