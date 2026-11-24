using BepInEx;
using BepInEx.Configuration;
using Steamworks;
using UnityEngine;

namespace LittleWitchCoop
{
    [BepInPlugin("br.littlewitch.coop", "Little Witch Co-op", "0.10.6")]
    public sealed class Plugin : BaseUnityPlugin
    {
#if DEVELOPMENT_BUILD
        private const string BuildFlavor = "testes";
#else
        private const string BuildFlavor = "pública";
#endif
        private bool open;
        private readonly CapacityButton decrease = new CapacityButton(), increase = new CapacityButton();
        private readonly ModerationAlert moderationAlert = new ModerationAlert();
        private ICoopRoom observedRoom;
        private RemovalReason observedRemoval;
        private bool showPlayers;
        private bool showBrowser;
        private bool showSettings;
        private ConfigEntry<string> language;
        private ConfigEntry<int> savedCapacity;
        private SteamLobbyBrowser lobbyBrowser;
        private string lastBrowserStatus;
        private readonly SteamLobbyPanel lobbyPanel = new SteamLobbyPanel();
        private BanStore bans;
        private PlayerListPanel players;
        private float opacity;
        private float startedAt;
        private ICoopRoom room;
        private readonly ChatOverlay chat = new ChatOverlay();
        private readonly EmoteWheel emotes = new EmoteWheel();
        private string displayName = "Ellie";
        private bool cursorCaptured;
        private SteamRoom steamRoom;
        private string steamMessage = "Aguardando inicialização da Steam...";
        private bool steamUnavailable;
        private float nextSteamCheck;
        private readonly RemotePlayer remotePlayer = new RemotePlayer();
        private readonly System.Collections.Generic.Dictionary<string, RemotePlayer> remotePlayers = new System.Collections.Generic.Dictionary<string, RemotePlayer>();
        private PlayerSnapshot localSnapshot = new PlayerSnapshot();
        private float nextSnapshot;
        private string address = "127.0.0.1";
        private string portText = "7777";
        private string roomCodeInput = "";
        private string networkMessage = "Hospede ou entre pelo IP do host.";
        private string lastNetworkStatus;
        private bool updateLogged;
        private bool guiLogged;
        private Texture2D background;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle button;
        private GUIStyle caption;
        private ConfigEntry<float> savedX;
        private ConfigEntry<float> savedY;
        private Rect window = new Rect(24, 24, 370, 510);
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;

        private void Awake()
        {
            language = Config.Bind("General", "Language", "auto", "auto = Windows; pt-BR = Portugues; en = English.");
            L10n.Language = language.Value;
            savedCapacity = Config.Bind("Room", "Capacity", 2, "Room size, including host: 2 to 99 (experimental).");
            lobbyPanel.Capacity = Mathf.Clamp(savedCapacity.Value, 2, 99);
            steamMessage = "Aguardando inicialização da Steam...";
            networkMessage = "Hospede ou entre pelo IP do host.";
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            bans = new BanStore(System.IO.Path.Combine(Paths.ConfigPath, "LittleWitchCoop.bans.txt"));
            players = new PlayerListPanel(bans);
            chat.RemoteEmote += emoji => { if (room != null && room.Connected) emotes.PlaySound(emoji); };
            savedX = Config.Bind("Panel", "X", 24f, "Posição horizontal do painel.");
            savedY = Config.Bind("Panel", "Y", 24f, "Posição vertical do painel.");
            window.x = float.IsNaN(savedX.Value) || float.IsInfinity(savedX.Value) ? 24f : savedX.Value;
            window.y = float.IsNaN(savedY.Value) || float.IsInfinity(savedY.Value) ? 24f : savedY.Value;
            Logger.LogInfo("Little Witch Co-op 0.10.6 carregado. F2 abre o painel. Y abre o chat.");
        }

