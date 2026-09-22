using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Combat
{
    /// <summary>Deterministic shortest-cost approach to a reachable attack cell.</summary>
    public static class MeleeBrain
    {
        public static UnitRuntimeState FindTarget(CombatSystem combat, UnitRuntimeState actor,
            IReadOnlyList<UnitRuntimeState> targets)
        {
            foreach (var target in targets)
                if (combat.CanAttack(actor, target)) return target;
            return null;
        }

        public static HexCoordinates ChooseDestination(GridModel grid, UnitRuntimeState actor,
            IReadOnlyList<UnitRuntimeState> targets, int budget)
        {
            var range = HexPathfinder.FindReachable(grid, actor.Position, int.MaxValue);
            IReadOnlyList<HexCoordinates> bestPath = null;
            int bestCost = int.MaxValue;
            // Target list and neighbor order also break equal-cost ties consistently.
            foreach (var target in targets)
            {
                if (!target.IsPlacedOn(grid) || target.Team == actor.Team) continue;
                foreach (var cell in grid.GetNeighbors(target.Position))
                {
                    if (!range.Costs.TryGetValue(cell.Coordinates, out int cost) || cost >= bestCost) continue;
                    bestCost = cost;
                    bestPath = range.GetPathTo(cell.Coordinates);
                }
            }
            var destination = actor.Position;
            if (bestPath == null) return destination;
            for (int i = 1; i < bestPath.Count; i++)
            {
                int cost = grid.GetCell(bestPath[i]).MovementCost;
                if (cost > budget) break;
                budget -= cost;
                destination = bestPath[i];
            }
            return destination;
        }
    }
}
