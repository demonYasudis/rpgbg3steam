using System;
using GuildTactics.Units;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Combat
{
    /// <summary>Validates a basic attack, consumes one action and commits its result exactly once.</summary>
    public sealed class CombatSystem
    {
        private readonly GridModel grid;
        private readonly TurnManager turns;
        private readonly IDice dice;

        public CombatSystem(GridModel grid, TurnManager turns, IDice dice)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
            this.dice = dice ?? throw new ArgumentNullException(nameof(dice));
            if (!ReferenceEquals(grid, turns.Grid))
                throw new ArgumentException("Combat and turns must use the same grid.", nameof(turns));
        }

        public bool CanAttack(UnitRuntimeState attacker, UnitRuntimeState target) =>
            turns.CanSelectAction(attacker) && turns.ActionAvailable &&
            attacker.IsPlacedOn(grid) && target != null && target.IsPlacedOn(grid) &&
            attacker.Team != target.Team && attacker.Position.DistanceTo(target.Position) >= 1 &&
            attacker.Position.DistanceTo(target.Position) <= attacker.Definition.AttackRange;

        public bool TryAttack(UnitRuntimeState attacker, UnitRuntimeState target, out AttackResult result)
        {
            result = null;
            if (!CanAttack(attacker, target) || !turns.TryBeginAction(attacker)) return false;
            try
            {
                result = PrepareAttack(dice, attacker, target);
                target.ApplyDamage(result.Damage);
                // Remain locked until presentation completes. No damage is deferred to that callback.
                return true;
            }
            catch
            {
                // A broken dice provider must not strand the turn in ResolvingAction.
                // The attempted action stays spent; no HP change occurs before both valid rolls.
                turns.TryCompleteAction(attacker);
                throw;
            }
        }

        // Prepare all rolls before committing any HP (also used for area abilities).
        internal static AttackResult PrepareAttack(IDice dice, UnitRuntimeState attacker,
            UnitRuntimeState target, int attackModifier = 0, int damageModifier = 0)
        {
            int bonus = Clamp((long)attacker.Definition.Attack + attackModifier);
            int damageBonus = Clamp((long)attacker.Definition.DamageBonus + damageModifier);
            int attackRoll = RollChecked(dice, 20);
            bool hit = (long)attackRoll + bonus >= target.Defense;
            int damageRoll = hit ? RollChecked(dice, attacker.Definition.DamageDie) : 0;
            int damage = hit ? (int)Math.Max(0L, Math.Min(int.MaxValue, (long)damageRoll + damageBonus)) : 0;
            return new AttackResult(target.Position, attackRoll, bonus, target.Defense,
                attacker.Definition.DamageDie, damageRoll, damageBonus, damage,
                Math.Min(target.CurrentHealth, damage), damage >= target.CurrentHealth);
        }

        private static int Clamp(long value) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));

        private static int RollChecked(IDice dice, int sides)
        {
            int roll = dice.Roll(sides);
            if (roll < 1 || roll > sides)
                throw new InvalidOperationException("Dice returned a value outside 1.." + sides + ".");
            return roll;
        }
    }
}
