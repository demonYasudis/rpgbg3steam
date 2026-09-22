using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Abilities;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Combat
{
    public enum TurnState { AwaitTurn, SelectingAction, Moving, ResolvingAction, TurnComplete }

    /// <summary>Authoritative turn permissions and resources, independent of presentation.</summary>
    public sealed class TurnManager
    {
        private readonly GridModel grid;
        internal GridModel Grid => grid;
        private int activeIndex = -1;
        public IReadOnlyList<UnitRuntimeState> Order { get; }
        public UnitRuntimeState ActiveUnit => activeIndex < 0 ? null : Order[activeIndex];
        public TurnState State { get; private set; } = TurnState.AwaitTurn;
        public int Round { get; private set; }
        public int RemainingMovement { get; private set; }
        public bool ActionAvailable { get; private set; }
        public TrapField Traps { get; } = new TrapField();
        public IReadOnlyList<TrapHit> LastTrapHits { get; private set; } = Array.Empty<TrapHit>();

        public TurnManager(GridModel grid, IEnumerable<UnitRuntimeState> units)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (units == null) throw new ArgumentNullException(nameof(units));
            var ordered = new List<UnitRuntimeState>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                if (unit == null || !ids.Add(unit.InstanceId) ||
                    !unit.IsPlacedOn(grid))
                    throw new ArgumentException("Turn participants must be unique units placed on this grid.", nameof(units));
                // Stable insertion: tied initiative retains the supplied spawn order.
                int index = ordered.FindIndex(other => other.Definition.Initiative < unit.Definition.Initiative);
                if (index < 0) ordered.Add(unit);
                else ordered.Insert(index, unit);
            }
            if (ordered.Count == 0) throw new ArgumentException("At least one participant is required.", nameof(units));
            Order = ordered.AsReadOnly();
        }

        public bool TryStartNextTurn()
        {
            if (State != TurnState.AwaitTurn && State != TurnState.TurnComplete) return false;
            int nextIndex = activeIndex;
            int nextRound = Round;
            bool found = false;
            for (int checkedUnits = 0; checkedUnits < Order.Count; checkedUnits++)
            {
                nextIndex = (nextIndex + 1) % Order.Count;
                if (nextIndex == 0) nextRound++;
                if (Order[nextIndex].IsAlive)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                activeIndex = -1;
                RemainingMovement = 0;
                ActionAvailable = false;
                State = TurnState.AwaitTurn;
                return false;
            }
            activeIndex = nextIndex;
            Round = nextRound;
            ActiveUnit.BeginTurn();
            RemainingMovement = ActiveUnit.Definition.Movement;
            ActionAvailable = true;
            State = TurnState.SelectingAction;
            return true;
        }

        public bool CanSelectAction(UnitRuntimeState unit) =>
            unit != null && unit.IsAlive && ReferenceEquals(unit, ActiveUnit) && State == TurnState.SelectingAction;

        public bool TryBeginMovement(UnitRuntimeState unit, HexCoordinates destination,
            out IReadOnlyList<HexCoordinates> path)
        {
            path = Array.Empty<HexCoordinates>();
            if (!CanSelectAction(unit) || destination == unit.Position) return false;
            var range = HexPathfinder.FindReachable(grid, unit.Position, RemainingMovement);
            var candidate = Traps.LimitPath(unit, range.GetPathTo(destination));
            if (candidate.Count < 2 || !unit.TryMoveAlong(grid, candidate)) return false;
            RemainingMovement -= range.Costs[unit.Position];
            LastTrapHits = Traps.TriggerPath(unit, candidate);
            State = TurnState.Moving;
            path = candidate;
            return true;
        }

        public bool TryCompleteMovement(UnitRuntimeState unit)
        {
            if (!ReferenceEquals(unit, ActiveUnit) || State != TurnState.Moving) return false;
            State = TurnState.SelectingAction;
            return true;
        }

        public bool TryBeginAction(UnitRuntimeState unit)
        {
            if (!CanSelectAction(unit) || !ActionAvailable) return false;
            ActionAvailable = false;
            State = TurnState.ResolvingAction;
            return true;
        }

        public bool TryCompleteAction(UnitRuntimeState unit)
        {
            if (!ReferenceEquals(unit, ActiveUnit) || State != TurnState.ResolvingAction) return false;
            State = TurnState.SelectingAction;
            return true;
        }

        public bool TryEndTurn(UnitRuntimeState unit)
        {
            // A unit that died during its turn must still be able to leave the queue.
            if (unit == null || !ReferenceEquals(unit, ActiveUnit) || State != TurnState.SelectingAction) return false;
            RemainingMovement = 0;
            ActionAvailable = false;
            State = TurnState.TurnComplete;
            return true;
        }
    }
}
