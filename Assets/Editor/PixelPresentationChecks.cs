using System;
using System.IO;
using System.Linq;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Generation;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class PixelPresentationChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Crypt Art")]
        public static void Run()
        {
            var definitions = HeroDefinitions.Defaults.Concat(EnemyDefinitions.Regular.Select(a => a.Unit))
                .Concat(new[] { EnemyDefinitions.MiniBoss.Unit }).ToArray();
            var silhouettes = definitions.Select(d => string.Join(",", CryptPixelArt.Draw(d.Id, Color.cyan)
                .Select(c => c.r + ":" + c.g + ":" + c.b + ":" + c.a))).ToArray();
            Require(silhouettes.Distinct().Count() == definitions.Length, "Every archetype has distinct art");
            var root = new GameObject("Crypt art validation");
            var cameraObject = new GameObject("Art camera");
            var target = new RenderTexture(960, 480, 24) { filterMode = FilterMode.Point };
            var pixels = new Texture2D(960, 480, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var grid = new HexGrid.HexGrid(5, 3);
                var layout = new HexLayout();
                grid.GetCell(new HexCoordinates(0, 0)).Terrain = TerrainType.Blocked;
                grid.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.Pit;
                grid.GetCell(new HexCoordinates(2, 0)).Terrain = TerrainType.HighGround;
                var view = root.AddComponent<HexGridView>(); view.Initialize(grid, layout);
                for (int i = 0; i < definitions.Length; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(grid, "art-" + i, definitions[i], new HexCoordinates(i % 5, 1 + i / 5), out var state), "Art fixture spawn");
                    var obj = new GameObject(definitions[i].Id); obj.transform.SetParent(root.transform);
                    obj.AddComponent<UnitView>().Initialize(state, layout,
                        i < 4 ? new[] { new Color(0.4f, 0.6f, 0.85f), new Color(0.7f, 0.45f, 0.75f), new Color(0.4f, 0.7f, 0.45f), new Color(0.8f, 0.65f, 0.3f) }[i] : new Color(0.85f, 0.4f, 0.3f));
                }
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>())
                {
                    renderer.gameObject.layer = 30;
                    Require(renderer.sprite.texture.filterMode == FilterMode.Point && renderer.sprite.texture.mipmapCount == 1,
                        "Nearest pixels without mip blur");
                }
                var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 30;
                camera.orthographic = true; camera.orthographicSize = 3.75f; camera.allowMSAA = false;
                camera.backgroundColor = new Color(0.035f, 0.04f, 0.065f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.transform.position = layout.GetBounds(grid).center + new Vector3(0, 0, -10);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 480), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp18-art.png", pixels.EncodeToPNG());
                Debug.Log("WP-18 passed: nine distinct sprites, point filtering, no mipmaps; rendered crypt art gallery.");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-18: " + message); }
    }
}
