using System;
using System.Collections.Generic;
using UnityEngine;
using GuildTactics.Visibility;
using GuildTactics.Generation;

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
        private readonly Dictionary<TerrainType, Sprite> terrainSprites = new Dictionary<TerrainType, Sprite>();
        private readonly List<Texture2D> terrainTextures = new List<Texture2D>();
        private HexGrid grid;
        private HexCoordinates? hovered;
        private HexCoordinates? selected;
        private readonly HashSet<HexCoordinates> reachable = new HashSet<HexCoordinates>();
        private readonly HashSet<HexCoordinates> targets = new HashSet<HexCoordinates>();
        private readonly HashSet<HexCoordinates> traps = new HashSet<HexCoordinates>();
        private readonly HashSet<HexCoordinates> danger = new HashSet<HexCoordinates>();
        public void SetDangerCells(IEnumerable<HexCoordinates> coordinates) => SetCells(danger, coordinates);
        private static readonly Color TargetColor = new Color(0.68f, 0.32f, 0.62f);
        private static readonly Color TrapColor = new Color(0.85f, 0.4f, 0.1f);
        public int TileCount => tiles.Count;
        public DungeonBiome Biome { get; private set; }
        public FogOfWarSystem Visibility { get; private set; }
        public bool DebugVisibility { get; private set; }

        public void SetFog(FogOfWarSystem visibility)
        {
            if (visibility != null && !ReferenceEquals(visibility.Grid, grid))
                throw new ArgumentException("View and vision must use the same grid.", nameof(visibility));
            Visibility = visibility;
            RefreshAll();
        }

        public void ToggleVisibilityDebug()
        {
            DebugVisibility = !DebugVisibility;
            RefreshAll();
        }

        public void RefreshAll()
        {
            foreach (var coordinate in tiles.Keys) Refresh(coordinate);
        }

        public void Initialize(HexGrid grid, HexLayout layout, DungeonBiome biome = DungeonBiome.Crypt)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (tileSprite != null) throw new InvalidOperationException("View is already initialized.");
            Biomes.Validate(biome);
            Biome = biome;
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
            foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
            {
                var textured = (Color32[])pixels.Clone();
                for (int y = 0; y < TextureSize; y++)
                    for (int x = 0; x < TextureSize; x++)
                    {
                        int px = x / 4, py = y / 4;
                        bool mortar = py % 8 == 0 || (px + (py / 8 % 2) * 5) % 11 == 0;
                        byte shade = mortar ? (byte)130 : (byte)(220 + (px * 7 + py * 11) % 3 * 12);
                        if (terrain == TerrainType.Blocked) shade = py % 6 == 0 || (px + py / 6 * 4) % 9 == 0 ? (byte)95 : (byte)245;
                        if (terrain == TerrainType.HighGround) shade = py % 5 == 0 ? (byte)130 : (byte)245;
                        if (terrain == TerrainType.Pit) shade = px < 5 || px > 26 || py < 5 || py > 26 ? (byte)220 : (byte)55;
                        if (Biome == DungeonBiome.FloodedCellar)
                        {
                            // Dry planks and rivets, wet masonry, raised stone platforms and deep-water ripples.
                            switch (terrain)
                            {
                                case TerrainType.Ground:
                                    shade = py % 6 == 0 ? (byte)100 : (byte)(205 + (px + py / 6) % 3 * 15);
                                    if ((px == 5 || px == 26) && py % 6 == 2) shade = 130;
                                    break;
                                case TerrainType.Blocked:
                                    shade = py % 7 == 0 || (px + py / 7 * 3) % 10 == 0 ? (byte)100 : (byte)230;
                                    if ((px * 3 + py * 5) % 17 < 3) shade = 150;
                                    break;
                                case TerrainType.HighGround:
                                    shade = py < 5 || py > 26 || px < 5 || px > 26 ? (byte)105 : (byte)240;
                                    if (py % 9 == 0) shade = 180;
                                    break;
                                case TerrainType.Pit:
                                    shade = py % 7 == (px / 8 % 2) && px % 8 < 6 ? (byte)230 : (byte)105;
                                    break;
                            }
                        }
                        textured[y * TextureSize + x] = new Color32(shade, shade, shade, pixels[y * TextureSize + x].a);
                    }
                var art = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                { name = Biome + " " + terrain, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                art.SetPixels32(textured); art.Apply(false, true); terrainTextures.Add(art);
                terrainSprites.Add(terrain, Sprite.Create(art, new Rect(0, 0, TextureSize, TextureSize),
                    new Vector2(0.5f, 0.5f), TextureSize / (2f * layout.Radius), 0, SpriteMeshType.FullRect));
            }

            foreach (var cell in grid.Cells)
            {
                var tile = new GameObject("Hex " + cell.Coordinates, typeof(SpriteRenderer));
                tile.transform.SetParent(transform, false);
                tile.transform.position = layout.ToWorld(cell.Coordinates);
                var renderer = tile.GetComponent<SpriteRenderer>();
                renderer.sprite = terrainSprites[cell.Terrain];
                renderer.color = TerrainColor(cell.Terrain, Biome);
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

        public static Color TerrainColor(TerrainType terrain, DungeonBiome biome = DungeonBiome.Crypt)
        {
            if (biome == DungeonBiome.FloodedCellar)
            {
                switch (terrain)
                {
                    case TerrainType.HighGround: return new Color(0.48f, 0.59f, 0.53f);
                    case TerrainType.Blocked: return new Color(0.24f, 0.40f, 0.40f);
                    case TerrainType.Pit: return new Color(0.10f, 0.28f, 0.39f);
                    default: return new Color(0.43f, 0.39f, 0.29f);
                }
            }
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
            tile.sprite = terrainSprites[grid.GetCell(coordinate.Value).Terrain];
            if (Visibility != null)
            {
                var state = Visibility.GetState(coordinate.Value);
                if (DebugVisibility)
                {
                    tile.sprite = tileSprite;
                    tile.color = state == CellVisibility.Visible ? new Color(0.2f, 0.7f, 0.3f) :
                        state == CellVisibility.Explored ? new Color(0.25f, 0.3f, 0.65f) : new Color(0.06f, 0.06f, 0.08f);
                    if (state == CellVisibility.Visible && danger.Contains(coordinate.Value)) tile.color = new Color(1f, 0.22f, 0.12f);
                    return;
                }
                if (state == CellVisibility.Unknown)
                {
                    tile.sprite = tileSprite;
                    tile.color = new Color(0.06f, 0.06f, 0.08f);
                    return;
                }
                if (state == CellVisibility.Explored)
                {
                    Visibility.TryGetRememberedTerrain(coordinate.Value, out var terrain);
                    tile.sprite = terrainSprites[terrain];
                    var remembered = TerrainColor(terrain, Biome);
                    tile.color = new Color(remembered.r * 0.45f, remembered.g * 0.45f, remembered.b * 0.45f);
                    return;
                }
            }
            tile.color = coordinate == selected
                ? (coordinate == hovered ? SelectedHoverColor : SelectedColor)
                : (coordinate == hovered ? HoverColor :
                    (targets.Contains(coordinate.Value) ? TargetColor :
                    (traps.Contains(coordinate.Value) ? TrapColor :
                    (reachable.Contains(coordinate.Value) ? ReachableColor : TerrainColor(grid.GetCell(coordinate.Value).Terrain, Biome)))));
            if (danger.Contains(coordinate.Value)) tile.color = new Color(1f, 0.22f, 0.12f);
        }

        private void OnDestroy()
        {
            foreach (var art in terrainSprites.Values)
                if (Application.isPlaying) Destroy(art); else DestroyImmediate(art);
            foreach (var art in terrainTextures)
                if (Application.isPlaying) Destroy(art); else DestroyImmediate(art);
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
