using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LittleWitchCoop
{
    internal sealed class ChatOverlay : IDisposable
    {
        private const string InputControl = "LWCoop.ChatInput";
        private const string HistoryControl = "LWCoop.ChatHistory";
        private readonly List<string> history = new List<string>();
        private readonly HashSet<InputAction> blockedActions = new HashSet<InputAction>();
        private EmojiRenderer emojis;
        private sealed class PeerBubble { public string Text; public float Until; public bool Emote; public float LastEmote=-10; }
        private readonly Dictionary<string, PeerBubble> peerBubbles = new Dictionary<string, PeerBubble>();
        private readonly Dictionary<string, string> knownPeers = new Dictionary<string, string>();


        private bool showEmojiPicker;
        private int inputCursor;
        private int inputSelection;
        private int pendingCaret = -1;
        private int pressedEmoji = -1;
        private int pickerControl;
        private static readonly Color HistoryBackground = new Color(0.045f, 0.085f, 0.068f, 1f);
        private static readonly string[] QuickEmojis = {
            "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "😊", "🙂",
            "😉", "😍", "🥰", "😘", "😎", "🤔", "😴", "😭", "🥺", "😢",
            "😱", "😡", "😤", "🥳", "👍", "👎", "👋", "👏", "🙌", "🤝",
            "🙏", "💪", "❤️", "🧡", "💛", "💚", "💜", "✨", "🌸", "🍄",
            "🌿", "🌈", "🔥", "⭐", "🌙", "🧙", "🐈", "☕", "🎉", "👀"
        };
        private string historyText = "";
        private string draft = "";
        private string feedback = "";
        private string localBubble = "";

        private bool localBubbleIsEmote;

        private int lastBlockedFrame = -10;

        private AudioSource notificationAudio;
        private AudioClip notificationClip;
        private float nextNotification;
        public event Action<string> RemoteEmote;
        private float localBubbleUntil;

        private float lastMessageAt = -100;
        private int openedFrame;
        private bool focusInput;
        private bool scrollToEnd = true;
        private Vector2 scroll;
        private float contentHeight;
        private float historyHeight = 140;
        private GUIStyle text;
        private GUIStyle input;
        private GUIStyle hint;
        private GUIStyle shortcutKey;
        private GUIStyle shortcutText;
        private GUIStyle playerName;
        private Font nameFont;
        private GUIStyle bubble;
        private GUIStyle sendButton;
        private ICoopRoom room;
        private string author = "Ellie";
        public bool IsOpen { get; private set; }

        public void Tick(ICoopRoom currentRoom, string displayName, bool blocked = false)
        {
            if (emojis == null) emojis = new EmojiRenderer();
            if (!ReferenceEquals(room, currentRoom)) { knownPeers.Clear(); peerBubbles.Clear(); room = currentRoom; }
            var present = new HashSet<string>();
            if (room != null && !room.Finished) foreach (RoomPeer peer in room.Peers)
            {
                present.Add(peer.Id);
                if (!knownPeers.ContainsKey(peer.Id)) AddSystem(peer.Name + L10n.T(" entrou na sala."));
                knownPeers[peer.Id] = peer.Name;
            }
            foreach (string id in new List<string>(knownPeers.Keys)) if (!present.Contains(id))
            {
                AddSystem(knownPeers[id] + L10n.T(" saiu da sala.")); knownPeers.Remove(id); peerBubbles.Remove(id);
            }
            author = displayName;
            if (!blocked && !IsOpen && Input.GetKeyDown(KeyCode.Y)) SetOpen(true);
            ChatMessage message;
            if (room != null)
                while (room.TryReceiveChat(out message))
                {
                    if (message.IsEmote)
                    {
                        PeerBubble existing; if (peerBubbles.TryGetValue(message.SenderId, out existing) && Time.realtimeSinceStartup - existing.LastEmote < 0.5f) continue;

                        peerBubbles[message.SenderId] = new PeerBubble { Text=message.Text, Until=Time.realtimeSinceStartup+3.5f, Emote=true, LastEmote=Time.realtimeSinceStartup };
                        if (RemoteEmote != null) RemoteEmote(message.Text);
                    }
                    else
                    {
                        Add(message.Author, message.Text, false);
                        peerBubbles[message.SenderId] = new PeerBubble { Text=message.Text, Until=Time.realtimeSinceStartup+Mathf.Clamp(3f+message.Text.Length*0.04f,4f,8f) };
                        PlayNotification();
                    }
                }
        }

        private void PlayNotification()
        {
            // Collapse bursts into a single quiet notification; emotes have their own sound.
            if (Time.realtimeSinceStartup < nextNotification) return;
            nextNotification = Time.realtimeSinceStartup + 0.7f;
            if (notificationAudio == null)
            {
                var holder = new GameObject("LittleWitchCoop.ChatNotification");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                notificationAudio = holder.AddComponent<AudioSource>();
                notificationAudio.playOnAwake = false;
                notificationAudio.spatialBlend = 0f;
                notificationAudio.volume = 0.12f;
                const int rate = 22050;
                var samples = new float[(int)(rate * 0.18f)];
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = (double)i / rate;
                    double envelope = Math.Min(1, t / 0.012) * Math.Exp(-22 * t) * Math.Min(1, (0.18 - t) / 0.025);
                    samples[i] = (float)(Math.Sin(2 * Math.PI * 783.99 * t) * envelope * 0.35);
                }
                notificationClip = AudioClip.Create("Little Witch Co-op chat", samples.Length, 1, rate, false);
                notificationClip.SetData(samples, 0);
            }
            notificationAudio.PlayOneShot(notificationClip);
        }

        public void SetOpen(bool value)
        {
            if (IsOpen == value) return;
            IsOpen = value;
            if (value)
            {
                openedFrame = Time.frameCount;
                focusInput = true;
                feedback = "";
                BlockGameInput();
            }
            else
            {
                showEmojiPicker = false;
                pressedEmoji = -1;
                if (pickerControl != 0 && GUIUtility.hotControl == pickerControl) GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
            }
        }

        public void BlockGameInput(bool emoteWheelOpen = false)
        {
            if (!IsOpen && !emoteWheelOpen)
            {
                // Wait for the closing key/mouse press to end before restoring
                // gameplay actions, so Esc/B/click cannot also activate the game.
                if (Time.frameCount <= lastBlockedFrame + 1 || Input.GetKey(KeyCode.Escape)
                    || Input.GetKey(KeyCode.F2)
                    || Input.GetKey(KeyCode.B) || Input.GetKey(KeyCode.Return)
                    || Input.GetKey(KeyCode.KeypadEnter) || Input.GetMouseButton(0)) return;
                RestoreGameInput();
                return;
            }
            lastBlockedFrame = Time.frameCount;
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                blockedActions.Add(action);
                action.Disable();
            }
        }

        private void RestoreGameInput()
        {
            foreach (InputAction action in blockedActions)
            {
                try { action.Enable(); }
                catch (ObjectDisposedException) { }
            }
            blockedActions.Clear();
        }

        public void ShowEmote(string emoji, bool local)
        {
            if (!EmoteChoices.Contains(emoji)) return;
            float until = Time.realtimeSinceStartup + 3.5f;
            if (local) { localBubble = emoji; localBubbleUntil = until; localBubbleIsEmote = true; }

        }

        private void Add(string name, string message, bool local)
        {
            AppendHistory(DateTime.Now.ToString("HH:mm") + "  " + name + "  " + message);
            float until = lastMessageAt + Mathf.Clamp(3f + message.Length * 0.04f, 4f, 8f);
            if (local) { localBubble = message; localBubbleUntil = until; localBubbleIsEmote = false; }

        }

        private void AddSystem(string message)
        {
            AppendHistory("• " + message);
        }

        public void PlayerRemoved(string name, bool banned)
        {


            AddSystem(ChatMessage.Clean(name, 40) + (banned ? L10n.T(" foi banido da sala.") : L10n.T(" foi expulso da sala.")));
        }

        private void AppendHistory(string line)
        {
            if (!IsOpen || scroll.y >= contentHeight - historyHeight - 30) scrollToEnd = true;
            history.Add(line);
            if (history.Count > 200) history.RemoveAt(0);
            historyText = emojis.Encode(string.Join("\n\n", history.ToArray()));
            lastMessageAt = Time.realtimeSinceStartup;
        }

        private void Send()
        {
            string message = ChatMessage.Clean(draft, 512);
            if (message.Length == 0) { SetOpen(false); return; }
            if (room == null || !room.Connected)
            {
                feedback = L10n.T("Conecte-se a uma sala para enviar.");
                return;
            }
            if (!room.SendChat(author, message))
            {
                feedback = L10n.T("Aguarde a conexão confirmar as mensagens anteriores.");
                return;
            }
            Add(author, message, true);
            draft = "";
            SetOpen(false);
        }

        private void PrepareStyles()
        {
            if (emojis == null) emojis = new EmojiRenderer();
            if (text != null) return;
            text = new GUIStyle(GUI.skin.textArea) {
                fontSize = 14, wordWrap = true, richText = false,
                padding = new RectOffset(5, 5, 4, 4)
            };
            text.normal.background = null;
            text.focused.background = null;
            text.hover.background = null;
            text.active.background = null;
            text.normal.textColor = text.focused.textColor = new Color(0.91f, 0.94f, 0.87f);
            input = new GUIStyle(GUI.skin.textField) { fontSize = 14, richText = false, padding = new RectOffset(8, 8, 6, 4) };
            hint = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = false, wordWrap = true };
            hint.normal.textColor = new Color(0.72f, 0.82f, 0.72f);
            shortcutKey = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            shortcutKey.normal.textColor = new Color(1f, 0.91f, 0.65f);
            shortcutText = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            shortcutText.normal.textColor = new Color(0.96f, 0.95f, 0.87f);
            playerName = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, richText = false, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            playerName.normal.textColor = Color.white;
            foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
            {
                string fontName = font.name.ToLowerInvariant();
                if (fontName.Contains("pixel") || fontName.Contains("dunggeunmo") || fontName.Contains("galmuri") || fontName.Contains("m5x7") || fontName.Contains("pressstart"))
                {
                    playerName.font = font;
                    break;
                }
            }
            string fontPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ChatOverlay).Assembly.Location), "Minecraftia-Regular.ttf");
            if (System.IO.File.Exists(fontPath))
            {
                try
                {
                    nameFont = new Font(fontPath);
                    if (!nameFont.HasCharacter('A')) throw new InvalidOperationException("Fonte sem caracteres validos.");
                    playerName.font = nameFont;
                    playerName.fontStyle = FontStyle.Normal;
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[LittleWitchCoop] Fonte dos nomes: " + error.Message);
                    if (nameFont != null) UnityEngine.Object.Destroy(nameFont);
                    nameFont = null;
                }
            }
            bubble = new GUIStyle(GUI.skin.label) {
                fontSize = 13, wordWrap = true, richText = false,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0)
            };
            bubble.normal.textColor = new Color(0.12f, 0.18f, 0.14f);
            sendButton = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }

        public void Draw(Vector3? localHead, Dictionary<string, Vector3?> remoteHeads)
        {
            PrepareStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            int oldDepth = GUI.depth;
            Color oldSelection = GUI.skin.settings.selectionColor;
            float scale = Mathf.Clamp(Screen.height / 900f, 0.75f, 1.5f);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.color = Color.white;
            GUI.depth = -11000;
            GUI.skin.settings.selectionColor = new Color(0.35f, 0.55f, 0.42f, 0.85f);
            try
            {
                if (room != null && room.Connected)
                {
                    DrawPlayerName(author, localHead, scale);
                    foreach (RoomPeer peer in room.Peers)
                    {
                        Vector3? head;
                        if (remoteHeads.TryGetValue(peer.Id, out head)) DrawPlayerName(peer.Name, head, scale);
                    }
                }
                DrawBubble(localBubble, localBubbleUntil, localHead, scale, localBubbleIsEmote);
                if (room != null && room.Connected) foreach (var item in peerBubbles)
                {
                    Vector3? head;
                    if (remoteHeads.TryGetValue(item.Key, out head)) DrawBubble(item.Value.Text, item.Value.Until, head, scale, item.Value.Emote);
                }
                Rect area = new Rect(width * 0.2f, height - 40 - (IsOpen ? 220 : 60), width * 0.61f, IsOpen ? 220 : 60);
                if (IsOpen) DrawOpen(area);
                else DrawRecent(area);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
                GUI.depth = oldDepth;
                GUI.skin.settings.selectionColor = oldSelection;
            }
        }

        private void DrawOpen(Rect area)
        {
            Event current = Event.current;
            Rect entry = new Rect(area.x + 10, area.yMax - 57, area.width - 174, 29);
            Rect picker = new Rect(area.xMax - 356, entry.y - 204, 346, 198);
            pickerControl = GUIUtility.GetControlID("LWCoop.EmojiPicker".GetHashCode(), FocusType.Passive);
            int pickedEmoji = HandlePickerInput(picker, current);
            if (GUI.GetNameOfFocusedControl() == InputControl) emojis.HandleEditing(current, ref draft);
            bool chatControl = GUI.GetNameOfFocusedControl() == InputControl || GUI.GetNameOfFocusedControl() == HistoryControl;
            if (chatControl && ((current.type == EventType.KeyDown && (current.control || current.command) && current.keyCode == KeyCode.C)
                || (current.type == EventType.ExecuteCommand && current.commandName == "Copy")))
            {
                emojis.CopySelection();
                current.Use();
            }
            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Y && Time.frameCount == openedFrame) current.Use();
                else if (current.keyCode == KeyCode.Escape) { SetOpen(false); current.Use(); return; }
                else if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                {
                    Send(); current.Use();
                    if (!IsOpen) return;
                }
                else if (GUI.GetNameOfFocusedControl() == HistoryControl && !AllowsHistoryKey(current)) current.Use();
            }
            Fill(area, HistoryBackground);
            Fill(new Rect(area.x, area.y, area.width, 2), new Color(0.48f, 0.62f, 0.47f, 0.9f));
            Rect view = new Rect(area.x + 9, area.y + 8, area.width - 18, area.height - 72);
            historyHeight = view.height;
            float textWidth = view.width - 20;
            contentHeight = Mathf.Max(view.height, text.CalcHeight(new GUIContent(historyText), textWidth) + 8);
            if (scrollToEnd) { scroll.y = Mathf.Max(0, contentHeight - view.height); scrollToEnd = false; }
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, textWidth, contentHeight));
            GUI.SetNextControlName(HistoryControl);
            Rect textArea = new Rect(0, 0, textWidth, contentHeight);
            GUI.TextArea(textArea, historyText, text);
            emojis.DrawInline(textArea, historyText, text, HistoryBackground, new Rect(0, scroll.y, textWidth, view.height), GUI.GetNameOfFocusedControl() == HistoryControl);
            GUI.EndScrollView();
            GUI.SetNextControlName(InputControl);
            string edited = GUI.TextField(entry, emojis.Encode(draft), 512, input);
            draft = emojis.ReadEdited(edited);
            if (focusInput && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(InputControl);
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.text = emojis.Encode(draft);
                editor.cursorIndex = editor.selectIndex = pendingCaret < 0 ? editor.text.Length : pendingCaret;
                pendingCaret = -1;
                focusInput = false;
            }
            if (GUI.GetNameOfFocusedControl() == InputControl)
            {
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                inputCursor = editor.cursorIndex;
                inputSelection = editor.selectIndex;
            }
            GUI.BeginGroup(entry);
            Rect inputArea = new Rect(0, 0, entry.width, entry.height);
            emojis.DrawInline(inputArea, emojis.Encode(draft), input, HistoryBackground, inputArea, GUI.GetNameOfFocusedControl() == InputControl);
            GUI.EndGroup();
            bool enabled = GUI.enabled;
            if (GUI.Button(new Rect(area.xMax - 156, area.yMax - 57, 72, 29), "Emojis", sendButton)) showEmojiPicker = !showEmojiPicker;
            GUI.enabled = enabled && room != null && room.Connected && draft.Trim().Length > 0;
            if (GUI.Button(new Rect(area.xMax - 74, area.yMax - 57, 64, 29), L10n.T("Enviar"), sendButton)) Send();
            GUI.enabled = enabled;
            string help = room == null || !room.Connected ? L10n.T("Conecte-se a uma sala para enviar. Enter com campo vazio fecha.") : L10n.T("Enter envia ou fecha se vazio • selecione o histórico e use Ctrl+C para copiar");
            GUI.Label(new Rect(area.x + 10, area.yMax - 25, area.width - 20, 22), feedback.Length > 0 ? feedback : help, hint);
            if (showEmojiPicker) DrawEmojiPicker(picker);
            if (pickedEmoji >= 0) InsertEmoji(QuickEmojis[pickedEmoji]);
        }

        private static Rect EmojiButton(Rect area, int index)
        {
            return new Rect(area.x + 8 + index % 10 * 33, area.y + 23 + index / 10 * 30, 30, 28);
        }

        private int HandlePickerInput(Rect area, Event current)
        {
            // Consume the entire gesture before TextArea/BeginScrollView can see it.
            // GUI.enabled alone does not isolate a selectable text control underneath.
            bool captured = GUIUtility.hotControl == pickerControl;
            if (!showEmojiPicker && !captured) return -1;
            bool inside = showEmojiPicker && area.Contains(current.mousePosition);
            if (current.type == EventType.MouseDown && inside)
            {
                pressedEmoji = -1;
                if (current.button == 0)
                {
                    for (int i = 0; i < QuickEmojis.Length; i++)
                        if (EmojiButton(area, i).Contains(current.mousePosition)) { pressedEmoji = i; break; }
                    GUIUtility.hotControl = pickerControl;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp && (captured || inside))
            {
                int chosen = captured && current.button == 0 && pressedEmoji >= 0
                    && EmojiButton(area, pressedEmoji).Contains(current.mousePosition) ? pressedEmoji : -1;
                if (captured) GUIUtility.hotControl = 0;
                pressedEmoji = -1;
                current.Use();
                return chosen;
            }
            else if ((current.type == EventType.MouseDrag && captured)
                || (current.type == EventType.ScrollWheel && inside)) current.Use();
            return -1;
        }

        private void InsertEmoji(string unicode)
        {
            string encoded = emojis.Encode(draft);
            int start = Mathf.Clamp(Mathf.Min(inputCursor, inputSelection), 0, encoded.Length);
            int end = Mathf.Clamp(Mathf.Max(inputCursor, inputSelection), start, encoded.Length);
            string insert = emojis.Encode(unicode);
            draft = emojis.Decode(encoded.Remove(start, end - start).Insert(start, insert));
            if (draft.Length > 512) draft = ChatMessage.Clean(draft, 512);
            pendingCaret = Mathf.Min(start + insert.Length, emojis.Encode(draft).Length);
            inputCursor = inputSelection = pendingCaret;
            focusInput = true;
        }

        private void DrawEmojiPicker(Rect area)
        {
            Fill(area, HistoryBackground);
            GUI.Label(new Rect(area.x + 8, area.y + 3, area.width - 16, 18), "Emojis", hint);
            for (int i = 0; i < QuickEmojis.Length; i++)
            {
                Rect buttonArea = EmojiButton(area, i);
                if (Event.current.type == EventType.Repaint)
                    sendButton.Draw(buttonArea, GUIContent.none, buttonArea.Contains(Event.current.mousePosition),
                        pressedEmoji == i && GUIUtility.hotControl == pickerControl, false, false);
                emojis.Icon(new Rect(buttonArea.x + 5, buttonArea.y + 4, 20, 20), QuickEmojis[i]);
            }
            GUI.Label(new Rect(area.x + 8, area.yMax - 19, area.width - 16, 18), "Twemoji • CC BY 4.0", hint);
        }

        private static bool AllowsHistoryKey(Event current)
        {
            if (current.control || current.command)
                return current.keyCode == KeyCode.C || current.keyCode == KeyCode.A;
            return current.keyCode == KeyCode.LeftArrow || current.keyCode == KeyCode.RightArrow
                || current.keyCode == KeyCode.UpArrow || current.keyCode == KeyCode.DownArrow
                || current.keyCode == KeyCode.Home || current.keyCode == KeyCode.End
                || current.keyCode == KeyCode.PageUp || current.keyCode == KeyCode.PageDown;
        }

        private void DrawRecent(Rect area)
        {
            float age = Time.realtimeSinceStartup - lastMessageAt;
            if (history.Count > 0 && age < 12f)
            {
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(12f - age));
                Fill(area, new Color(0.045f, 0.085f, 0.068f, 0.72f));
                string recent = "";
                for (int i = Mathf.Max(0, history.Count - 3); i < history.Count; i++)
                    recent += Ellipsis(emojis.Encode(history[i]), 110) + "\n";
                Rect recentArea = new Rect(area.x + 8, area.y + 2, area.width - 16, area.height - 4);
                GUI.Label(recentArea, recent, text);
                emojis.DrawInline(recentArea, recent, text, HistoryBackground, recentArea, false);
                GUI.color = Color.white;
            }
            if (room != null && room.Connected)
            {
                DrawShortcut(new Rect(area.x, area.yMax + 3, 116, 29), "Y", L10n.T("Conversar"));
                DrawShortcut(new Rect(area.x + 124, area.yMax + 3, 100, 29), "B", "Emotes");
            }
        }

        private void DrawShortcut(Rect area, string key, string description)
        {
            Fill(area, new Color(0.36f, 0.48f, 0.36f, 0.95f));
            Fill(new Rect(area.x + 1, area.y + 1, area.width - 2, area.height - 2), new Color(0.045f, 0.085f, 0.068f, 0.94f));
            Rect keyArea = new Rect(area.x + 5, area.y + 4, 23, 21);
            Fill(keyArea, new Color(0.35f, 0.42f, 0.30f));
            GUI.Label(keyArea, key, shortcutKey);
            GUI.Label(new Rect(area.x + 35, area.y, area.width - 39, area.height), description, shortcutText);
        }

        private void DrawPlayerName(string name, Vector3? head, float scale)
        {
            Camera camera = Camera.main;
            if (head == null || camera == null || string.IsNullOrEmpty(name)) return;
            Vector3 point = camera.WorldToScreenPoint(head.Value);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            string shown = Ellipsis(ChatMessage.Clean(name, 40), 24);
            if (nameFont != null)
            {
                nameFont.RequestCharactersInTexture(shown, 12, FontStyle.Normal);
                if (nameFont.material != null && nameFont.material.mainTexture != null)
                    nameFont.material.mainTexture.filterMode = FilterMode.Point;
            }
            while (playerName.CalcSize(new GUIContent(shown)).x > 170 && new StringInfo(shown).LengthInTextElements > 2)
                shown = Ellipsis(shown, new StringInfo(shown).LengthInTextElements - 1);
            float width = Mathf.Clamp(playerName.CalcSize(new GUIContent(shown)).x + 16, 40, 186);
            Rect area = new Rect(Mathf.Clamp(point.x / scale - width / 2, 2, Screen.width / scale - width - 2),
                Mathf.Max(2, (Screen.height - point.y) / scale + 5), width, 22);
            Fill(area, new Color(0f, 0f, 0f, 0.72f));
            GUI.Label(area, shown, playerName);
        }

        private void DrawBubble(string message, float until, Vector3? head, float scale, bool isEmote)
        {
            if (head == null || message.Length == 0 || Time.realtimeSinceStartup >= until) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            Vector3 point = camera.WorldToScreenPoint(head.Value);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            string preview = Ellipsis(emojis.Encode(message), 72);
            float maxWidth = 210;
            while (bubble.CalcHeight(new GUIContent(preview), maxWidth) > 38 && preview.Length > 4)
                preview = Ellipsis(preview, new StringInfo(preview).LengthInTextElements - 2);
            float w = Mathf.Clamp(bubble.CalcSize(new GUIContent(preview)).x, 60, maxWidth) + 20;
            float h = Mathf.Max(22, bubble.CalcHeight(new GUIContent(preview), w - 20)) + 14;
            if (isEmote) { w = 60; h = 58; }
            Rect area = new Rect(point.x / scale - w / 2, (Screen.height - point.y) / scale - h - 4, w, h);
            Color border = new Color(0.16f, 0.23f, 0.17f, 0.97f);
            Color paper = new Color(0.98f, 0.95f, 0.81f, 0.98f);
            Fill(new Rect(area.x + 3, area.y, area.width - 6, area.height), border);
            Fill(new Rect(area.x, area.y + 3, area.width, area.height - 6), border);
            Fill(new Rect(area.x + 3, area.y + 3, area.width - 6, area.height - 6), paper);
            Fill(new Rect(area.center.x - 6, area.yMax - 1, 12, 5), border);
            Fill(new Rect(area.center.x - 3, area.yMax + 4, 6, 4), border);
            Fill(new Rect(area.center.x - 3, area.yMax - 2, 6, 5), paper);
            Rect bubbleText = new Rect(area.x + 10, area.y + 7, area.width - 20, area.height - 14);
            if (isEmote)
            {
                emojis.Icon(new Rect(area.center.x - 20, area.y + 8, 40, 40), message);
                return;
            }
            GUI.Label(bubbleText, preview, bubble);
            emojis.DrawInline(bubbleText, preview, bubble, paper, bubbleText, false);
        }

        private static string Ellipsis(string value, int limit)
        {
            var info = new StringInfo(value);
            return info.LengthInTextElements <= limit ? value : info.SubstringByTextElements(0, Mathf.Max(1, limit - 1)) + "…";
        }

        private static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = old * color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public void Dispose()
        {
            SetOpen(false);
            RestoreGameInput();
            if (emojis != null) emojis.Dispose();
            if (notificationAudio != null) UnityEngine.Object.Destroy(notificationAudio.gameObject);
            if (notificationClip != null) UnityEngine.Object.Destroy(notificationClip);
            if (nameFont != null) UnityEngine.Object.Destroy(nameFont);
        }
    }
}





