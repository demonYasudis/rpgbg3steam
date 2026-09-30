using System;
using System.IO;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class CharacterCameraChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Character Camera")]
        public static void Run()
        {
            var root = new GameObject("Character camera fixture");
            var cameraObject = new GameObject("Character camera");
            var previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.cullingMask = 1 << 30;
                var grid = new HexGrid.HexGrid();
                var layout = new HexLayout();
                var view = root.AddComponent<HexGridView>();
                view.Initialize(grid, layout);
                var interaction = root.AddComponent<HexGridInteraction>();
                var center = new HexCoordinates(4, 5);
                string[] ids = { "warrior", "rogue", "ranger", "mage", "ash-crawler", "veil-stalker", "hollow-brute" };
                for (int i = 0; i < ids.Length; i++)
                {
                    var position = new HexCoordinates(2 + i % 4, 5 + i / 4);
                    Require(UnitRuntimeState.TrySpawn(grid, "camera-" + i,
                        new UnitDefinition(ids[i], ids[i], 3), position, out var state), "Spawn fixture");
                    var obj = new GameObject(ids[i]); obj.transform.SetParent(root.transform);
                    obj.AddComponent<UnitView>().Initialize(state, layout, Color.white);
                }
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>()) renderer.gameObject.layer = 30;
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(640, 480) })
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    Texture2D capture = null;
                    try
                    {
                        camera.targetTexture = target; camera.ResetAspect();
                        interaction.Initialize(grid, layout, view, camera);
                        interaction.SetSelected(center); interaction.ShowCharacterDetail();
                        Vector3 screen = camera.WorldToScreenPoint(layout.ToWorld(center));
                        Require(Mathf.Abs(camera.pixelHeight / (2f * camera.orthographicSize) - 80f) < 0.01f,
                            "Detail scale survives window size changes");
                        Require(screen.y > 32 && screen.y < size.y - HexGridInteraction.HudHeight, "Focus below HUD");
                        interaction.ProcessPointer(screen, false);
                        Require(interaction.Hovered == center, "Detailed cell picking");
                        if (size.x == 1280)
                        {
                            camera.Render(); RenderTexture.active = target;
                            capture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                            capture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); capture.Apply();
                            Directory.CreateDirectory("Logs");
                            File.WriteAllBytes("Logs/characters-64-screen.png", capture.EncodeToPNG());
                        }
                        var beforePan = camera.transform.position;
                        interaction.PanCamera(new Vector2(80, 0));
                        Require(Mathf.Abs(camera.transform.position.x - beforePan.x + 1f) < 0.01f, "Drag pans one world unit");
                        var beforeZoom = camera.orthographicSize;
                        interaction.ZoomCamera(1);
                        Require(camera.orthographicSize < beforeZoom, "Wheel zooms in");
                        interaction.ZoomCamera(-1);
                        Require(Mathf.Abs(camera.orthographicSize - beforeZoom) < 0.01f, "Wheel zoom reversible");
                        interaction.FrameCamera();
                        foreach (var cell in grid.Cells)
                        {
                            screen = camera.WorldToScreenPoint(layout.ToWorld(cell.Coordinates));
                            Require(screen.x > 0 && screen.x < size.x && screen.y > 32 &&
                                screen.y < size.y - HexGridInteraction.HudHeight, "Overview fits the complete map");
                        }
                        interaction.ShowCharacterDetail();
                        Require(interaction.DetailView, "Return from overview");
                    }
                    finally
                    {
                        RenderTexture.active = previous; camera.targetTexture = null;
                        if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
                Debug.Log("Character camera checks passed: ~64px silhouettes, three resolutions, picking, pan, zoom and map overview.");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Character camera: " + message); }
    }
}
