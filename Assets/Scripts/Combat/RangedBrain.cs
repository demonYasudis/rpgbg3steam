using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Combat
{
    /// <summary>One deterministic movement plan. Prefer clear ranged shots outside melee reach.</summary>
    public static class RangedBrain
    {
        public static HexCoordinates ChooseDestination(GridModel grid, UnitRuntimeState actor,
            IReadOnlyList<UnitRuntimeState> targets, int budget, TurnManager turns)
        {
            var visible = new List<UnitRuntimeState>();
            foreach (var target in targets)
                if (target.IsPlacedOn(grid) && target.Team != actor.Team && turns.CanSee(actor, target.Position)) visible.Add(target);
            if (visible.Count == 0) return actor.Position;
            var range = HexPathfinder.FindReachable(grid, actor.Position, budget);
            var best = actor.Position;
            int bestSafety = -1, bestShot = -1, bestCost = int.MaxValue;
            long bestDistance = long.MaxValue;
            foreach (var cell in grid.Cells)
            {
                if (!range.Costs.TryGetValue(cell.Coordinates, out int cost)) continue;
                int safety = 1, shot = 0;
                long nearest = long.MaxValue;
                foreach (var target in visible)
                {
                    long distance = cell.Coordinates.DistanceTo(target.Position);
                    if (distance <= 1) safety = 0;
                    if (distance < nearest) nearest = distance;
                    if (distance >= 1 && distance <= actor.Definition.AttackRange && distance <= actor.Definition.VisionRange &&
                        HexLineOfSight.CanShoot(grid, cell.Coordinates, target.Position)) shot = 1;
                }
                // A safe firing cell beats cover without a shot. Otherwise approach visible targets.
                if (shot > bestShot || (shot == bestShot && (safety > bestSafety ||
                    (safety == bestSafety && ((shot == 0 && nearest < bestDistance) ||
                    ((shot != 0 || nearest == bestDistance) && cost < bestCost))))))
                {
                    best = cell.Coordinates; bestShot = shot; bestSafety = safety; bestCost = cost; bestDistance = nearest;
                }
            }
            return best;
        }
    }
}
