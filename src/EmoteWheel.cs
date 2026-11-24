using System;
using UnityEngine;

namespace LittleWitchCoop
{
    internal sealed class EmoteWheel : IDisposable
    {
        private EmojiRenderer emojis;
        private AudioSource audio;
        private AudioClip chime;
        private AudioClip customSound;
        private GUIStyle label;
        private float progress;
        private float nextEmote;
        private Vector2 anchor;
        private string scene;
        public bool IsOpen { get; private set; }

        public void Tick(bool connected, bool anotherPanel, Vector3? head)
        {
            Camera camera = Camera.main;
            bool available = connected && !anotherPanel && head != null && camera != null;
            if (available)
            {
                Vector3 screen = camera.WorldToScreenPoint(head.Value);
                available = screen.z > 0 && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                if (available) anchor = new Vector2(screen.x, Screen.height - screen.y + 68 * Scale);
            }
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!available || (IsOpen && scene != currentScene)) Close();
            else if (Input.GetKeyDown(KeyCode.B))
            {
                if (IsOpen) Close();
                else if (Time.realtimeSinceStartup >= nextEmote) { IsOpen = true; scene = currentScene; }
            }
            else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
            progress = Mathf.MoveTowards(progress, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.16f);
        }

        private static float Scale { get { return Mathf.Clamp(Screen.height / 900f, 0.75f, 1.5f); } }
        public void Close() { IsOpen = false; }

        public string Draw()
        {
            if (progress <= 0) return null;
            if (emojis == null) emojis = new EmojiRenderer();
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, richText = false };
                label.normal.textColor = new Color(0.96f, 0.94f, 0.83f);
            }
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            int oldDepth = GUI.depth;
            string selected = null;
            try
            {
                float scale = Scale;
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                GUI.depth = -12000;
                GUI.color = new Color(1, 1, 1, progress);
                GUI.enabled = IsOpen && progress > 0.85f;
                Vector2 center = anchor / scale;
                // Keep every choice on screen when Ellie is close to an edge.
                center.x = Mathf.Clamp(center.x, 148, Screen.width / scale - 148);
                center.y = Mathf.Clamp(center.y, 145, Screen.height / scale - 185);
                float eased = 1f - Mathf.Pow(1f - progress, 3);
                float radius = 105 * eased;
                if (Event.current.type == EventType.Repaint)
                {
                    for (int i = 0; i < 72; i++)
                    {
                        float angle = i * Mathf.PI * 2 / 72;
                        Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        PixelBox(new Rect(p.x - 3, p.y - 3, 6, 6), new Color(0.2f, 0.33f, 0.24f, 0.78f));
                    }
                }
                int hover = -1;
                for (int i = 0; i < EmoteChoices.Icons.Length; i++)
                {
                    float angle = -Mathf.PI / 2 + i * Mathf.PI / 4;
                    Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    Rect button = new Rect(Mathf.Round(point.x - 25), Mathf.Round(point.y - 25), 50, 50);
                    bool over = button.Contains(Event.current.mousePosition);
                    if (over) hover = i;
                    if (GUI.Button(button, GUIContent.none, GUIStyle.none)) selected = EmoteChoices.Icons[i];
                    if (Event.current.type == EventType.Repaint)
                    {
                        PixelBox(button, over ? new Color(0.93f, 0.83f, 0.48f) : new Color(0.43f, 0.58f, 0.42f));
                        PixelBox(new Rect(button.x + 3, button.y + 3, 44, 44), new Color(0.055f, 0.11f, 0.085f, 0.97f));
                        emojis.Icon(new Rect(button.x + 9, button.y + 9, 32, 32), EmoteChoices.Icons[i]);
                    }
                }
                if (Event.current.type == EventType.Repaint)
                    PixelBox(new Rect(center.x - 147, center.y + 136, 294, 44), new Color(0.055f, 0.11f, 0.085f, 0.94f));
                GUI.Label(new Rect(center.x - 146, center.y + 136, 292, 22), hover >= 0 ? L10n.T(EmoteChoices.Names[hover]) : L10n.T("Escolha um emote"), label);
                GUI.Label(new Rect(center.x - 146, center.y + 156, 292, 22), L10n.T("B / Esc fecha • clique para escolher"), label);
                if (selected != null) { nextEmote = Time.realtimeSinceStartup + 0.8f; Close(); }
                if (IsOpen && Event.current.isMouse) Event.current.Use();
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
                GUI.depth = oldDepth;
            }
            return selected;
        }

        private static void PixelBox(Rect area, Color color)
        {
            Color old = GUI.color;
            GUI.color *= color;
            GUI.DrawTexture(new Rect(area.x + 3, area.y, area.width - 6, area.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x, area.y + 3, area.width, area.height - 6), Texture2D.whiteTexture);
            GUI.color = old;
        }

        public void PlaySound(string emoji)
        {
            if (audio == null)
            {
                var holder = new GameObject("LittleWitchCoop.EmoteChime");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                audio = holder.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 0;
                audio.volume = 0.18f;
                const int rate = 22050;
                var samples = new float[(int)(rate * 0.24f)];
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = (double)i / rate;
                    double attack = Math.Min(1, t / 0.008);
                    double envelope = attack * Math.Exp(-20 * t) * Math.Min(1, (0.24 - t) / 0.02);
                    double bell = Math.Sin(2 * Math.PI * 1046.5 * t) + 0.35 * Math.Sin(2 * Math.PI * 1568 * t);
                    samples[i] = (float)(bell * envelope * 0.45);
                }
                chime = AudioClip.Create("Little Witch Co-op • plim", samples.Length, 1, rate, false);
                chime.SetData(samples, 0);
            }
            audio.Stop();
            if (customSound != null) { UnityEngine.Object.Destroy(customSound); customSound = null; }
            int index = Array.IndexOf(EmoteChoices.Icons, emoji);
            if (index < 0) return;
            string folder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "LittleWitchCoop", "EmoteSounds");
            string path = System.IO.Path.Combine(folder, EmoteChoices.SoundFiles[index]);
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                if (System.IO.File.Exists(path))
                {
                    if (new System.IO.FileInfo(path).Length > 2097152) throw new System.IO.InvalidDataException("WAV deve ter ate 2 MB.");
                    int channels, rate;
                    float[] samples = EmoteWav.Decode(System.IO.File.ReadAllBytes(path), out channels, out rate);
                    customSound = AudioClip.Create(EmoteChoices.SoundFiles[index], samples.Length / channels, channels, rate, false);
                    if (!customSound.SetData(samples, 0)) throw new System.IO.InvalidDataException("Nao foi possivel carregar as amostras.");
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[LittleWitchCoop] Som personalizado ignorado: " + path + " — " + error.Message);
                if (customSound != null) { UnityEngine.Object.Destroy(customSound); customSound = null; }
            }
            audio.PlayOneShot(customSound != null ? customSound : chime);
        }

        public void Dispose()
        {
            if (emojis != null) emojis.Dispose();
            if (audio != null) UnityEngine.Object.Destroy(audio.gameObject);
            if (chime != null) UnityEngine.Object.Destroy(chime);
            if (customSound != null) UnityEngine.Object.Destroy(customSound);
        }
    }
}


