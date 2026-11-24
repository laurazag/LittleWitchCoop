using UnityEngine;
namespace LittleWitchCoop
{
    internal sealed class ModerationAlert
    {
        public bool IsOpen { get; private set; }
        private RemovalReason reason;
        private float openedAt;
        private GUIStyle title, message, button;
        public void Show(RemovalReason value)
        {
            if (value == RemovalReason.None) return;
            reason = value; openedAt = Time.realtimeSinceStartup; IsOpen = true;
        }
        public void Draw()
        {
            if (!IsOpen) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
                title.normal.textColor = Color.white;
                message = new GUIStyle(title) { fontSize = 18, fontStyle = FontStyle.Normal, wordWrap = true };
                button = new GUIStyle(message) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            }
            Matrix4x4 oldMatrix = GUI.matrix; Color oldColor = GUI.color;
            int oldDepth = GUI.depth; bool oldEnabled = GUI.enabled;
            try
            {
                GUI.matrix = Matrix4x4.identity; GUI.color = Color.white; GUI.enabled = true; GUI.depth = -20000;
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.35f));
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.14f);
                float scale = Mathf.Min(Screen.width / 390f, Mathf.Clamp(Screen.height / 900f, 0.75f, 1.5f)) * Mathf.Lerp(0.7f, 1f, 1f - Mathf.Pow(1f - t, 3));
                GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width / 2f, Screen.height / 2f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
                GUI.color = new Color(1, 1, 1, t);
                Fill(new Rect(-180, -102, 360, 204), new Color(0.48f, 0.55f, 0.43f, 0.95f));
                Fill(new Rect(-178, -100, 356, 200), new Color(0, 0, 0, 0.88f));
                GUI.Label(new Rect(-160, -84, 320, 35), L10n.Portuguese ? "ALERTA" : "ALERT", title);
                string text = reason == RemovalReason.Banned ? (L10n.Portuguese ? "Você foi banido(a)!" : "You were banned!") : (L10n.Portuguese ? "Você foi expulso(a) da sala!" : "You were kicked from the room!");
                GUI.Label(new Rect(-155, -35, 310, 62), text, message);
                GUI.enabled = t >= 1;
                Rect confirm = new Rect(-65, 49, 130, 34);
                bool hover = confirm.Contains(Event.current.mousePosition);
                if (GUI.Button(confirm, GUIContent.none, GUIStyle.none)) IsOpen = false;
                Fill(confirm, new Color(0.48f, 0.55f, 0.43f));
                Fill(new Rect(confirm.x + 2, confirm.y + 2, confirm.width - 4, confirm.height - 4), hover ? new Color(0.19f, 0.27f, 0.20f) : new Color(0.10f, 0.16f, 0.12f));
                GUI.Label(confirm, "OK", button);
                if (Event.current.type == EventType.KeyDown && t >= 1 && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter || Event.current.keyCode == KeyCode.Escape)) { IsOpen = false; Event.current.Use(); }
                if (Event.current.isMouse || Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp) Event.current.Use();
            }
            finally { GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
        }
        private static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color *= color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }
    }
}
