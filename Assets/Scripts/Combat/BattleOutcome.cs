using System.Collections.Generic;
using GuildTactics.Units;

namespace GuildTactics.Combat
{
    public enum BattleOutcome { Ongoing, Victory, Defeat }

    public static class BattleRules
    {
        public static BattleOutcome Evaluate(IEnumerable<UnitRuntimeState> participants)
        {
            bool playersAlive = false;
            bool enemiesAlive = false;
            foreach (var unit in participants)
            {
                if (!unit.IsAlive) continue;
                if (unit.Team == UnitTeam.Player) playersAlive = true;
                else enemiesAlive = true;
            }
            if (!playersAlive) return BattleOutcome.Defeat;
            return enemiesAlive ? BattleOutcome.Ongoing : BattleOutcome.Victory;
        }
    }
}
