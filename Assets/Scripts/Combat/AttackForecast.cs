using System;
using GuildTactics.Units;
using GuildTactics.HexGrid;

namespace GuildTactics.Combat
{
    /// <summary>Pure attack arithmetic shared by the preview and the committed roll. No dice calls.</summary>
    public sealed class AttackForecast
    {
        public HexCoordinates TargetPosition { get; }
        public int AttackBonus { get; }
        public int Defense { get; }
        public int DamageDie { get; }
        public int DamageBonus { get; }
        public int HitPercent { get; }
        public int MinimumDamage => DamageForRoll(1);
        public int MaximumDamage => DamageForRoll(DamageDie);
        public AttackForecast(UnitRuntimeState attacker, UnitRuntimeState target, int attackModifier = 0, int damageModifier = 0)
        {
            if (attacker == null || target == null) throw new ArgumentNullException();
            TargetPosition = target.Position;
            AttackBonus = Clamp((long)attacker.Attack + attackModifier);
            Defense = target.Defense; DamageDie = attacker.DamageDie;
            DamageBonus = Clamp((long)attacker.Definition.DamageBonus + damageModifier);
            int hits = 0;
            for (int roll = 1; roll <= 20; roll++) if (Hits(roll)) hits++;
            HitPercent = hits * 5;
        }
        public bool Hits(int roll) => (long)roll + AttackBonus >= Defense;
        public int DamageForRoll(int roll) => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)roll + DamageBonus));
        private static int Clamp(long value) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));
    }
}
