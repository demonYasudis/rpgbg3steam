using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Placeholder visuals only; never owns or mutates authoritative cell state.</summary>
    public sealed class HexGridView : MonoBehaviour
    {
        private const float TileFill = 0.94f;
        private const int TextureSize = 128;
        private static readonly Color GroundColor = new Color(0.25f, 0.29f, 0.34f);
        private static readonly Color ReachableColor = new Color(0.27f, 0.48f, 0.35f);
        private static readonly Color HoverColor = new Color(0.35f, 0.68f, 0.76f);
        private static readonly Color SelectedColor = new Color(0.93f, 0.65f, 0.26f);
        private static readonly Color SelectedHoverColor = new Color(1f, 0.84f, 0.46f);
        private readonly Dictionary<HexCoordinates, SpriteRenderer> tiles =
            new Dictionary<HexCoordinates, SpriteRenderer>();
        private Sprite tileSprite;
        private Texture2D tileTexture;
        private HexGrid grid;
        private HexCoordinates? hovered;
        private HexCoordinates? selected;
        private readonly HashSet<HexCoordinates> reachable = new HashSet<HexCoordinates>();
        private readonly HashSet<HexCoordinates> targets = new HashSet<HexCoordinates>();
        private readonly HashSet<HexCoordinates> traps = new HashSet<HexCoordinates>();
        private static readonly Color TargetColor = new Color(0.68f, 0.32f, 0.62f);
        private static readonly Color TrapColor = new Color(0.85f, 0.4f, 0.1f);
        public int TileCount => tiles.Count;

        public void Initialize(HexGrid grid, HexLayout layout)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (tileSprite != null) throw new InvalidOperationException("View is already initialized.");
            this.grid = grid;

            // Unity's default sprite material works in the existing Built-in pipeline.
            // One shared white sprite, no imported art or per-cell material instances.
            var vertices = new Vector2[6];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = layout.Corner(i) * TileFill;
            // Use an alpha silhouette: the renderer may draw a quad even for overridden sprite geometry.
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    var point = new Vector2((x + 0.5f) / TextureSize * 2 - 1,
                        (y + 0.5f) / TextureSize * 2 - 1) * layout.Radius;
                    bool inside = true;
                    for (int edge = 0; edge < vertices.Length; edge++)
                    {
                        var side = vertices[(edge + 1) % vertices.Length] - vertices[edge];
                        var offset = point - vertices[edge];
                        if (side.x * offset.y - side.y * offset.x < 0) { inside = false; break; }
                    }
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            tileTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "Placeholder Hex Texture", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            tileTexture.SetPixels32(pixels);
            tileTexture.Apply(false, true);
            tileSprite = Sprite.Create(tileTexture, new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / (2f * layout.Radius), 0, SpriteMeshType.FullRect);
            tileSprite.name = "Placeholder Hex";

            foreach (var cell in grid.Cells)
            {
                var tile = new GameObject("Hex " + cell.Coordinates, typeof(SpriteRenderer));
                tile.transform.SetParent(transform, false);
                tile.transform.position = layout.ToWorld(cell.Coordinates);
                var renderer = tile.GetComponent<SpriteRenderer>();
                renderer.sprite = tileSprite;
                renderer.color = TerrainColor(cell.Terrain);
                tiles.Add(cell.Coordinates, renderer);
            }
        }

        public void SetHighlights(HexCoordinates? newHovered, HexCoordinates? newSelected)
        {
            var oldHovered = hovered;
            var oldSelected = selected;
            hovered = newHovered;
            selected = newSelected;
            Refresh(oldHovered);
            Refresh(oldSelected);
            Refresh(hovered);
            Refresh(selected);
        }

        public void SetReachableCells(IEnumerable<HexCoordinates> coordinates)
        {
            reachable.Clear();
            if (coordinates != null)
                foreach (var coordinate in coordinates) reachable.Add(coordinate);
            foreach (var coordinate in tiles.Keys) Refresh(coordinate);
        }

        public void SetTargetCells(IEnumerable<HexCoordinates> coordinates) => SetCells(targets, coordinates);
        public void SetTrapCells(IEnumerable<HexCoordinates> coordinates) => SetCells(traps, coordinates);

        public static Color TerrainColor(TerrainType terrain)
        {
            switch (terrain)
            {
                case TerrainType.HighGround: return new Color(0.60f, 0.53f, 0.35f);
                case TerrainType.Blocked: return new Color(0.43f, 0.43f, 0.48f);
                case TerrainType.Pit: return new Color(0.12f, 0.08f, 0.20f);
                default: return GroundColor;
            }
        }

        private void SetCells(HashSet<HexCoordinates> set, IEnumerable<HexCoordinates> coordinates)
        {
            set.Clear();
            if (coordinates != null) foreach (var coordinate in coordinates) set.Add(coordinate);
            foreach (var coordinate in tiles.Keys) Refresh(coordinate);
        }

        private void Refresh(HexCoordinates? coordinate)
        {
            if (!coordinate.HasValue || !tiles.TryGetValue(coordinate.Value, out var tile)) return;
            tile.color = coordinate == selected
                ? (coordinate == hovered ? SelectedHoverColor : SelectedColor)
                : (coordinate == hovered ? HoverColor :
                    (targets.Contains(coordinate.Value) ? TargetColor :
                    (traps.Contains(coordinate.Value) ? TrapColor :
                    (reachable.Contains(coordinate.Value) ? ReachableColor : TerrainColor(grid.GetCell(coordinate.Value).Terrain)))));
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (tileSprite != null) Destroy(tileSprite);
                if (tileTexture != null) Destroy(tileTexture);
            }
            else
            {
                if (tileSprite != null) DestroyImmediate(tileSprite);
                if (tileTexture != null) DestroyImmediate(tileTexture);
            }
        }
    }
}
