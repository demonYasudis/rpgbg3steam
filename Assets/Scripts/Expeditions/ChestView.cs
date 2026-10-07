using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEngine;

namespace GuildTactics.Expeditions
{
    /// <summary>Presentation only; ExpeditionRun remains authoritative for loot and actions.</summary>
    public sealed class ChestView : MonoBehaviour
    {
        private PlayerUnitController controller;
        private SpriteRenderer chestRenderer;
        private Sprite[] frames;
        private bool opened;
        private float openedAt;

        public void Initialize(PlayerUnitController units, HexLayout layout)
        {
            controller = units;
            var texture = Resources.Load<Texture2D>("PropSprites/chest");
            if (texture == null) return; // The objective label remains usable if artwork is missing.
            frames = new Sprite[4];
            float width = texture.width / 4f;
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Sprite.Create(texture, new Rect(i * width, 0, width, texture.height),
                    new Vector2(0.5f, 0.43f), width / 1.1f, 0, SpriteMeshType.FullRect);
            chestRenderer = gameObject.AddComponent<SpriteRenderer>();
            chestRenderer.sortingOrder = 5;
            var position = layout.ToWorld(units.Expedition.Chest);
            transform.position = new Vector3(position.x, position.y, -0.1f);
            opened = units.Expedition.ChestOpened;
            openedAt = Time.unscaledTime - 1f;
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            if (chestRenderer == null || controller == null) return;
            var run = controller.Expedition;
            if (run.ChestOpened && !opened)
            {
                opened = true;
                openedAt = Time.unscaledTime;
            }
            int index = opened ? Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - openedAt) * 6f), 0, 3) : 0;
            chestRenderer.sprite = frames[index];
            chestRenderer.enabled = run.Result == null && run.Mission.RequiresRelic &&
                (controller.Visibility == null || controller.Visibility.IsVisible(run.Chest));
        }

        private void OnDestroy()
        {
            if (frames == null) return;
            foreach (var frame in frames)
                if (Application.isPlaying) Destroy(frame);
                else DestroyImmediate(frame);
        }
    }
}
