using System;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class TurnChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Turns")]
        public static void Run()
        {
            var grid = new GridModel(8, 8);
            UnitRuntimeState Spawn(string id, int q, int initiative, int movement = 3)
            {
                Require(UnitRuntimeState.TrySpawn(grid, id, new UnitDefinition(id, id, movement, initiative),
                    new HexCoordinates(q, 0), out var unit), "Spawn");
                return unit;
            }
            var slow = Spawn("slow", 0, int.MinValue);
            var first = Spawn("first", 2, int.MaxValue);
            var tied = Spawn("tied", 4, int.MaxValue);
            var still = Spawn("still", 6, 0, 0);
            var turns = new TurnManager(grid, new[] { slow, first, tied, still });
            Require(turns.Order.SequenceEqual(new[] { first, tied, still, slow }), "Stable initiative including extremes");
            Require(turns.ActiveUnit == null && !turns.TryEndTurn(first) && !turns.TryBeginAction(first), "Await turn blocks commands");
            Require(turns.TryStartNextTurn() && !turns.TryStartNextTurn(), "Start once");
            Require(turns.Round == 1 && turns.ActiveUnit == first && turns.RemainingMovement == 3 && turns.ActionAvailable,
                "First turn resources");
            Require(!turns.TryBeginMovement(tied, new HexCoordinates(4, 1), out _) &&
                !turns.TryBeginAction(tied) && !turns.TryEndTurn(tied), "Inactive unit cannot act");
            Require(!turns.TryBeginMovement(first, tied.Position, out _) && turns.RemainingMovement == 3,
                "Invalid move costs nothing");
            var destination = new HexCoordinates(2, 1);
            grid.GetCell(destination).MovementCost = 2;
            Require(turns.TryBeginMovement(first, destination, out var path) && path.Count == 2 &&
                turns.RemainingMovement == 1 && first.Position == destination, "Movement commits and spends weighted cost");
            Require(!turns.TryBeginMovement(first, new HexCoordinates(2, 2), out _) &&
                !turns.TryBeginAction(first) && !turns.TryEndTurn(first) && !turns.TryStartNextTurn(), "Moving locks all commands");
            Require(!turns.TryCompleteMovement(tied) && turns.TryCompleteMovement(first) &&
                !turns.TryCompleteMovement(first), "Movement completes once for actor");
            Require(!turns.TryBeginMovement(first, new HexCoordinates(2, 3), out _) && turns.RemainingMovement == 1,
                "Remaining budget limits subsequent routes");
            Require(turns.TryBeginMovement(first, new HexCoordinates(2, 2), out _) && turns.RemainingMovement == 0,
                "Split movement consumes final point");
            turns.TryCompleteMovement(first);
            Require(!turns.TryBeginMovement(first, new HexCoordinates(2, 3), out _), "Exhausted movement rejected");
            Require(turns.TryBeginAction(first) && !turns.ActionAvailable && !turns.TryBeginAction(first) &&
                !turns.TryEndTurn(first) && !turns.TryBeginMovement(first, destination, out _), "Resolving action locks input");
            Require(!turns.TryCompleteAction(tied) && turns.TryCompleteAction(first) &&
                !turns.TryCompleteAction(first) && !turns.TryBeginAction(first), "One action per turn");
            Require(turns.TryEndTurn(first) && !turns.TryEndTurn(first), "Exhausted turn can end exactly once");
            for (int i = 0; i < 3; i++)
            {
                Require(turns.TryStartNextTurn() && turns.TryEndTurn(turns.ActiveUnit), "Skip unused turn including zero movement");
            }
            Require(turns.TryStartNextTurn() && turns.Round == 2 && turns.ActiveUnit == first &&
                turns.RemainingMovement == 3 && turns.ActionAvailable, "Next round resets resources");
            for (int i = 0; i < 100; i++)
            {
                var expected = turns.Order[(i + 1) % 4];
                Require(turns.TryEndTurn(turns.ActiveUnit) && turns.TryStartNextTurn() && turns.ActiveUnit == expected,
                    "Stable repeated rounds");
            }
            var single = new TurnManager(grid, new[] { still });
            Require(single.TryStartNextTurn() && single.TryEndTurn(still) && single.TryStartNextTurn() && single.Round == 2,
                "Single immobile participant can cycle");
            Throws<ArgumentException>(() => new TurnManager(grid, Array.Empty<UnitRuntimeState>()));
            Throws<ArgumentException>(() => new TurnManager(grid, new[] { first, first }));
            Throws<ArgumentException>(() => new TurnManager(grid, new UnitRuntimeState[] { null }));
            Throws<ArgumentNullException>(() => new TurnManager(null, new[] { first }));
            Throws<ArgumentNullException>(() => new TurnManager(grid, null));
            Debug.Log("WP-05 model checks passed: stable order, rounds, resource costs/reset, turn ownership, resolution locks and invalid input.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static PlayerUnitController controller;
        private static UnitRuntimeState actor;
        private static int stage;

        internal static void BeginPresentation(PlayerUnitController units)
        {
            controller = units;
            actor = units.SelectedUnit;
            Require(units.GetComponent<TurnOrderUI>() != null, "Turn UI wired in saved scene");
            foreach (var other in units.Units.Where(unit => unit != actor))
                Require(!units.TrySelectUnit(other.Position), "Cannot select inactive hero");
            units.SetAnimationSecondsPerStep(0.02f);
            var destination = units.CurrentRange.Costs.Keys.First(cell => cell != actor.Position);
            Require(units.TryMoveSelected(destination) && units.IsMoving, "Animated movement begins");
            Require(!units.TryMoveSelected(destination) && !units.TryEndTurn() && !units.TryWaitAction(), "Animated input lock");
            stage = 0;
        }

        internal static bool PollPresentation()
        {
            if (stage == 0)
            {
                if (controller.IsMoving) return false;
                var view = controller.GetComponentsInChildren<UnitView>().Single(item => item.State == actor);
                Require(Vector3.Distance(view.transform.position,
                    new HexLayout().ToWorld(actor.Position) + new Vector3(0, 0, -0.2f)) < 0.001f, "Animated endpoint aligned");
                Require(controller.TryWaitAction() && !controller.TryWaitAction() && !controller.TryEndTurn() &&
                    !controller.TryMoveSelected(actor.Position.GetNeighbor(0)), "Action animation locks commands");
                stage = 1;
                return false;
            }
            if (stage == 1)
            {
                if (controller.Turns.State == TurnState.ResolvingAction) return false;
                Require(!controller.TryWaitAction() && controller.TryEndTurn() && !controller.TryEndTurn(), "Action spent, end once");
                stage = 2;
                return false;
            }
            if (controller.Turns.State == TurnState.TurnComplete) return false;
            Require(controller.SelectedUnit != actor && controller.Turns.CanSelectAction(controller.SelectedUnit), "Next frame advances selection");
            controller.SetAnimationSecondsPerStep(1);
            var next = controller.CurrentRange.Costs.Keys.First(cell => cell != controller.SelectedUnit.Position);
            Require(controller.TryMoveSelected(next), "Movement before disabling");
            controller.enabled = false;
            Require(!controller.IsMoving && !controller.TryEndTurn(), "Disable stops animation and blocks commands");
            controller.enabled = true;
            Require(controller.Turns.State == TurnState.SelectingAction && controller.CurrentRange != null, "Re-enable restores input and range");
            Require(controller.TryWaitAction(), "Action before disabling");
            controller.enabled = false;
            controller.enabled = true;
            Require(!controller.TryWaitAction() && controller.TryEndTurn(), "Interrupted action stays spent without softlock");
            Debug.Log("WP-05 Play Mode checks passed: animation completion, locks, UI wiring, turn advance and disable recovery.");
            return true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("WP-05 failed: " + message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
