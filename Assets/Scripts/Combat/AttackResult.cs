using GuildTactics.HexGrid;

namespace GuildTactics.Combat
{
    /// <summary>Immutable roll snapshot for feedback; it cannot apply damage a second time.</summary>
    public sealed class AttackResult
    {
        public HexCoordinates TargetPosition { get; }
        public int AttackRoll { get; }
        public int AttackBonus { get; }
        public long AttackTotal => (long)AttackRoll + AttackBonus;
        public int Defense { get; }
        public bool Hit => AttackTotal >= Defense;
        public int DamageDie { get; }
        public int DamageRoll { get; }
        public int DamageBonus { get; }
        public int Damage { get; }
        public int AppliedDamage { get; }
        public bool Killed { get; }

        internal AttackResult(HexCoordinates targetPosition, int attackRoll, int attackBonus, int defense,
            int damageDie, int damageRoll, int damageBonus, int damage, int appliedDamage, bool killed)
        {
            TargetPosition = targetPosition;
            AttackRoll = attackRoll;
            AttackBonus = attackBonus;
            Defense = defense;
            DamageDie = damageDie;
            DamageRoll = damageRoll;
            DamageBonus = damageBonus;
            Damage = damage;
            AppliedDamage = appliedDamage;
            Killed = killed;
        }
    }
}
