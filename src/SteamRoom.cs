using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Steamworks;

namespace LittleWitchCoop
{
    // Todas as chamadas Steam ficam na thread principal, junto dos callbacks do jogo.
    internal sealed class SteamRoom : ICoopRoom
    {
        internal const string Protocol = "lwcoop-6";
        private readonly SteamLobbyBrowser browser;
        private string roomName;
        private bool publicRoom;
        public ulong GameplayPeer { get { foreach (ulong id in connections.Keys) return id; return 0; } }
        public int PingMilliseconds { get; private set; }
        private float nextPing;
        private const int Channel = 27;
        private const int PacketMagic = 0x4C574335;
        private readonly BanStore bans;
        private readonly HashSet<string> removedSessions = new HashSet<string>();
        private string joinToken;
        public bool IsHost { get { return hosting; } }
        public string PeerId { get { var p=Peers; return p.Length==0?"":p[0].Id; } }

        public bool SendChat(string author, string text) { return Connected && state.Send(author,text,false); }
        public bool SendEmote(string author, string emoji) { return Connected && state.Send(author,emoji,true); }
        public bool TryReceiveChat(out ChatMessage message) { message=null; return state!=null && state.Receive(out message); }
        private const int MaxPacketBytes = 262144;
        private readonly CallResult<LobbyCreated_t> created;
        private readonly CallResult<LobbyEnter_t> entered;
        private readonly Callback<GameLobbyJoinRequested_t> invited;
        private readonly Callback<LobbyInvite_t> codeInvited;
        private PrivateRoomCode privateCode, pendingCode;
        private const int CodeChannel = 29;
        private readonly IntPtr[] codeMessages = new IntPtr[16];
        private readonly Dictionary<ulong, float> codeRequests = new Dictionary<ulong, float>();
        private float nextCodeRequest, codeWindow;
        private int codeSessions;
        public string RoomCode { get { return hosting && !publicRoom && !Finished && privateCode != null ? privateCode.Text : ""; } }
        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> sessionRequested;
        private readonly Callback<SteamNetworkingMessagesSessionFailed_t> sessionFailed;
        private readonly IntPtr[] messages = new IntPtr[32];
        private CSteamID lobby;

        private CSteamID owner;
        private bool hosting;
        private bool cancelled;
        private bool shuttingDown;
        private float startedAt;

        private float nextSend;

        public string Status { get; private set; }
        public string PeerName { get { var p=Peers; return p.Length==0?"":p[0].Name; } }
        public bool Connected { get {return !Finished && Peers.Length>0;} }
        public bool Finished { get; private set; }
        public bool Pending { get; private set; }
        public RemovalReason CloseReason { get; private set; }
        public PlayerSnapshot Remote { get { var p=Peers; return p.Length==0?new PlayerSnapshot():p[0].Snapshot; } }
        public bool CanInvite { get { return hosting && lobby.IsValid() && !Pending && !Finished; } }

