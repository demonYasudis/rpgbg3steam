using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class UnitMovementChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Unit Movement")]
        public static void Run()
        {
            CheckDefinitionsAndRuntimeMovement();
            Debug.Log("WP-04 model checks passed: definitions/runtime separation, four spawns, legal routes, atomic occupancy and rejected illegal movement.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static void CheckDefinitionsAndRuntimeMovement()
        {
            Require(HeroDefinitions.Defaults.Count == 4, "Exactly four definitions");
            Require(HeroDefinitions.Defaults.Select(item => item.Id).Distinct().Count() == 4, "Unique definition IDs");
            Require(HeroDefinitions.Defaults.All(item => item.Movement >= 0), "Valid movement stats");
            Throws<ArgumentException>(() => new UnitDefinition(" ", "Hero", 1));
            Throws<ArgumentException>(() => new UnitDefinition("hero", null, 1));
            Throws<ArgumentOutOfRangeException>(() => new UnitDefinition("hero", "Hero", -1));

            var grid = new GridModel(6, 6);
            var positions = new[]
            {
                new HexCoordinates(0, 0), new HexCoordinates(0, 1),
                new HexCoordinates(1, 1), new HexCoordinates(0, 2)
            };
            var units = new List<UnitRuntimeState>();
            for (int index = 0; index < HeroDefinitions.Defaults.Count; index++)
            {
                Require(UnitRuntimeState.TrySpawn(grid, "test-" + index, HeroDefinitions.Defaults[index],
                    positions[index], out var unit), "Hero spawn " + index);
                units.Add(unit);
            }
            Require(grid.Cells.Count(cell => cell.IsOccupied) == 4, "Four occupied spawn cells");
            Require(!UnitRuntimeState.TrySpawn(grid, "duplicate-cell", HeroDefinitions.Defaults[0], positions[0], out _),
                "Occupied spawn rejected");
            Require(!UnitRuntimeState.TrySpawn(grid, "test-0", HeroDefinitions.Defaults[0], new HexCoordinates(3, 3), out _),
                "Duplicate instance ID rejected");

            var mover = units[0];
            var origin = mover.Position;
            var destination = new HexCoordinates(3, 0);
            var route = new[] { origin, new HexCoordinates(1, -1) };
            Require(!mover.TryMoveAlong(grid, route), "Out-of-grid step rejected");
            route = new[] { origin, new HexCoordinates(0, 1) };
            Require(!mover.TryMoveAlong(grid, route), "Occupied destination rejected");
            route = new[] { origin, new HexCoordinates(2, 0) };
            Require(!mover.TryMoveAlong(grid, route), "Non-adjacent route rejected");
            route = new[] { origin, new HexCoordinates(0, 1), new HexCoordinates(1, 1),
                new HexCoordinates(2, 1), destination };
            Require(!mover.TryMoveAlong(grid, route), "Route through occupied cells rejected");
            var openRoute = new[] { origin, new HexCoordinates(1, 0), new HexCoordinates(2, 0), destination };
            Require(mover.TryMoveAlong(grid, openRoute), "Legal route committed");
            Require(mover.Position == destination && !grid.GetCell(origin).IsOccupied &&
                grid.GetCell(destination).OccupantId == mover.InstanceId, "Position and occupancy update together");
            Require(!mover.TryMoveAlong(grid, openRoute), "Old route cannot commit twice");

            var limitedDefinition = new UnitDefinition("limited", "Limited", 1);
            Require(UnitRuntimeState.TrySpawn(grid, "limited", limitedDefinition, new HexCoordinates(4, 4), out var limited),
                "Limited unit spawned");
            var tooFar = new[] { new HexCoordinates(4, 4), new HexCoordinates(5, 4), new HexCoordinates(5, 5) };
            Require(!limited.TryMoveAlong(grid, tooFar) && limited.Position == new HexCoordinates(4, 4),
                "Movement budget enforced without mutation");
            grid.GetCell(new HexCoordinates(5, 4)).Terrain = TerrainType.Blocked;
            Require(!limited.TryMoveAlong(grid, new[] { limited.Position, new HexCoordinates(5, 4) }),
                "Blocked terrain rejected");
            Throws<ArgumentNullException>(() => limited.TryMoveAlong(null, tooFar));
            Throws<ArgumentNullException>(() => limited.TryMoveAlong(grid, null));
        }

        internal static void ValidatePresentation(GridModel grid, HexGridView gridView,
            HexGridInteraction interaction, PlayerUnitController controller, Camera camera)
        {
            Require(controller != null && controller.Units.Count == 4, "Four runtime heroes");
            Require(grid.Cells.Count(cell => cell.IsOccupied) == 4, "Four occupied runtime cells");
            Require(controller.GetComponentsInChildren<UnitView>().Length == 4, "Four unit views");
            var layout = new HexLayout();
            foreach (var unit in controller.Units)
            {
                var unitView = controller.GetComponentsInChildren<UnitView>().Single(item => item.State == unit);
                Require(Vector3.Distance(unitView.transform.position,
                    layout.ToWorld(unit.Position) + new Vector3(0, 0, -0.2f)) < 0.001f, "Spawn view alignment");
            }

            controller.SetAnimationSecondsPerStep(0);
            var selected = controller.Units[0];
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(selected.Position)), true);
            Require(controller.SelectedUnit == selected && controller.CurrentRange != null &&
                controller.CurrentRange.Origin == selected.Position, "Click selects hero and computes range");
            foreach (var other in controller.Units.Skip(1))
                Require(!controller.CurrentRange.Costs.ContainsKey(other.Position), "Occupied hero cell unavailable");

            var origin = selected.Position;
            var destination = new HexCoordinates(3, 0);
            Require(controller.CurrentRange.Costs.ContainsKey(destination), "Chosen destination is reachable");
            var tileRenderers = Array.FindAll(gridView.GetComponentsInChildren<SpriteRenderer>(), item => item.sortingOrder == 0);
            Require(tileRenderers[destination.R * grid.Width + destination.Q].color !=
                tileRenderers[11 * grid.Width + 11].color, "Reachable destination highlighted");
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(destination)), true);
            Require(selected.Position == destination && !grid.GetCell(origin).IsOccupied &&
                grid.GetCell(destination).OccupantId == selected.InstanceId, "Click commits occupancy once");
            Require(!controller.IsMoving && controller.CurrentRange.Origin == destination,
                "Immediate test animation completes and refreshes range");
            var movedView = controller.GetComponentsInChildren<UnitView>().Single(item => item.State == selected);
            Require(Vector3.Distance(movedView.transform.position,
                layout.ToWorld(destination) + new Vector3(0, 0, -0.2f)) < 0.001f, "Final view alignment");
            Require(!controller.TryMoveSelected(controller.Units[1].Position), "Occupied destination remains illegal");
            Require(selected.Position == destination && grid.GetCell(destination).OccupantId == selected.InstanceId,
                "Rejected move preserves logical state");
            Throws<ArgumentOutOfRangeException>(() => controller.SetAnimationSecondsPerStep(-1));
            Debug.Log("WP-04 Play Mode checks passed: four heroes, selection, occupied-cell exclusion, click movement, single occupancy commit and final alignment.");
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("WP-04 failed: " + description);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