        private void Update()
        {
            InitializeSteam();
            if (steamRoom != null) steamRoom.Tick(Time.realtimeSinceStartup);
            if (lobbyBrowser != null) lobbyBrowser.Tick(Time.realtimeSinceStartup, steamRoom == null ? 0 : steamRoom.GameplayPeer);
            if (lobbyBrowser != null && lobbyBrowser.Status != lastBrowserStatus)
            {
                lastBrowserStatus = lobbyBrowser.Status;
                Logger.LogInfo("Navegador Steam: " + lastBrowserStatus + " Resultados da API: " + lobbyBrowser.NativeMatches);
            }
            if (!ReferenceEquals(room, observedRoom) || room == null || room.CloseReason == RemovalReason.None)
            { observedRoom = room; observedRemoval = RemovalReason.None; }
            if (room != null && room.CloseReason != RemovalReason.None && observedRemoval == RemovalReason.None)
            {
                observedRemoval = room.CloseReason;
                moderationAlert.Show(observedRemoval);
                chat.SetOpen(false); emotes.Close(); SetOpen(false);
            }
            chat.Tick(room, displayName, moderationAlert.IsOpen);
            emotes.Tick(room != null && room.Connected, chat.IsOpen || open || moderationAlert.IsOpen, RemotePlayer.LocalHead());
            chat.BlockGameInput(emotes.IsOpen || open || moderationAlert.IsOpen);
            if (chat.IsOpen && open) SetOpen(false);
            UpdateCursor();
            if (!updateLogged)
            {
                updateLogged = true;
                Logger.LogInfo("Update ativo: leitura de teclado iniciada.");
            }
            if (Input.GetKeyDown(KeyCode.F2) && !chat.IsOpen && !moderationAlert.IsOpen)
            {
                SetOpen(!open);
                Logger.LogInfo("F2 recebido. Painel " + (open ? "aberto" : "fechado"));
            }
            opacity = Mathf.MoveTowards(opacity, open ? 1f : 0f, Time.unscaledDeltaTime / 0.16f);
            if (room != null && room.Status != lastNetworkStatus)
            {
                lastNetworkStatus = room.Status;
                Logger.LogInfo(lastNetworkStatus);
            }
        }

