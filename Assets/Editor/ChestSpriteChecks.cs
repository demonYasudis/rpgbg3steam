using System;
using System.IO;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class ChestSpriteChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Chest and Goblin Art")]
        public static void Run()
        {
            UnitSpriteChecks.Run();
            ExpeditionChecks.Run();
            var root = new GameObject("Chest art checks");
            var cameraObject = new GameObject("Sprite gallery camera");
            var target = new RenderTexture(1440, 960, 24);
            var pixels = new Texture2D(1440, 960, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            using (var goblin = UnitSpriteSheet.Load("veil-stalker"))
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var map = DungeonGenerator.Generate(13);
                var layout = new HexLayout();
                var gridView = root.AddComponent<HexGridView>(); gridView.Initialize(map.Grid, layout);
                var input = root.AddComponent<HexGridInteraction>(); input.Initialize(map.Grid, layout, gridView, camera);
                var units = root.AddComponent<PlayerUnitController>();
                units.Initialize(map.Grid, layout, gridView, input, 0.02f, enableFog: true,
                    playerSpawns: map.PlayerSpawns, encounter: EncounterGenerator.Generate(map), expeditionMap: map);
                var chestObject = new GameObject("Chest"); chestObject.transform.SetParent(root.transform);
                chestObject.AddComponent<ChestView>().Initialize(units, layout);
                var chest = chestObject.GetComponent<SpriteRenderer>();
                Require(chest != null && chest.sprite != null, "Chest loads generated artwork");
                Require(chest.enabled == units.Visibility.IsVisible(units.Expedition.Chest), "Chest respects fog at initialization");
                Require(chest.sprite.texture.width == 2172 && chest.sprite.rect.width == 543, "Full source resolution and four cells");
                Require(chest.sprite.texture.filterMode == FilterMode.Point && chest.sprite.texture.mipmapCount == 1, "Crisp chest import");
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 9; col++)
                    {
                        var obj = new GameObject("Goblin frame"); obj.transform.SetParent(root.transform);
                        obj.layer = 30; obj.transform.position = new Vector3(col * 1.4f, (5 - row) * 1.45f, 0);
                        obj.AddComponent<SpriteRenderer>().sprite = goblin.Frame((UnitSpriteSheet.Pose)row,
                            col / (row == 0 ? 5f : 10f) + 0.001f, false);
                    }
                var chestFrames = new Sprite[4];
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var obj = new GameObject("Chest frame"); obj.transform.SetParent(root.transform);
                        obj.layer = 30; obj.transform.position = new Vector3(2 + i * 2.3f, 0, 0);
                        chestFrames[i] = Sprite.Create(chest.sprite.texture, new Rect(i * 543, 0, 543, 724), new Vector2(0.5f, 0.43f), 400);
                        obj.AddComponent<SpriteRenderer>().sprite = chestFrames[i];
                    }
                    camera.cullingMask = 1 << 30; camera.orthographic = true; camera.orthographicSize = 4.8f;
                    camera.transform.position = new Vector3(5.6f, 3.5f, -10);
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.13f, 0.15f, 0.19f);
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1440, 960), 0, 0); pixels.Apply();
                    Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/goblin-chest-gallery.png", pixels.EncodeToPNG());
                }
                finally { foreach (var frame in chestFrames) if (frame != null) UnityEngine.Object.DestroyImmediate(frame); }
                Debug.Log("Chest and goblin art checks passed: slicing, import, visibility, expedition regression and rendered gallery.");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Chest sprites: " + message); }
    }
}
