using System;
using GuildTactics.HexGrid;
using UnityEngine;

namespace GuildTactics.Units
{
    /// <summary>Procedural placeholder presentation for one unit.</summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const int TextureSize = 64;
        private const float NormalScale = 0.58f;
        private const float SelectedScale = 0.72f;
        private Sprite sprite;
        private Texture2D texture;
        private Camera worldCamera;
        public UnitRuntimeState State { get; private set; }

        public void Initialize(UnitRuntimeState state, HexLayout layout, Color color, Camera camera = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            worldCamera = camera;
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (sprite != null) throw new InvalidOperationException("Unit view is already initialized.");

            texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = state.Definition.DisplayName + " Placeholder", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[TextureSize * TextureSize];
            float radius = TextureSize * 0.46f;
            Vector2 center = Vector2.one * (TextureSize - 1) * 0.5f;
            Color32 fill = color;
            Color32 border = new Color32(28, 31, 38, 255);
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * TextureSize + x] = distance <= radius
                        ? (distance >= radius - 5 ? border : fill)
                        : new Color32(0, 0, 0, 0);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize);
            sprite.name = state.Definition.DisplayName + " Placeholder";
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 10;
            transform.localScale = Vector3.one * NormalScale;
            SnapTo(layout.ToWorld(state.Position));
        }

        public void SetSelected(bool selected) =>
            transform.localScale = Vector3.one * (selected ? SelectedScale : NormalScale);

        public void SetWorldPosition(Vector3 position) => transform.position = WithUnitDepth(position);
        public void SnapTo(Vector3 position) => transform.position = WithUnitDepth(position);

        private static Vector3 WithUnitDepth(Vector3 position) => new Vector3(position.x, position.y, -0.2f);

        private void OnGUI()
        {
            if (State == null || !State.IsAlive || worldCamera == null) return;
            var screen = worldCamera.WorldToScreenPoint(transform.position);
            if (screen.z <= 0) return;
            GUI.Label(new Rect(screen.x - 32, Screen.height - screen.y + 12, 80, 24),
                $"{State.CurrentHealth}/{State.Definition.MaxHealth}");
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (sprite != null) Destroy(sprite);
                if (texture != null) Destroy(texture);
            }
            else
            {
                if (sprite != null) DestroyImmediate(sprite);
                if (texture != null) DestroyImmediate(texture);
            }
        }
    }
}