        private void InitializeSteam()
        {
            if (steamRoom != null || steamUnavailable || Time.realtimeSinceStartup < nextSteamCheck) return;
            nextSteamCheck = Time.realtimeSinceStartup + 1f;
            try
            {
                if (Object.FindObjectOfType<SteamManager>() == null || !SteamManager.Initialized) return;
                lobbyBrowser = new SteamLobbyBrowser(bans);
                steamRoom = new SteamRoom(JoinSteam, bans, lobbyBrowser);
                displayName = ChatMessage.Clean(SteamFriends.GetPersonaName(), 40);
                if (displayName.Length == 0) displayName = "Ellie";
                lobbyPanel.RoomName = ChatMessage.Clean((L10n.Portuguese ? "Clareira de " : "Clearing of ") + displayName, 48);
                steamMessage = "Steam pronta para criar salas e receber convites.";
                Logger.LogInfo(steamMessage);
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    ulong lobbyId;
                    if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out lobbyId))
                    {
                        JoinSteam(new CSteamID(lobbyId));
                        break;
                    }
                }
            }
            catch (System.Exception exception)
            {
                steamUnavailable = true;
                steamMessage = (L10n.Portuguese ? "Steam indisponível nesta sessão. Use IP/Radmin." : "Steam unavailable. Use IP/Radmin.");
                Logger.LogError("Inicialização Steam do mod: " + exception);
            }
        }

        private void JoinSteam(CSteamID lobby)
        {
            if (steamRoom == null || steamRoom.Pending) return;
            if (room != null) room.Dispose();
            ClearRemotePlayers();
            room = steamRoom;
            steamRoom.Join(lobby, Time.realtimeSinceStartup);
            SetOpen(true);
            Logger.LogInfo("Convite Steam aceito. Entrando na sala.");
        }

        private void HostSteam(bool publicRoom = false)
        {
            if (steamRoom == null || steamRoom.Pending) return;
            if (room != null) room.Dispose();
            ClearRemotePlayers();
            room = steamRoom;
            steamRoom.Host(Time.realtimeSinceStartup, lobbyPanel.RoomName, publicRoom, lobbyPanel.Capacity);
        }

        private void StartRoom(bool host)
        {
            int port;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                networkMessage = (L10n.Portuguese ? "Use uma porta entre 1 e 65535." : "Use a port between 1 and 65535.");
                return;
            }
            if (!host && string.IsNullOrWhiteSpace(address))
            {
                networkMessage = (L10n.Portuguese ? "Informe o IP do host." : "Enter the host IP.");
                return;
            }
            if (room != null) room.Dispose();
            var tcpRoom = new NetworkRoom(displayName, bans, lobbyPanel.Capacity);
            room = tcpRoom;
            tcpRoom.Start(host, address.Trim(), port);
        }

        private void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            chat.BlockGameInput(emotes.IsOpen || open || moderationAlert.IsOpen);
            if (!open)
            {
                savedCapacity.Value = lobbyPanel.Capacity;
                savedX.Value = window.x;
                savedY.Value = window.y;
                Config.Save();
            }
            UpdateCursor();
        }

        private void UpdateCursor()
        {
            if (open || chat.IsOpen || emotes.IsOpen || moderationAlert.IsOpen)
            {
                if (!cursorCaptured)
                {
                    previousCursorVisible = Cursor.visible;
                    previousCursorLock = Cursor.lockState;
                    cursorCaptured = true;
                }
                ShowCursor();
            }
            else if (cursorCaptured)
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                cursorCaptured = false;
            }
        }

        private void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            chat.BlockGameInput(emotes.IsOpen || open || moderationAlert.IsOpen);
            UpdateCursor();
            if (room == null || !room.Connected)
            {
                ClearRemotePlayers();
                return;
            }
            if (Time.realtimeSinceStartup >= nextSnapshot)
            {
                localSnapshot = remotePlayer.Capture();
                room.Publish(localSnapshot);
                nextSnapshot = Time.realtimeSinceStartup + 0.05f;
            }
            var present = new System.Collections.Generic.HashSet<string>();
            foreach (RoomPeer peer in room.Peers)
            {
                present.Add(peer.Id);
                RemotePlayer visual;
                if (!remotePlayers.TryGetValue(peer.Id, out visual)) { visual = new RemotePlayer(); remotePlayers.Add(peer.Id, visual); }
                visual.Capture();
                visual.Render(peer.Snapshot, localSnapshot);
            }
            foreach (string id in new System.Collections.Generic.List<string>(remotePlayers.Keys))
                if (!present.Contains(id)) { remotePlayers[id].Clear(); remotePlayers.Remove(id); }
        }

        private void ClearRemotePlayers()
        {
            remotePlayer.Clear();
            foreach (var visual in remotePlayers.Values) visual.Clear();
            remotePlayers.Clear();
        }

        private void PrepareStyles()
        {
            if (background != null) return;
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.075f, 0.12f, 0.105f, 0.97f));
            background.Apply();
            title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.94f, 0.91f, 0.76f);
            body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            body.normal.textColor = new Color(0.84f, 0.89f, 0.83f);
            caption = new GUIStyle(body) { fontSize = 12 };
            caption.normal.textColor = new Color(0.64f, 0.75f, 0.65f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        }

        private void OnGUI()
        {
            if (moderationAlert.IsOpen) { moderationAlert.Draw(); UpdateCursor(); return; }
            DrawPanel();
                        var heads = new System.Collections.Generic.Dictionary<string, Vector3?>();
            foreach (var item in remotePlayers) heads[item.Key] = item.Value.RemoteHead();
            chat.Draw(RemotePlayer.LocalHead(), heads);
            string emote = emotes.Draw();
            if (emote != null && room != null && room.Connected && room.SendEmote(displayName, emote))
            {
                chat.ShowEmote(emote, true);
                emotes.PlaySound(emote);
                Logger.LogInfo("Emote enviado: " + emote);
            }
            UpdateCursor();
        }

        private void DrawPanel()
        {
            if (!guiLogged)
            {
                guiLogged = true;
                startedAt = Time.realtimeSinceStartup;
                Logger.LogInfo("OnGUI ativo: interface iniciada.");
            }
            GUI.depth = -10000;
            if (open) ShowCursor();
            PrepareStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            float scale = Mathf.Min(1.25f, Mathf.Min(Screen.width / 600f, Screen.height / 560f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float age = Time.realtimeSinceStartup - startedAt;
                if (age < 7f && !open)
                {
                    GUI.color = new Color(1, 1, 1, Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((7f - age) / 0.3f));
                    GUI.DrawTexture(new Rect(24, 24, 370, 88), background);
                    GUI.Label(new Rect(42, 35, 335, 25), L10n.T("Little Witch Co-op carregado"), body);
                    GUI.Label(new Rect(42, 66, 335, 24), L10n.T("Aperte F2 para abrir o painel"), caption);
                }
                if (opacity <= 0f) return;
                GUI.color = new Color(1, 1, 1, opacity);
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width / scale - window.width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height / scale - window.height));
                Rect animated = window;
                animated.y -= (1 - opacity) * 10;
                GUI.enabled = open;
                Rect moved = GUI.Window(174923, animated, DrawWindow, "", GUIStyle.none);
                if (open)
                {
                    window.x = Mathf.Clamp(moved.x, 0, Mathf.Max(0, Screen.width / scale - window.width));
                    window.y = Mathf.Clamp(moved.y + (1 - opacity) * 10, 0, Mathf.Max(0, Screen.height / scale - window.height));
                }
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
            }
        }

        private void DrawWindow(int id)
        {
                GUI.DrawTexture(new Rect(0, 0, 370, 510), background);
                GUI.Label(new Rect(20, 16, 260, 34), "Little Witch Co-op", title);
                if (GUI.Button(new Rect(286, 20, 28, 26), new GUIContent("", L10n.Portuguese ? "Configurações" : "Settings"), button)) showSettings = !showSettings;
                DrawSettingsIcon(new Rect(290, 23, 20, 20));
                if (GUI.Button(new Rect(322, 20, 28, 26), "×", button)) SetOpen(false);
                GUI.Label(new Rect(20, 57, 330, 24), L10n.T("Uma companhia na floresta"), caption);
                if (showSettings)
                {
                    GUI.Label(new Rect(20, 105, 330, 28), L10n.Portuguese ? "Idioma do mod" : "Mod language", body);
                    string[] choices = { "auto", "pt-BR", "en" };
                    string[] names = { L10n.Portuguese ? "Automático (Windows)" : "Automatic (Windows)", "Português (Brasil)", "English" };
                    for (int i = 0; i < choices.Length; i++)
                        if (GUI.Button(new Rect(20, 146 + i * 43, 330, 34), (L10n.Language == choices[i] ? "✓  " : "") + names[i], button))
                        {
                            L10n.Language = choices[i]; language.Value = choices[i]; Config.Save();
                        }
                    GUI.Label(new Rect(20, 286, 330, 70), L10n.Portuguese ? "Preferência salva neste PC. Nomes e mensagens dos jogadores não são traduzidos." : "Preference saved on this PC. Player names and messages are not translated.", caption);
                    GUI.Label(new Rect(20, 365, 220, 25), (L10n.Portuguese ? "Vagas (Steam e IP): " : "Slots (Steam and IP): ") + lobbyPanel.Capacity, caption);
                    bool capacityEnabled = GUI.enabled; GUI.enabled = capacityEnabled && (room == null || room.Finished);
                    lobbyPanel.Capacity = decrease.Draw(new Rect(254, 361, 42, 30), "-", button, lobbyPanel.Capacity, -1);
                    lobbyPanel.Capacity = increase.Draw(new Rect(305, 361, 45, 30), "+", button, lobbyPanel.Capacity, 1);
                    GUI.enabled = capacityEnabled;
                    if (GUI.Button(new Rect(20, 433, 330, 32), L10n.Portuguese ? "Voltar" : "Back", button)) showSettings = false;
                    GUI.DragWindow(new Rect(0, 0, 278, 54));
                    return;
                }
                if (GUI.Button(new Rect(20, 94, 103, 29), L10n.T("Conexão"), button)) { showPlayers = false; showBrowser = false; }
                if (GUI.Button(new Rect(130, 94, 111, 29), L10n.T("Salas Steam"), button))
                {
                    showPlayers = false; showBrowser = true;
                    if (lobbyBrowser != null) lobbyBrowser.Refresh();
                }
                if (GUI.Button(new Rect(248, 94, 102, 29), L10n.T("Jogadores"), button)) { showPlayers = true; showBrowser = false; }
                Color tabColor = GUI.color;
                GUI.color = new Color(0.63f, 0.75f, 0.65f, tabColor.a * 0.35f);
                GUI.DrawTexture(new Rect(20, 131, 330, 1), Texture2D.whiteTexture);
                GUI.color = tabColor;
                if (showBrowser)
                {
                    lobbyPanel.Draw(lobbyBrowser, steamRoom, room != null && !room.Finished, () => HostSteam(lobbyPanel.PublicRoom), JoinSteam);
                    GUI.Label(new Rect(20, 479, 330, 22), L10n.T("F2 abre / fecha     •     versão 0.10.6 • ") + (L10n.Portuguese ? BuildFlavor : BuildFlavor == "testes" ? "test" : "public"), caption);
                    GUI.DragWindow(new Rect(0, 0, 278, 54));
                    return;
                }
                if (showPlayers)
                {
                    players.Draw(room, displayName, chat.PlayerRemoved);
                    GUI.Label(new Rect(20, 479, 330, 22), L10n.T("F2 abre / fecha     •     versão 0.10.6 • ") + (L10n.Portuguese ? BuildFlavor : BuildFlavor == "testes" ? "test" : "public"), caption);
                    GUI.DragWindow(new Rect(0, 0, 278, 54));
                    return;
                }
                bool enabled = GUI.enabled;
                bool busy = room != null && !room.Finished;
                GUI.enabled = enabled && !busy && steamRoom != null && !steamRoom.Pending;
                if (GUI.Button(new Rect(20, 140, 160, 34), (L10n.Portuguese ? "Steam (privada)" : "Steam (private)"), button)) HostSteam();
                GUI.enabled = enabled && steamRoom != null && steamRoom.CanInvite;
                if (GUI.Button(new Rect(190, 140, 160, 34), L10n.T("Convidar amigos"), button)) steamRoom.Invite();
                GUI.enabled = enabled;
                bool hasRoomCode = steamRoom != null && steamRoom.RoomCode.Length > 0;
                GUI.Label(new Rect(20, 181, 330, 34), hasRoomCode ? (L10n.Portuguese ? "Sala privada Steam • compartilhe o código abaixo." : "Private Steam room • share the code below.") : L10n.T(steamMessage), caption);
                GUI.Label(new Rect(20, 223, 56, 26), L10n.Portuguese ? "Código" : "Code", caption);
                GUI.enabled = enabled && (hasRoomCode || !busy);
                if (hasRoomCode)
                {
                    GUI.TextField(new Rect(76, 223, 167, 28), steamRoom.RoomCode);
                    if (GUI.Button(new Rect(252, 221, 98, 32), L10n.Portuguese ? "Copiar" : "Copy", button)) GUIUtility.systemCopyBuffer = steamRoom.RoomCode;
                }
                else
                {
                    roomCodeInput = GUI.TextField(new Rect(76, 223, 167, 28), roomCodeInput, 80);
                    GUI.enabled = enabled && !busy && steamRoom != null && !steamRoom.Pending;
                    if (GUI.Button(new Rect(252, 221, 98, 32), L10n.T("Entrar"), button))
                    {
                        if (steamRoom.JoinCode(roomCodeInput, Time.realtimeSinceStartup)) { if (room != null && !ReferenceEquals(room, steamRoom)) room.Dispose(); ClearRemotePlayers(); room = steamRoom; }
                        else networkMessage = L10n.Portuguese ? "Código da sala inválido." : "Invalid room code.";
                    }
                }
                GUI.enabled = enabled && !busy;
                GUI.Label(new Rect(20, 263, 50, 26), L10n.T("Porta"), body);
                portText = GUI.TextField(new Rect(76, 263, 80, 28), portText, 5);
                if (GUI.Button(new Rect(168, 261, 182, 32), L10n.T("Hospedar por IP"), button)) StartRoom(true);
                GUI.Label(new Rect(20, 308, 35, 26), "IP", body);
                address = GUI.TextField(new Rect(55, 308, 188, 28), address, 253);
                if (GUI.Button(new Rect(252, 306, 98, 32), L10n.T("Entrar"), button)) StartRoom(false);
                GUI.enabled = enabled;
                string status = room == null ? L10n.T(networkMessage) : room.Status;
                if (ReferenceEquals(room, steamRoom) && steamRoom != null && steamRoom.Connected && steamRoom.PingMilliseconds >= 0)
                    status += "\nPing Steam: " + steamRoom.PingMilliseconds + " ms";
                GUI.Label(new Rect(20, 350, 330, 55), status, body);
                GUI.Label(new Rect(20, 406, 330, 22), room != null && room.Connected ? remotePlayer.Status : L10n.T("IP direto • rede local ou Radmin"), caption);
                GUI.enabled = enabled && room != null;
                if (GUI.Button(new Rect(20, 433, 330, 32), L10n.T("Desconectar / fechar sala"), button))
                {
                    room.Dispose();
                    room = null;
                    networkMessage = "Desconectado. Você pode abrir outra sala.";
                }
                GUI.enabled = enabled;
                GUI.Label(new Rect(20, 479, 330, 22), L10n.T("F2 abre / fecha     •     versão 0.10.6 • ") + (L10n.Portuguese ? BuildFlavor : BuildFlavor == "testes" ? "test" : "public"), caption);
                GUI.DragWindow(new Rect(0, 0, 278, 54));
        }

        private static void DrawSettingsIcon(Rect area)
        {
            if (Event.current.type != EventType.Repaint) return;
            // Pixel gear: drawn directly so missing font glyphs cannot hide it.
            string[] pixels = {
                "0001100000", "0101101010", "0111111110", "0011001100",
                "1110000111", "1110000111", "0011001100", "0111111110",
                "0101101010", "0001100000"
            };
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.91f, 0.65f, previous.a);
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                    if (pixels[y][x] == '1') GUI.DrawTexture(new Rect(area.x + x * 2, area.y + y * 2, 2, 2), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (open) SetOpen(false);
            chat.Dispose();
            emotes.Dispose();
            if (players != null) players.Dispose();
            UpdateCursor();
            if (room != null) room.Dispose();
            if (steamRoom != null) steamRoom.Shutdown();
            if (lobbyBrowser != null) lobbyBrowser.Dispose();
            ClearRemotePlayers();
            Logger.LogInfo("Componente do painel destruído.");
            if (background != null) Destroy(background);
        }
    }
}
























