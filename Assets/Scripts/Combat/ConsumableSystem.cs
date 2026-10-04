using GuildTactics.Units;

namespace GuildTactics.Combat
{
    public static class ConsumableSystem
    {
        public static bool CanHeal(TurnManager turns, UnitRuntimeState actor) =>
            turns != null && turns.CanSelectAction(actor) && turns.ActionAvailable && actor.Team == UnitTeam.Player &&
            actor.IsPlacedOn(turns.Grid) && actor.HealingPotions > 0 && actor.CurrentHealth < actor.Definition.MaxHealth;

        // HP and stock commit once; the presentation only releases the action lock.
        public static bool TryHeal(TurnManager turns, UnitRuntimeState actor, out int healed)
        {
            healed = 0;
            if (!CanHeal(turns, actor) || !turns.TryBeginAction(actor)) return false;
            healed = actor.DrinkHealingPotion();
            return true;
        }
    }
}
