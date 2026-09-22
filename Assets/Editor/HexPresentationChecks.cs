using System;
using System.IO;
using GuildTactics.Core;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    /// <summary>Dependency-free checks, including the saved scene in real Play Mode.</summary>
    [InitializeOnLoad]
    public static class HexPresentationChecks
    {
        private const string PendingKey = "GuildTactics.WP02.Pending";
        private static int playFrames;
        private static double deadline;
        private static bool turnChecksStarted;
        private static bool combatChecksStarted;
        private static bool enemyChecksStarted;
        private static bool abilityChecksStarted;
        private static PlayerUnitController sceneUnits;

        static HexPresentationChecks()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += WaitForPlayMode;
        }

        [MenuItem("Tools/Guild Tactics/Validate Hex Layout")]
        public static void ValidateLayout()
        {
            var grid = new GridModel();
            foreach (float radius in new[] { 0.25f, 1f, 2.5f })
            {
                var layout = new HexLayout(radius, new Vector2(-4.5f, 3.25f));
                for (int r = -12; r <= 12; r++)
                    for (int q = -12; q <= 12; q++)
                    {
                        var coordinate = new HexCoordinates(q, r);
                        var center = layout.ToWorld(coordinate);
                        Require(layout.ToCoordinates(center) == coordinate, "Center round trip");
                        for (int d = 0; d < 6; d++)
                        {
                            Vector3 towardNeighbor = layout.ToWorld(coordinate.GetNeighbor(d)) - center;
                            Require(layout.ToCoordinates(center + towardNeighbor * 0.49f) == coordinate,
                                "Inside shared edge");
                            Require(layout.ToCoordinates(center + towardNeighbor * 0.51f) == coordinate.GetNeighbor(d),
                                "Across shared edge");
                            Require(layout.ToCoordinates(center + (Vector3)layout.Corner(d) * 0.98f) == coordinate,
                                "Inside corner");
                        }
                    }
                var bounds = layout.GetBounds(grid);
                foreach (var cell in grid.Cells)
                    for (int d = 0; d < 6; d++)
                    {
                        Vector3 corner = layout.ToWorld(cell.Coordinates) + (Vector3)layout.Corner(d);
                        Require(corner.x >= bounds.min.x - 0.001f && corner.x <= bounds.max.x + 0.001f &&
                            corner.y >= bounds.min.y - 0.001f && corner.y <= bounds.max.y + 0.001f, "Bounds contain tile");
                    }
            }
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                bool rejected = false;
                try { new HexLayout(invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "Invalid radius rejected");
            }
            Debug.Log("WP-02 layout checks passed: centers, edges, corners, negative coordinates, scales, bounds.");
        }

        // Batch entry point: deliberately omit -quit so the editor can enter Play Mode.
        public static void RunBatch()
        {
            try
            {
                HexGridChecks.Run();
                ValidateLayout();
                HexPathfindingChecks.Run();
                UnitMovementChecks.Run();
                TurnChecks.Run();
                CombatChecks.Run();
                EnemyChecks.Run();
                AbilityChecks.Run();
                SessionState.SetBool(PendingKey, true);
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update -= WaitForPlayMode;
                EditorApplication.update += WaitForPlayMode;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void WaitForPlayMode()
        {
            if (EditorApplication.timeSinceStartup > deadline)
            {
                Finish(new TimeoutException("WP-02 Play Mode startup timed out."));
                return;
            }
            if (!EditorApplication.isPlaying || ++playFrames < 10) return;
            try
            {
                if (!turnChecksStarted)
                {
                    ValidatePlayingScene();
                    turnChecksStarted = true;
                }
                else if (!combatChecksStarted)
                {
                    if (TurnChecks.PollPresentation())
                    {
                        CombatChecks.BeginPresentation(sceneUnits);
                        combatChecksStarted = true;
                    }
                }
                else if (!enemyChecksStarted)
                {
                    if (CombatChecks.PollPresentation())
                    {
                        EnemyChecks.BeginPresentation(sceneUnits);
                        enemyChecksStarted = true;
                    }
                }
                else if (!abilityChecksStarted)
                {
                    if (EnemyChecks.PollPresentation())
                    {
                        AbilityChecks.BeginPresentation(sceneUnits);
                        abilityChecksStarted = true;
                    }
                }
                else if (AbilityChecks.PollPresentation()) Finish(null);
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void ValidatePlayingScene()
        {
            GameBootstrap bootstrap = null;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.TryGetComponent<GameBootstrap>(out var candidate)) bootstrap = candidate;
            Require(bootstrap != null && bootstrap.Grid != null, "Saved scene bootstrapped");
            var view = bootstrap.GetComponentInChildren<HexGridView>();
            var interaction = bootstrap.GetComponentInChildren<HexGridInteraction>();
            var units = bootstrap.GetComponentInChildren<PlayerUnitController>();
            sceneUnits = units;
            var camera = new SerializedObject(bootstrap).FindProperty("gridCamera").objectReferenceValue as Camera;
            Require(view != null && view.TileCount == 144 && interaction != null && units != null && camera != null,
                "144 runtime tiles and gameplay wiring");
            var renderers = Array.FindAll(view.GetComponentsInChildren<SpriteRenderer>(), item => item.sortingOrder == 0);
            Require(renderers.Length == 144, "Exactly one renderer per cell");
            var layout = new HexLayout();
            foreach (var cell in bootstrap.Grid.Cells)
            {
                var point = camera.WorldToScreenPoint(layout.ToWorld(cell.Coordinates));
                interaction.ProcessPointer(point, false);
                Require(interaction.Hovered == cell.Coordinates, "Mouse screen projection hovers exact cell " + cell.Coordinates);
                Require(cell.Terrain == TerrainType.Ground, "Presentation preserves terrain model");
            }
            int occupiedCells = 0;
            foreach (var cell in bootstrap.Grid.Cells) if (cell.IsOccupied) occupiedCells++;
            Require(occupiedCells == 7, "Four heroes and three enemies occupy cells");
            // Temporarily detach gameplay input to test the isolated cell-selection layer.
            units.enabled = false;
            interaction.SetSelected(new HexCoordinates(5, 5));
            var selected = interaction.Selected;
            interaction.ProcessPointer(new Vector2(-100, -100), true);
            Require(interaction.Hovered == null && interaction.Selected == selected, "Outside viewport preserves selection");
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(new HexCoordinates(-2, 0))), true);
            Require(interaction.Hovered == null && interaction.Selected == selected, "Outside grid preserves selection");
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(new HexCoordinates(0, 0))), false, false);
            Require(interaction.Hovered == null && interaction.Selected == selected, "Lost focus clears hover");

            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(new HexCoordinates(5, 5))), true);
            Color selectedHoverColor = renderers[5 * 12 + 5].color;
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(new HexCoordinates(6, 5))), false);
            Color selectedColor = renderers[5 * 12 + 5].color;
            Color hoverColor = renderers[5 * 12 + 6].color;
            // Isolate the WP-02 selection layer; range coverage is checked separately below.
            view.SetReachableCells(null);
            Require(selectedColor != hoverColor && selectedColor != selectedHoverColor,
                "Selected, hovered and selected-hovered colors are distinct");
            int highlighted = 0;
            foreach (var renderer in renderers) if (renderer.color != renderers[0].color) highlighted++;
            Require(highlighted == 2 && interaction.Selected == new HexCoordinates(5, 5), "One hover and one persistent selection");
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 1280, 720);
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 640, 960);
            Debug.Log("WP-02 Play Mode checks passed: saved scene, 144 renderers, all 144 mouse projections, highlights, outside/focus, landscape/portrait framing and rendered pixels.");
            units.enabled = true;
            UnitMovementChecks.ValidatePresentation(bootstrap.Grid, view, interaction, units, camera);
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 1280, 720, "wp03");
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 640, 960, "wp03");
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 1280, 720, "wp04");
            CaptureAndCheckFraming(camera, interaction, layout, bootstrap.Grid, 640, 960, "wp04");
            TurnChecks.BeginPresentation(units);
        }

        private static void CaptureAndCheckFraming(Camera camera, HexGridInteraction interaction,
            HexLayout layout, GridModel grid, int width, int height, string prefix = "wp02")
        {
            var target = new RenderTexture(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.ResetAspect();
                interaction.FrameCamera();
                foreach (var cell in grid.Cells)
                    for (int d = 0; d < 6; d++)
                    {
                        var point = camera.WorldToViewportPoint(layout.ToWorld(cell.Coordinates) + (Vector3)layout.Corner(d));
                        Require(point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1f - HexGridInteraction.HudHeight / height,
                            "Every tile inside camera below HUD");
                    }
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                Color background = pixels.GetPixel(0, 0);
                foreach (var cell in grid.Cells)
                {
                    Vector3 point = camera.WorldToScreenPoint(layout.ToWorld(cell.Coordinates));
                    Color color = pixels.GetPixel((int)point.x, (int)point.y);
                    Require(Mathf.Abs(color.r - background.r) + Mathf.Abs(color.g - background.g) +
                        Mathf.Abs(color.b - background.b) > 0.15f, "Tile center renders visibly");
                }
                var center = layout.ToWorld(new HexCoordinates(5, 5));
                for (int d = 0; d < 6; d++)
                {
                    var neighbor = layout.ToWorld(new HexCoordinates(5, 5).GetNeighbor(d));
                    var edgePoint = camera.WorldToScreenPoint((center + neighbor) * 0.5f);
                    var edgeColor = pixels.GetPixel((int)edgePoint.x, (int)edgePoint.y);
                    Require(Mathf.Abs(edgeColor.r - background.r) + Mathf.Abs(edgeColor.g - background.g) +
                        Mathf.Abs(edgeColor.b - background.b) < 0.05f, "Visible gutter on all six hex edges");
                }
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes($"Logs/{prefix}-{width}x{height}.png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.ResetAspect();
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
                interaction.FrameCamera();
            }
        }

        private static void Finish(Exception exception)
        {
            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= WaitForPlayMode;
            if (exception != null) Debug.LogException(exception);
            EditorApplication.Exit(exception == null ? 0 : 1);
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("WP-02 failed: " + description);
        }
    }

}
