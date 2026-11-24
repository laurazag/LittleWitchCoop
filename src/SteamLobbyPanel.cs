using System;
using System.Globalization;
using Steamworks;
using UnityEngine;

namespace LittleWitchCoop
{
    internal sealed class SteamLobbyPanel
    {
        public string RoomName = "";
        public bool PublicRoom = true;
        public int Capacity = 2;
        private readonly CapacityButton decrease = new CapacityButton(), increase = new CapacityButton();
        private GUIStyle label;
        private GUIStyle small;
        private GUIStyle button;
        private Vector2 scroll;
        private string feedback = "";
        private bool preview;
        private static readonly SteamLobbyEntry[] Examples = {
            new SteamLobbyEntry { Name = "Clareira das estrelinhas", Members = 1, Capacity = 2, Ping = 28 },
            new SteamLobbyEntry { Name = "Chá e poções com as amigas", Members = 1, Capacity = 2, Ping = 65 },
            new SteamLobbyEntry { Name = "Cogumelos ao luar", Members = 2, Capacity = 2, Ping = 42 },
            new SteamLobbyEntry { Name = "Uma sala com um nome bem comprido na floresta", Members = 1, Capacity = 2, Ping = 134 },
            new SteamLobbyEntry { Name = "Cantinho das flores", Members = 1, Capacity = 2, Ping = -1 },
            new SteamLobbyEntry { Name = "Bruxinhas da madrugada", Members = 2, Capacity = 2, Ping = 210 },
            new SteamLobbyEntry { Name = "Uma flor pra você", Members = 1, Capacity = 2, Ping = 19 },
            new SteamLobbyEntry { Name = "Casa da Ellie", Members = 1, Capacity = 2, Ping = 87 }
        };

        public void Draw(SteamLobbyBrowser browser, SteamRoom steamRoom, bool busy, Action create, Action<CSteamID> join)
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false, wordWrap = false };
                label.normal.textColor = new Color(0.91f, 0.94f, 0.86f);
                small = new GUIStyle(label) { fontSize = 11, wordWrap = true };
                small.normal.textColor = new Color(0.69f, 0.79f, 0.7f);
                button = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            }
            GUI.Label(new Rect(20, 136, 207, 20), L10n.Portuguese ? "Nome da sala" : "Room name", small);
            bool capacityEnabled = GUI.enabled;
            GUI.enabled = capacityEnabled && !busy;
            Capacity = decrease.Draw(new Rect(226, 133, 25, 23), "-", button, Capacity, -1);
            GUI.Label(new Rect(255, 136, 64, 20), (L10n.Portuguese ? "Até " : "Max ") + Capacity, small);
            Capacity = increase.Draw(new Rect(325, 133, 25, 23), "+", button, Capacity, 1);
            GUI.enabled = capacityEnabled;
            RoomName = GUI.TextField(new Rect(20, 157, 330, 27), RoomName, 48);
            PublicRoom = GUI.Toggle(new Rect(20, 193, 223, 24), PublicRoom, L10n.T("Pública • aparecer na lista"));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && browser != null && steamRoom != null && !busy && !steamRoom.Pending;
            if (GUI.Button(new Rect(252, 189, 98, 29), L10n.T("Criar sala"), button)) { feedback = ""; create(); }
            bool privateHost = steamRoom != null && steamRoom.RoomCode.Length > 0;
            GUI.enabled = enabled && browser != null && !browser.Searching;
            if (GUI.Button(new Rect(252, privateHost ? 405 : 224, 98, 28), L10n.T("Atualizar"), button)) { feedback = ""; preview = false; scroll = Vector2.zero; browser.Refresh(); }
            GUI.enabled = enabled;
            if (privateHost) DrawPrivateCode(steamRoom.RoomCode);
            if (!privateHost) GUI.Label(new Rect(20, 224, 227, 29), preview ? L10n.T("Prévia • salas e pings simulados") : browser == null ? L10n.T("Aguardando a Steam...") : browser.Status, small);
            GUI.Label(new Rect(25, privateHost ? 303 : 261, 150, 20), L10n.T("Sala"), small);
            GUI.Label(new Rect(172, privateHost ? 303 : 261, 46, 20), L10n.T("Vagas"), small);
            GUI.Label(new Rect(218, privateHost ? 303 : 261, 50, 20), "Ping", small);
            int count = preview ? Examples.Length : browser == null ? 0 : browser.Entries.Count;
            scroll = GUI.BeginScrollView(new Rect(20, privateHost ? 326 : 284, 330, privateHost ? 77 : 144), scroll, new Rect(0, 0, 309, Mathf.Max(privateHost ? 77 : 144, count * 36)));
            if (count == 0) GUI.Label(new Rect(5, 6, 299, 40), browser != null && browser.Searching ? L10n.T("Procurando clareiras abertas...") : L10n.T("Clique em Atualizar para buscar salas."), small);
            for (int i = 0; i < count; i++)
            {
                SteamLobbyEntry entry = preview ? Examples[i] : browser.Entries[i];
                float y = i * 36;
                GUI.Label(new Rect(5, y + 4, 145, 25), new GUIContent(ShortName(entry.Name, 139), entry.Name), label);
                GUI.Label(new Rect(152, y + 4, 42, 25), entry.Members + "/" + entry.Capacity, small);
                string ping = entry.IsLocal ? L10n.T("Você") : entry.Ping >= 0 ? entry.Ping + " ms" : entry.Nonce != 0 ? "…" : "—";
                GUI.Label(new Rect(196, y + 4, 54, 25), ping, small);
                GUI.enabled = enabled && !preview && !busy && !entry.IsLocal && entry.Members < entry.Capacity;
                if (GUI.Button(new Rect(251, y + 2, 54, 28), L10n.T("Entrar"), button))
                {
                    // Steam validates actual ownership, capacity and admission on join.
                    if (SteamMatchmaking.GetLobbyData(entry.Lobby, "lwcoop") != SteamRoom.Protocol
                        || SteamMatchmaking.GetLobbyData(entry.Lobby, "lwcoop-public") != "1")
                        feedback = L10n.T("Essa sala mudou ou ficou cheia. Atualize a lista.");
                    else { feedback = ""; join(entry.Lobby); }
                }
                GUI.enabled = enabled;
            }
            GUI.EndScrollView();