        public SteamRoom(Action<CSteamID> onInvite, BanStore banStore = null, SteamLobbyBrowser lobbyBrowser = null)
        {
            bans = banStore ?? new BanStore();
            browser = lobbyBrowser;
            PingMilliseconds = -1;
            Status = L10n.T("Steam disponível. Crie uma sala ou aceite um convite.");
            Finished = true;
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            invited = Callback<GameLobbyJoinRequested_t>.Create(invite => onInvite(invite.m_steamIDLobby));
            codeInvited = Callback<LobbyInvite_t>.Create(invite =>
            {
                if (pendingCode == null || invite.m_ulSteamIDLobby != pendingCode.Lobby || invite.m_ulSteamIDUser != pendingCode.Owner
                    || invite.m_ulGameID != SteamUtils.GetAppID().m_AppId) return;
                CSteamID target = new CSteamID(pendingCode.Lobby);
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(pendingCode.Owner));
                SteamNetworkingMessages.CloseChannelWithUser(ref identity, CodeChannel);
                pendingCode = null;
                Join(target, UnityEngine.Time.realtimeSinceStartup);
            });
            sessionRequested = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequested);
            sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(failure =>
            {
                ulong id = failure.m_info.m_identityRemote.GetSteamID().m_SteamID; if (connections.ContainsKey(id)) { RemoveConnection(id); if (!hosting) Fail(L10n.T("A conexão Steam falhou. Tente abrir outra sala.")); }
            });
        }

        public void Host(float now, string name = "Clareira das bruxinhas", bool visible = false, int capacity = 2)
        {
            if (Pending) return;
            try
            {
                requestedCapacity=Math.Max(2,Math.Min(99,capacity)); Begin(now,true);
                roomName = ChatMessage.Clean(name, 48);
                if (roomName.Length == 0) roomName = "Clareira das bruxinhas";
                publicRoom = visible;
                Status = L10n.T("Criando sala na Steam...");
                created.Set(SteamMatchmaking.CreateLobby(visible ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypePrivate, requestedCapacity));
            }
            catch (Exception) { Pending = false; Fail(L10n.T("A API Steam não conseguiu criar a sala nesta sessão.")); }
        }

        public void Join(CSteamID target, float now)
        {
            if (Pending || !target.IsValid()) return;
            try
            {
                ulong advertisedOwner;
                if (browser != null && ulong.TryParse(SteamMatchmaking.GetLobbyData(target, "lwcoop-owner"), out advertisedOwner))
                    browser.PreserveConnection(new CSteamID(advertisedOwner));
                Begin(now,false);
                Status = L10n.T("Entrando na sala do convite Steam...");
                entered.Set(SteamMatchmaking.JoinLobby(target));
            }
            catch (Exception) { Pending = false; Fail(L10n.T("A API Steam não conseguiu entrar na sala nesta sessão.")); }
        }

        private void Begin(float now,bool host)
        {
            Dispose();
            CloseReason = RemovalReason.None;
            hosting=host; state=new RoomState("steam:"+SteamUser.GetSteamID().m_SteamID,ChatMessage.Clean(SteamFriends.GetPersonaName(),40),host,requestedCapacity);
            cancelled = false;
            Pending = true;
            Finished = false;
            startedAt = now;
            nextSend = now;

            PingMilliseconds = -1;

            joinToken = Guid.NewGuid().ToString("N");
            removedSessions.Clear();
            publishedMembers = -1;
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        private void OnCreated(LobbyCreated_t result, bool ioFailure)
        {
            Pending = false;
            CSteamID resultLobby = new CSteamID(result.m_ulSteamIDLobby);
            if (cancelled || shuttingDown)
            {
                if (resultLobby.IsValid()) SteamMatchmaking.LeaveLobby(resultLobby);
                if (shuttingDown) ReleaseCallbacks();
                return;
            }
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Fail(L10n.T("A Steam não criou a sala. Resultado: ") + result.m_eResult);
                return;
            }
            lobby = resultLobby;
            owner = SteamUser.GetSteamID();
            if (!SteamMatchmaking.SetLobbyData(lobby, "lwcoop", Protocol)
                || !SteamMatchmaking.SetLobbyData(lobby, "lwcoop-name", roomName)
                || !SteamMatchmaking.SetLobbyData(lobby, "lwcoop-public", publicRoom ? "1" : "0")
                || !SteamMatchmaking.SetLobbyData(lobby, "lwcoop-owner", owner.m_SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture))
                || !SteamMatchmaking.SetLobbyData(lobby, "lwcoop-capacity", requestedCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture))
                || !SteamMatchmaking.SetLobbyData(lobby, "lwcoop-members", "1"))
            {
                Fail(L10n.T("A Steam não confirmou os dados da sala."));
                return;
            }
            SteamMatchmaking.SetLobbyMemberData(lobby, "lwcoop", Protocol);
            SteamMatchmaking.SetLobbyMemberData(lobby, "lwcoop-session", joinToken);
            bool typeOk=SteamMatchmaking.SetLobbyType(lobby,publicRoom?ELobbyType.k_ELobbyTypePublic:ELobbyType.k_ELobbyTypePrivate);
            bool joinOk=SteamMatchmaking.SetLobbyJoinable(lobby,true);
            if (!publicRoom) privateCode = PrivateRoomCode.Create(owner.m_SteamID, lobby.m_SteamID);
            UnityEngine.Debug.Log("[LittleWitchCoop] Publish lobby="+lobby+" AppID="+SteamUtils.GetAppID()+" public="+publicRoom+" type="+typeOk+" joinable="+joinOk);
            if (publicRoom && browser != null) browser.Publish(lobby);
            Status = publicRoom ? L10n.T("Sala pública Steam aberta: ") + roomName : L10n.T("Sala privada Steam aberta. Use Convidar amigos.");
        }

        private void OnEntered(LobbyEnter_t result, bool ioFailure)
        {
            Pending = false;
            CSteamID resultLobby = new CSteamID(result.m_ulSteamIDLobby);
            if (cancelled || shuttingDown)
            {
                if (resultLobby.IsValid()) SteamMatchmaking.LeaveLobby(resultLobby);
                if (shuttingDown) ReleaseCallbacks();
                return;
            }
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Fail(L10n.T("Não foi possível entrar na sala Steam. Confira se ela Minda está aberta."));
                return;
            }
            lobby = resultLobby;
            if (SteamMatchmaking.GetLobbyData(lobby, "lwcoop") != Protocol)
            {
                Fail(L10n.T("A sala usa uma versão incompatível do mod."));
                return;
            }
            owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (browser != null) browser.PreserveConnection(owner);
            SteamMatchmaking.SetLobbyMemberData(lobby, "lwcoop", Protocol);
            SteamMatchmaking.SetLobbyMemberData(lobby, "lwcoop-session", joinToken);
            Status = L10n.T("Na sala Steam. Aguardando conexão com o host...");
        }

        public void Invite()
        {
            if (!CanInvite) return;
            if (!SteamUtils.IsOverlayEnabled())
            {
                Status = L10n.T("Ative o pMinel Steam nas configurações da Steam para convidar.");
                return;
            }
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        }

        private bool IsLobbyPeer(CSteamID candidate)
        {
            if (!lobby.IsValid() || candidate == SteamUser.GetSteamID()) return false;
            if (!hosting && candidate != owner) return false;
            string session = SteamMatchmaking.GetLobbyMemberData(lobby, candidate, "lwcoop-session");
            if (session.Length != 32 || removedSessions.Contains(candidate.m_SteamID + "|" + session)
                || (hosting && bans.Contains("steam:" + candidate.m_SteamID))) return false;
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i) == candidate)
                    return SteamMatchmaking.GetLobbyMemberData(lobby, candidate, "lwcoop") == Protocol;
            return false;
        }

        private void OnSessionRequested(SteamNetworkingMessagesSessionRequest_t request)
        {
            SteamNetworkingIdentity identity = request.m_identityRemote;
            if ((!Finished && IsLobbyPeer(identity.GetSteamID())) || CanAcceptCodeSession(identity.GetSteamID()) || (browser != null && browser.CanAcceptProbe(identity.GetSteamID())))
                SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
            else
                SteamNetworkingMessages.CloseSessionWithUser(ref identity);
        }

        public void Tick(float now)
        {
            try { TickCodes(now); TickRoom(now); }
            catch (Exception exception)
            {
                Fail(L10n.T("Falha na conexão Steam (") + exception.GetType().Name + ").");
            }
        }

        private void TickRoom(float now)
        {
            if (Finished) return;
            if (Pending)
            {
                if (now - startedAt > 20f) Fail(L10n.T("A Steam demorou para responder. Tente novamente em instantes."));
                return;
            }
            if (!SteamUser.BLoggedOn()) { Fail(L10n.T("A conexão com a Steam foi encerrada.")); return; }
            if (!lobby.IsValid()) return;
            if (SteamMatchmaking.GetLobbyOwner(lobby) != owner)
            {
                Fail(L10n.T("O host saiu da sala Steam."));
                return;
            }
            if (hosting)
            {
                int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
                if (members != publishedMembers)
                {
                    if (SteamMatchmaking.SetLobbyData(lobby, "lwcoop-members", members.ToString(System.Globalization.CultureInfo.InvariantCulture))) publishedMembers = members;
                }
            }

            if (!hosting) {
                string decision=SteamMatchmaking.GetLobbyData(lobby,"lwcoop-remove-"+SteamUser.GetSteamID().m_SteamID);
                if(decision==joinToken+"|ban" || decision==joinToken+"|kick") {CloseReason=decision.EndsWith("|ban")?RemovalReason.Banned:RemovalReason.Kicked;Fail(new RoomRemovalException(CloseReason).Message);return;}
            }
            var wanted=new HashSet<ulong>();
            int count=SteamMatchmaking.GetNumLobbyMembers(lobby);
            for(int i=0;i<count;i++) {
                CSteamID member=SteamMatchmaking.GetLobbyMemberByIndex(lobby,i);
                if(hosting && member!=SteamUser.GetSteamID() && bans.Contains("steam:"+member.m_SteamID)) {string token=SteamMatchmaking.GetLobbyMemberData(lobby,member,"lwcoop-session");if(token.Length==32)Reject(member,token,true);continue;}
                if(!IsLobbyPeer(member))continue;
                wanted.Add(member.m_SteamID);
                if(!connections.ContainsKey(member.m_SteamID)) {
                    string id="steam:"+member.m_SteamID;
                    string name=ChatMessage.Clean(SteamFriends.GetFriendPersonaName(member),40);if(name.Length==0)name="Ellie";
                    var link=state.Add(id,name,id);if(link==null)continue;
                    connections.Add(member.m_SteamID,new Connection{Member=member,Link=link,LastReceived=now});
                }
                if(browser!=null)browser.PreserveConnection(member);
            }
            var stale=new List<ulong>();
            foreach(var item in connections)if(!wanted.Contains(item.Key)||now-item.Value.LastReceived>15)stale.Add(item.Key);
            foreach(ulong id in stale){RemoveConnection(id);if(!hosting){Fail(L10n.T("A conexão Steam ficou sem resposta."));return;}}
            Receive(now);if(Finished)return;
            if(now>=nextSend){nextSend=now+0.05f;foreach(var c in connections.Values)Send(c);}
            if(now>=nextPing){nextPing=now+1;PingMilliseconds=-1;foreach(var c in connections.Values){
                var identity=new SteamNetworkingIdentity();identity.SetSteamID(c.Member);
                SteamNetConnectionInfo_t info;SteamNetConnectionRealTimeStatus_t quality;
                var result=SteamNetworkingMessages.GetSessionConnectionInfo(ref identity,out info,out quality);
                if(result==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected&&quality.m_nPing>=0)PingMilliseconds=quality.m_nPing;break;
            }}
            Status=(L10n.Portuguese?"Na sala Steam • ":"In Steam room • ")+(Peers.Length+1)+" / "+Capacity;
        }
        private sealed class Connection {public CSteamID Member;public RoomState.Link Link;public float LastReceived;public uint Sent,Received;}
        private readonly Dictionary<ulong,Connection> connections=new Dictionary<ulong,Connection>();
        private RoomState state;
        private int requestedCapacity=2;
        private int publishedMembers=-1;
        public int Capacity {get{return state==null?requestedCapacity:state.Capacity;}}
        public RoomPeer[] Peers {get{return state==null?new RoomPeer[0]:state.Peers;}}
        private void Send(Connection c) {
            byte[] data;
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                writer.Write(PacketMagic);writer.Write(lobby.m_SteamID);ChatMessage.WriteText(writer,joinToken);writer.Write(++c.Sent);
                state.Write(writer,c.Link);writer.Flush();data=stream.ToArray();
            }
            if(data.Length>MaxPacketBytes)return;
            IntPtr buffer=Marshal.AllocHGlobal(data.Length);
            try{Marshal.Copy(data,0,buffer,data.Length);var identity=new SteamNetworkingIdentity();identity.SetSteamID(c.Member);SteamNetworkingMessages.SendMessageToUser(ref identity,buffer,(uint)data.Length,Constants.k_nSteamNetworkingSend_UnreliableNoNagle,Channel);}
            finally{Marshal.FreeHGlobal(buffer);}
        }
        private void Receive(float now) {
            int count=SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel,messages,messages.Length);var invalid=new HashSet<ulong>();
            try{for(int i=0;i<count;i++){
                var m=SteamNetworkingMessage_t.FromIntPtr(messages[i]);ulong sender=m.m_identityPeer.GetSteamID().m_SteamID;Connection c;
                if(invalid.Contains(sender)||!connections.TryGetValue(sender,out c))continue;
                try{
                    if(m.m_cbSize<16||m.m_cbSize>MaxPacketBytes)throw new InvalidDataException();var data=new byte[m.m_cbSize];Marshal.Copy(m.m_pData,data,0,data.Length);
                    using(var stream=new MemoryStream(data))using(var r=new BinaryReader(stream)){
                        if(r.ReadInt32()!=PacketMagic||r.ReadUInt64()!=lobby.m_SteamID)continue;
                        if(ChatMessage.ReadText(r,32)!=SteamMatchmaking.GetLobbyMemberData(lobby,c.Member,"lwcoop-session"))continue;
                        uint seq=r.ReadUInt32();if(unchecked((int)(seq-c.Received))<=0)continue;
                        state.Read(r,c.Link);if(stream.Position!=stream.Length)throw new InvalidDataException();c.Received=seq;c.LastReceived=now;
                    }
                }catch(Exception e){if(!hosting && e is RoomRemovalException)CloseReason=((RoomRemovalException)e).Reason;UnityEngine.Debug.LogWarning("[LittleWitchCoop] Peer packet: "+e.Message);invalid.Add(sender);}
            }}finally{for(int i=0;i<count;i++)SteamNetworkingMessage_t.Release(messages[i]);}
            foreach(ulong id in invalid){RemoveConnection(id);if(!hosting){Fail(CloseReason==RemovalReason.None?"Steam: invalid host packet":new RoomRemovalException(CloseReason).Message);break;}}
        }
        public void Publish(PlayerSnapshot snapshot){if(state!=null)state.Publish(snapshot);}
        public bool RemovePeer(string expectedId,bool ban){
            if(!hosting||Finished||!lobby.IsValid()||SteamMatchmaking.GetLobbyOwner(lobby)!=SteamUser.GetSteamID())return false;
            ulong target=0;
            foreach(var item in connections)if(item.Value.Link.Id==expectedId){
                string token=SteamMatchmaking.GetLobbyMemberData(lobby,item.Value.Member,"lwcoop-session");if(token.Length!=32)return false;
                if(ban)bans.Add(expectedId,item.Value.Link.Name);if(!Reject(item.Value.Member,token,ban))return false;target=item.Key;break;
            }
            if(target==0)return false;RemoveConnection(target);return true;
        }
        private bool Reject(CSteamID member,string session,bool ban){
            string key=member.m_SteamID+"|"+session;if(removedSessions.Contains(key))return true;
            if(!SteamMatchmaking.SetLobbyData(lobby,"lwcoop-remove-"+member.m_SteamID,session+(ban?"|ban":"|kick")))return false;
            removedSessions.Add(key);return true;
        }
        private void RemoveConnection(ulong id){Connection c;if(!connections.TryGetValue(id,out c))return;state.Remove(c.Link.Id);connections.Remove(id);var identity=new SteamNetworkingIdentity();identity.SetSteamID(c.Member);SteamNetworkingMessages.CloseSessionWithUser(ref identity);}
        private void Fail(string message){Dispose();Status=message;}
        public void Dispose(){
            ClearCodeRequests();
            cancelled=true;if(browser!=null)browser.Unpublish();foreach(ulong id in new List<ulong>(connections.Keys))RemoveConnection(id);
            if(lobby.IsValid())SteamMatchmaking.LeaveLobby(lobby);lobby=CSteamID.Nil;state=null;Finished=true;Status=L10n.T("Sala Steam encerrada.");
        }
        public bool JoinCode(string text, float now)
        {
            PrivateRoomCode parsed;
            if (Pending || !PrivateRoomCode.TryParse(text, out parsed)
                || !new CSteamID(parsed.Owner).IsValid() || !new CSteamID(parsed.Lobby).IsValid())
            { Status = L10n.Portuguese ? "Código da sala inválido." : "Invalid room code."; return false; }
            Begin(now, false); pendingCode = parsed; nextCodeRequest = now;
            Status = L10n.Portuguese ? "Solicitando convite pelo código..." : "Requesting an invite using the code...";
            return true;
        }
        private bool CanAcceptCodeSession(CSteamID candidate)
        {
            if (Finished || !hosting || publicRoom || privateCode == null || !candidate.IsValid()
                || candidate == SteamUser.GetSteamID() || bans.Contains("steam:" + candidate.m_SteamID)) return false;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - codeWindow > 10) { codeWindow = now; codeSessions = 0; }
            return ++codeSessions <= 64;
        }
        private void TickCodes(float now)
        {
            if (pendingCode != null)
            {
                if (now - startedAt > 15)
                {
                    Pending = false;
                    Fail(L10n.Portuguese ? "Código expirado, sala cheia ou convite indisponível." : "Code expired, room full, or invite unavailable.");
                    return;
                }
                if (now >= nextCodeRequest)
                {
                    nextCodeRequest = now + 2;
                    byte[] data = System.Text.Encoding.ASCII.GetBytes(pendingCode.Text);
                    IntPtr buffer = Marshal.AllocHGlobal(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, buffer, data.Length);
                        var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(pendingCode.Owner));
                        SteamNetworkingMessages.SendMessageToUser(ref identity, buffer, (uint)data.Length, Constants.k_nSteamNetworkingSend_Reliable, CodeChannel);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(CodeChannel, codeMessages, codeMessages.Length);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var incoming = SteamNetworkingMessage_t.FromIntPtr(codeMessages[i]);
                    if (Finished || !hosting || publicRoom || privateCode == null || (incoming.m_cbSize != 30 && incoming.m_cbSize != 47)) continue;
                    CSteamID sender = incoming.m_identityPeer.GetSteamID();
                    if (!sender.IsValid() || sender == SteamUser.GetSteamID() || bans.Contains("steam:" + sender.m_SteamID)) continue;
                    float last;
                    if (codeRequests.TryGetValue(sender.m_SteamID, out last) && now - last < 3) continue;
                    if (!codeRequests.ContainsKey(sender.m_SteamID) && codeRequests.Count >= 64) continue;
                    codeRequests[sender.m_SteamID] = now;
                    byte[] data = new byte[incoming.m_cbSize]; Marshal.Copy(incoming.m_pData, data, 0, data.Length);
                    if (System.Text.Encoding.ASCII.GetString(data) != privateCode.Text) continue;
                    if (SteamMatchmaking.GetNumLobbyMembers(lobby) >= requestedCapacity) continue;
                    SteamMatchmaking.InviteUserToLobby(lobby, sender);
                }
            }
            finally { for (int i = 0; i < count; i++) SteamNetworkingMessage_t.Release(codeMessages[i]); }
            var expired = new List<ulong>();
            foreach (var item in codeRequests) if (now - item.Value > 30) expired.Add(item.Key);
            foreach (ulong id in expired)
            {
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(id));
                SteamNetworkingMessages.CloseChannelWithUser(ref identity, CodeChannel); codeRequests.Remove(id);
            }
        }
        private void ClearCodeRequests()
        {
            if (pendingCode != null)
            {
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(pendingCode.Owner));
                SteamNetworkingMessages.CloseChannelWithUser(ref identity, CodeChannel);
                Pending = false;
            }
            foreach (ulong id in codeRequests.Keys)
            {
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(id));
                SteamNetworkingMessages.CloseChannelWithUser(ref identity, CodeChannel);
            }
            codeRequests.Clear(); pendingCode = null; privateCode = null;
        }

        public void Shutdown()
        {
            shuttingDown = true;
            Dispose();
            // Uma resposta tardia Minda precisa sair da sala que acabou de criar.
            if (!Pending) ReleaseCallbacks();
        }

        private void ReleaseCallbacks()
        {
            created.Dispose();
            entered.Dispose();
            invited.Dispose();
            codeInvited.Dispose();
            sessionRequested.Dispose();
            sessionFailed.Dispose();
        }
    }
}








