using System;
using GuildTactics.HexGrid;
using UnityEngine;

namespace GuildTactics.Units
{
    /// <summary>Sprite-sheet presentation with a procedural fallback for unknown content.</summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const int TextureSize = CryptPixelArt.Size;
        private const float NormalScale = 1f;
        private const float SelectedScale = 1f;
        private SpriteRenderer unitRenderer;
        private int lastHealth;
        private float hitUntil, movingUntil;
        private Sprite sprite;
        private Texture2D texture;
        private Camera worldCamera;
        private PlayerUnitController controller;
        private UnitSpriteSheet sheet;
        private float actionStarted = -10f, hitStarted;
        private UnitSpriteSheet.Pose actionPose;
        public UnitRuntimeState State { get; private set; }

        public void Initialize(UnitRuntimeState state, HexLayout layout, Color color, Camera camera = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            worldCamera = camera;
            controller = GetComponentInParent<PlayerUnitController>();
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (unitRenderer != null) throw new InvalidOperationException("Unit view is already initialized.");

            sheet = UnitSpriteSheet.Load(state.Definition.Id);
            if (sheet == null)
            {
                texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                {
                    name = state.Definition.DisplayName + " Placeholder", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = CryptPixelArt.Draw(state.Definition.Id, color);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize),
                    new Vector2(0.5f, 0.5f), TextureSize);
                sprite.name = state.Definition.DisplayName + " Placeholder";
            }
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            unitRenderer = renderer;
            lastHealth = state.CurrentHealth;
            renderer.sprite = sheet != null ? sheet.Frame(UnitSpriteSheet.Pose.Idle, 0) : sprite;
            renderer.sortingOrder = 10;
            transform.localScale = Vector3.one * NormalScale;
            SnapTo(layout.ToWorld(state.Position));
        }

        public void SetSelected(bool selected) =>
            transform.localScale = Vector3.one * (selected ? SelectedScale : NormalScale);

        public void SetWorldPosition(Vector3 position)
        {
            float dx = position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.0001f) unitRenderer.flipX = dx < 0;
            movingUntil = Time.unscaledTime + 0.08f;
            transform.position = WithUnitDepth(position);
        }
        public void SnapTo(Vector3 position) => transform.position = WithUnitDepth(position);

        public void PlayAction(Vector3 target, bool defend = false)
        {
            if (Mathf.Abs(target.x - transform.position.x) > 0.001f)
                unitRenderer.flipX = target.x < transform.position.x;
            actionPose = defend ? UnitSpriteSheet.Pose.Defend : UnitSpriteSheet.Pose.Attack;
            actionStarted = Time.unscaledTime;
        }

        private static Vector3 WithUnitDepth(Vector3 position) => new Vector3(position.x, position.y, -0.2f);

        private void Update()
        {
            if (State == null || unitRenderer == null) return;
            if (State.CurrentHealth < lastHealth)
            { hitStarted = Time.unscaledTime; hitUntil = hitStarted + 0.4f; }
            lastHealth = State.CurrentHealth;
            unitRenderer.color = Time.unscaledTime < hitUntil ? new Color(1f, 0.4f, 0.35f) : Color.white;
            if (sheet != null)
            {
                float now = Time.unscaledTime;
                unitRenderer.sprite = now < hitUntil ? sheet.Frame(UnitSpriteSheet.Pose.Hit, now - hitStarted, false)
                    : now - actionStarted < 0.8f ? sheet.Frame(actionPose, now - actionStarted, false)
                    : sheet.Frame(now < movingUntil ? UnitSpriteSheet.Pose.Walk : UnitSpriteSheet.Pose.Idle, now);
            }
        }

        private void OnGUI()
        {
            if (State == null || !State.IsAlive || worldCamera == null) return;
            if (controller != null && controller.Expedition?.Result != null) return;
            var screen = worldCamera.WorldToScreenPoint(transform.position);
            if (screen.z <= 0) return;
            float pixelsPerUnit = worldCamera.pixelHeight / (2f * worldCamera.orthographicSize);
            float width = Mathf.Clamp(pixelsPerUnit * 1.25f, 8, 44);
            float y = Screen.height - screen.y;
            // Zoomed maps contain offscreen units; their bars must not paint over the HUD.
            if (screen.x < 0 || screen.x > Screen.width || y < HexGridInteraction.HudHeight ||
                y + pixelsPerUnit * 0.45f + 21 > Screen.height - 32) return;
            var previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(screen.x - width / 2, y + pixelsPerUnit * 0.45f, width, 5), Texture2D.whiteTexture);
            GUI.color = State.Team == UnitTeam.Player ? new Color(0.3f, 0.9f, 0.45f) : new Color(1f, 0.45f, 0.3f);
            GUI.DrawTexture(new Rect(screen.x - width / 2 + 1, y + pixelsPerUnit * 0.45f + 1,
                (width - 2) * State.CurrentHealth / State.Definition.MaxHealth, 3), Texture2D.whiteTexture);
            GUI.color = previous;
            // Exact HP remains in the active-unit panel; small cells use non-overlapping bars.
            if (pixelsPerUnit >= 20)
                GUI.Label(new Rect(screen.x - width / 2, y + pixelsPerUnit * 0.45f + 5, width, 16),
                    $"{State.CurrentHealth}/{State.Definition.MaxHealth}",
                    new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
        }

        private void OnDestroy()
        {
            sheet?.Dispose();
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
