using System;
using System.Globalization;
using UnityEngine;

namespace LittleWitchCoop
{
    internal sealed class PlayerListPanel : IDisposable
    {
        private readonly BanStore bans;
        private GUIStyle nameStyle;
        private GUIStyle small;
        private GUIStyle button;
        private Texture2D kickIcon;
        private Texture2D banIcon;
        private Vector2 playerScroll;
        private Vector2 banScroll;
        private string feedback = "";
        public bool IsPreview { get; private set; }
        public PlayerListPanel(BanStore store) { bans = store; }

        public void Draw(ICoopRoom room, string localName, Action<string, bool> removed)
        {
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false, wordWrap = false };
                nameStyle.normal.textColor = new Color(0.91f, 0.94f, 0.86f);
                small = new GUIStyle(nameStyle) { fontSize = 11, wordWrap = true };
                small.normal.textColor = new Color(0.68f, 0.79f, 0.68f);
                button = new GUIStyle(GUI.skin.button) { fontSize = 11 };
                kickIcon = MakeIcon(false);
                banIcon = MakeIcon(true);
            }
            bool connected = room != null && room.Connected;
            bool host = room != null && room.IsHost && !room.Finished;
#if DEVELOPMENT_BUILD
            if (GUI.Button(new Rect(251, 137, 99, 26), IsPreview ? L10n.T("Sair da prévia") : L10n.T("Prévia de 50"), button))
            {
                IsPreview = !IsPreview;
                playerScroll = Vector2.zero;
            }
#endif
            GUI.Label(new Rect(20, 138, 225, 23), IsPreview ? L10n.T("Prévia • 50 nomes") : (L10n.Portuguese ? "Na sala • " : "In room • ") + (room == null || room.Finished ? 1 : room.Peers.Length + 1) + " / " + (room == null ? 2 : room.Capacity), nameStyle);
            // Four full rows fit here. Only this list scrolls; the ban heading
            // and its separate list always stay below it, including in preview.
            float playerContentHeight = IsPreview ? 50 * 53 - 5 : Mathf.Max(207, ((room == null ? 0 : room.Peers.Length) + 1) * 53 - 5);
            playerScroll = GUI.BeginScrollView(new Rect(20, 166, 330, 207), playerScroll, new Rect(0, 0, 309, playerContentHeight));
            if (IsPreview) DrawPreviewRows();
            else
            {
                Row(new Rect(0, 0, 309, 48), localName, room == null || room.Finished ? L10n.T("Você") : host ? L10n.T("Você • host") : L10n.T("Você • convidada"), 203);
                if (connected)
                {
                    RoomPeer[] peers = room.Peers;
                    for (int i = 0; i < peers.Length; i++)
                    {
                        RoomPeer peer = peers[i]; float y = (i + 1) * 53;
                        Row(new Rect(0, y, 309, 48), peer.Name, peer.IsHost ? "Host" : L10n.T("Convidada"), 203);
                        if (host)
                        {
                            if (IconButton(new Rect(237, y + 8, 29, 30), kickIcon, L10n.T("Expulsar • pode entrar novamente"))) Moderate(room, peer.Id, peer.Name, false, removed);
                            if (IconButton(new Rect(273, y + 8, 29, 30), banIcon, L10n.T("Banir • bloquear próximas entradas"))) Moderate(room, peer.Id, peer.Name, true, removed);
                        }
                    }
                }
                else GUI.Label(new Rect(5, 60, 299, 44), room == null ? L10n.T("Hospede ou entre em uma sala pela aba Conexão.") : room.Status, small);
            }
            GUI.EndScrollView();
            BanEntry[] entries = bans.Entries;
            GUI.Label(new Rect(20, 382, 330, 22), L10n.T("Banidos da sua sala • ") + entries.Length, nameStyle);
            banScroll = GUI.BeginScrollView(new Rect(20, 409, 330, 46), banScroll, new Rect(0, 0, 309, Mathf.Max(46, entries.Length * 43)));
            if (entries.Length == 0) GUI.Label(new Rect(5, 6, 290, 32), L10n.T("Nenhum banimento salvo."), small);
            for (int i = 0; i < entries.Length; i++)
            {
                BanEntry entry = entries[i];
                GUI.Label(new Rect(5, i * 43, 211, 22), new GUIContent(ShortName(entry.Name, 202), entry.Name), nameStyle);
                GUI.Label(new Rect(5, i * 43 + 20, 210, 18), entry.Id.StartsWith("steam:", StringComparison.Ordinal) ? L10n.T("Conta Steam") : L10n.T("Endereço IP / Radmin"), small);
                if (GUI.Button(new Rect(223, i * 43 + 5, 82, 28), L10n.T("Desbanir"), button))
                {
                    try { bans.Remove(entry.Id); feedback = L10n.T("Jogador desbanido. Pode entrar novamente."); }
                    catch (Exception) { feedback = L10n.T("Não foi possível salvar o desbanimento."); }
                }
            }
            GUI.EndScrollView();
            string help = GUI.tooltip;
            if (help.Length == 0) help = IsPreview ? (L10n.Portuguese ? "Prévia visual • até 99 pessoas (experimental)." : "Visual preview • up to 99 players (experimental).") : feedback.Length > 0 ? feedback : host ? L10n.T("Expulsar permite voltar. Banir bloqueia a entrada.") : L10n.T("Somente o host pode expulsar ou banir alguém da sala.");
            GUI.Label(new Rect(20, 459, 330, 18), help, small);
        }

        private void Moderate(ICoopRoom room, string id, string name, bool ban, Action<string, bool> removed)
        {
            try
            {
                if (!room.RemovePeer(id, ban)) { feedback = L10n.T("Esse jogador já saiu ou a conexão mudou."); return; }
                feedback = ban ? L10n.T("Jogador banido. A sala continua aberta.") : L10n.T("Jogador expulso. A sala continua aberta.");
                removed(name, ban);
            }
            catch (Exception) { feedback = L10n.T("Não foi possível salvar/aplicar a moderação."); }
        }
        private void DrawPreviewRows()
        {
            for (int i = 0; i < 50; i++)
            {
                string name;
                switch (i % 5)
                {
                    case 0: name = "Ellie da Floresta Com Um Nome Muito Comprido"; break;
                    case 1: name = "Bruxinha das Poções"; break;
                    case 2: name = "Ellie"; break;
                    case 3: name = "Uma Ellie Com Outro Nome Ainda Mais Comprido"; break;
                    default: name = "Ellie de teste " + (i + 1).ToString("00"); break;
                }
                Row(new Rect(0, i * 53, 309, 48), name, L10n.T("Simulada • ") + (i + 1).ToString("00"), 203);
                bool enabled = GUI.enabled;
                GUI.enabled = false;
                IconButton(new Rect(237, i * 53 + 8, 29, 30), kickIcon, L10n.T("Participante simulado"));
                IconButton(new Rect(273, i * 53 + 8, 29, 30), banIcon, L10n.T("Participante simulado"));
                GUI.enabled = enabled;
            }
        }

        private void Row(Rect area, string name, string role, float nameWidth = 230)
        {
            Color old = GUI.color;
            GUI.color *= new Color(0.16f, 0.24f, 0.18f, 0.85f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(area.x + 8, area.y + 4, nameWidth + 10, 23), new GUIContent(ShortName(name, nameWidth), name), nameStyle);
            GUI.Label(new Rect(area.x + 8, area.y + 27, 240, 18), role, small);
        }
        private string ShortName(string value, float width)
        {
            value = ChatMessage.Clean(value, 40);
            var elements = new StringInfo(value);
            if (nameStyle.CalcSize(new GUIContent(value)).x <= width) return value;
            for (int length = elements.LengthInTextElements - 1; length >= 0; length--)
            {
                string result = elements.SubstringByTextElements(0, length) + "…";
                if (nameStyle.CalcSize(new GUIContent(result)).x <= width) return result;
            }
            return "…";
        }
        private bool IconButton(Rect area, Texture2D icon, string tooltip)
        {
            bool clicked = GUI.Button(area, new GUIContent("", tooltip), button);
            GUI.DrawTexture(new Rect(area.x + 6, area.y + 7, 16, 16), icon);
            return clicked;
        }
        private static Texture2D MakeIcon(bool ban)
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(7.5f, 7.5f));
                    bool filled = ban ? radius >= 5 && radius <= 7 || radius < 6 && Mathf.Abs(x + y - 15) < 2
                        : x >= 2 && x <= 3 && y >= 2 && y <= 13 || x >= 2 && x <= 8 && (y == 2 || y == 13)
                        || x >= 6 && x <= 13 && (y == 7 || y == 8) || x >= 10 && x <= 13 && Mathf.Abs(y - 7.5f) <= 14 - x;
                    texture.SetPixel(x, y, filled ? (ban ? new Color(0.95f, 0.56f, 0.51f) : new Color(0.93f, 0.85f, 0.59f)) : Color.clear);
                }
            texture.Apply(false, true);
            texture.filterMode = FilterMode.Point;
            return texture;
        }
        public void Dispose()
        {
            if (kickIcon != null) UnityEngine.Object.Destroy(kickIcon);
            if (banIcon != null) UnityEngine.Object.Destroy(banIcon);
        }
    }
}