#if DEVELOPMENT_BUILD
            if (GUI.Button(new Rect(20, 434, 330, 25), preview ? L10n.T("Voltar para salas reais") : L10n.T("Ver prévia de salas"), button))
            {
                preview = !preview;
                scroll = Vector2.zero;
            }
#endif
            string help = GUI.tooltip;
            if (help.Length == 0) help = preview ? L10n.T("Exemplos visuais • entrar desativado nesta prévia.") : feedback.Length > 0 ? feedback : busy ? L10n.T("Saia da sala atual pela aba Conexão para entrar em outra.") : L10n.T("Ping —: sem resposta. Privadas: por convite Steam.");
            if (privateHost) GUI.Label(new Rect(20, 405, 227, 29), browser == null ? "" : browser.Status, small);
            GUI.Label(new Rect(20, 461, 330, 17), help, small);
        }

        private float copiedUntil;
        private void DrawPrivateCode(string code)
        {
            GUI.Label(new Rect(20, 222, 223, 19), L10n.Portuguese ? "Código da sua sala privada" : "Your private room code", small);
            Rect field = new Rect(20, 245, 330, 30);
            GUI.Box(field, GUIContent.none);
            GUI.Label(new Rect(field.x + 7, field.y + 4, field.width - 43, 23), code, label);
            Rect copy = new Rect(field.xMax - 33, field.y + 2, 31, field.height - 4);
            if (GUI.Button(copy, new GUIContent("", L10n.Portuguese ? "Copiar código" : "Copy code"), button))
            { GUIUtility.systemCopyBuffer = code; copiedUntil = Time.realtimeSinceStartup + 2; }
            if (Event.current.type == EventType.Repaint)
            {
                Color old = GUI.color; GUI.color = new Color(0.94f, 0.94f, 0.83f, old.a);
                GUI.DrawTexture(new Rect(copy.x + 9, copy.y + 6, 10, 13), Texture2D.whiteTexture);
                GUI.color = new Color(0.10f, 0.16f, 0.12f, old.a);
                GUI.DrawTexture(new Rect(copy.x + 10, copy.y + 7, 8, 11), Texture2D.whiteTexture);
                GUI.color = new Color(0.94f, 0.94f, 0.83f, old.a);
                GUI.DrawTexture(new Rect(copy.x + 13, copy.y + 10, 10, 13), Texture2D.whiteTexture);
                GUI.color = new Color(0.10f, 0.16f, 0.12f, old.a);
                GUI.DrawTexture(new Rect(copy.x + 14, copy.y + 11, 8, 11), Texture2D.whiteTexture); GUI.color = old;
            }
            GUI.Label(new Rect(20, 278, 330, 23), Time.realtimeSinceStartup < copiedUntil ? (L10n.Portuguese ? "Código copiado!" : "Code copied!") : (L10n.Portuguese ? "Envie para a pessoa colar em Conexão > Código." : "Share it to paste in Connection > Code."), small);
        }

        private string ShortName(string value, float width)
        {
            if (label.CalcSize(new GUIContent(value)).x <= width) return value;
            var elements = new StringInfo(value);
            for (int i = elements.LengthInTextElements - 1; i >= 0; i--)
            {
                string text = elements.SubstringByTextElements(0, i) + "…";
                if (label.CalcSize(new GUIContent(text)).x <= width) return text;
            }
            return "…";
        }
    }
}








