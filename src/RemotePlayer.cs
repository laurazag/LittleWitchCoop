using System.Collections.Generic;
using UnityEngine;

namespace LittleWitchCoop
{
    internal sealed class RemotePlayer
    {
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> localRenderers = new List<SpriteRenderer>();
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private GameObject root;
        private float nextSpriteScan;
        public string Status = L10n.T("Carregue um save para encontrar a Ellie.");

        public static Vector3? LocalHead()
        {
            GameObject player = GameObject.FindWithTag("Player");
            return player == null ? (Vector3?)null : Head(player.GetComponentsInChildren<SpriteRenderer>(), player.transform.position);
        }

        public Vector3? RemoteHead()
        {
            if (root == null || !root.activeSelf || renderers.Count == 0) return null;
            return Head(renderers.ToArray(), renderers[0].transform.position);
        }

        private static Vector3 Head(SpriteRenderer[] layers, Vector3 origin)
        {
            float top = origin.y + 1f;
            foreach (SpriteRenderer layer in layers)
                if (layer != null && layer.enabled && layer.sprite != null) top = Mathf.Max(top, layer.bounds.max.y);
            return new Vector3(origin.x, Mathf.Min(top, origin.y + 2.5f) + 0.15f, origin.z);
        }

        private static string SpriteKey(Sprite sprite)
        {
            return sprite.texture.name + "/" + sprite.name;
        }

        public PlayerSnapshot Capture()
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) return new PlayerSnapshot();
            var poses = new List<SpritePose>();
            localRenderers.Clear();
            localRenderers.AddRange(player.GetComponentsInChildren<SpriteRenderer>(true));
            foreach (SpriteRenderer renderer in localRenderers)
            {
                if (!renderer.gameObject.activeInHierarchy || !renderer.enabled || renderer.sprite == null || renderer.sprite.texture == null || poses.Count == 16) continue;
                Sprite sprite = renderer.sprite;
                string key = SpriteKey(sprite);
                sprites[key] = sprite;
                Vector3 p = renderer.transform.position;
                Quaternion r = renderer.transform.rotation;
                Vector3 s = renderer.transform.lossyScale;
                poses.Add(new SpritePose
                {
                    Key = key,
                    Transform = new[] { p.x, p.y, p.z, r.x, r.y, r.z, r.w, s.x, s.y, s.z },
                    FlipX = renderer.flipX,
                    FlipY = renderer.flipY,
                    SortingLayer = renderer.sortingLayerID,
                    SortingOrder = renderer.sortingOrder
                });
            }
            Status = poses.Count == 0 ? L10n.T("Ellie encontrada. Aguardando sprites visíveis.") : L10n.T("Ellie local encontrada • ") + poses.Count + L10n.T(" camadas");
            return new PlayerSnapshot { Scene = player.scene.name, Sprites = poses.ToArray() };
        }

        public void Render(PlayerSnapshot remote, PlayerSnapshot local)
        {
            bool sameArea = local.Sprites.Length > 0 && remote.Sprites.Length > 0 && local.Scene == remote.Scene;
            if (!sameArea)
            {
                if (root != null) root.SetActive(false);
                if (remote.Sprites.Length > 0 && local.Scene != remote.Scene) Status = L10n.T("Convidado em outra cena.");
                return;
            }
            if (root == null)
            {
                root = new GameObject("LittleWitchCoop.RemoteEllie");
                Object.DontDestroyOnLoad(root);
            }
            bool wasHidden = !root.activeSelf;
            root.SetActive(true);
            bool missingSprites = false;
            foreach (SpritePose pose in remote.Sprites)
            {
                Sprite cached;
                if (!sprites.TryGetValue(pose.Key, out cached) || cached == null) missingSprites = true;
            }
            if (missingSprites && Time.realtimeSinceStartup >= nextSpriteScan)
            {
                nextSpriteScan = Time.realtimeSinceStartup + 1f;
                foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite != null && sprite.texture != null) sprites[SpriteKey(sprite)] = sprite;
            }
            while (renderers.Count < remote.Sprites.Length)
            {
                var layer = new GameObject("RemoteSprite");
                layer.transform.SetParent(root.transform, false);
                renderers.Add(layer.AddComponent<SpriteRenderer>());
            }
            int visible = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                SpriteRenderer renderer = renderers[i];
                renderer.enabled = false;
                if (i >= remote.Sprites.Length) continue;
                SpritePose pose = remote.Sprites[i];
                Sprite sprite;
                if (!sprites.TryGetValue(pose.Key, out sprite) || sprite == null) continue;
                float[] t = pose.Transform;
                Vector3 position = new Vector3(t[0], t[1], t[2]);
                bool snap = wasHidden || renderer.sprite == null || Vector3.Distance(renderer.transform.position, position) > 5f;
                renderer.transform.position = snap ? position : Vector3.Lerp(renderer.transform.position, position, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
                renderer.transform.rotation = new Quaternion(t[3], t[4], t[5], t[6]);
                renderer.transform.localScale = new Vector3(t[7], t[8], t[9]);
                renderer.sprite = sprite;
                // Animation can enable different layers on each player. An array
                // index is not a material identity (a shadow can inherit body white).
                SpriteRenderer template = FindTemplate(sprite);
                if (template != null)
                {
                    renderer.sharedMaterial = template.sharedMaterial;
                    renderer.color = template.color;
                    properties.Clear();
                    template.GetPropertyBlock(properties);
                    renderer.SetPropertyBlock(properties);
                }
                else continue; // Do not display an unknown effect with default white material.
                renderer.flipX = pose.FlipX;
                renderer.flipY = pose.FlipY;
                renderer.sortingLayerID = pose.SortingLayer;
                renderer.sortingOrder = pose.SortingOrder;
                renderer.enabled = true;
                visible++;
            }
            Status = visible > 0 ? L10n.T("Ellie convidada visível • movimento recebido") : L10n.T("Aguardando sprites da convidada nesta área.");
        }

        public void Clear()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            renderers.Clear();
            localRenderers.Clear();
            sprites.Clear();
        }

        private SpriteRenderer FindTemplate(Sprite sprite)
        {
            SpriteRenderer sameTexture = null;
            foreach (SpriteRenderer candidate in localRenderers)
            {
                if (candidate == null || candidate.sprite == null) continue;
                if (candidate.sprite == sprite) return candidate;
                if (candidate.sprite.texture == sprite.texture && sameTexture == null) sameTexture = candidate;
            }
            return sameTexture;
        }
    }
}

