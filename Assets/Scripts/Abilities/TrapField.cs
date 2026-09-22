using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;

namespace GuildTactics.Abilities
{
    public sealed class ArmedTrap
    {
        public string OwnerId { get; }
        public UnitTeam Team { get; }
        public HexCoordinates Position { get; }
        public int Damage { get; }
        internal ArmedTrap(UnitRuntimeState owner, HexCoordinates position, int damage)
        { OwnerId = owner.InstanceId; Team = owner.Team; Position = position; Damage = damage; }
    }

    public sealed class TrapHit
    {
        public HexCoordinates Position { get; }
        public int Damage { get; }
        public bool Killed { get; }
        internal TrapHit(HexCoordinates position, int damage, bool killed)
        { Position = position; Damage = damage; Killed = killed; }
    }

    /// <summary>One persistent trap per owner. Allies cross safely; hostile entry consumes it.</summary>
    public sealed class TrapField
    {
        private readonly List<ArmedTrap> traps = new List<ArmedTrap>();
        public IReadOnlyList<ArmedTrap> Traps => traps.AsReadOnly();
        public bool Contains(HexCoordinates position) => traps.Exists(trap => trap.Position == position);

        internal void Place(UnitRuntimeState owner, HexCoordinates position, int damage)
        {
            traps.RemoveAll(trap => trap.OwnerId == owner.InstanceId);
            traps.Add(new ArmedTrap(owner, position, damage));
        }

        // Predict only the stopping point. Occupancy still commits once, before damage is applied.
        internal IReadOnlyList<HexCoordinates> LimitPath(UnitRuntimeState unit, IReadOnlyList<HexCoordinates> path)
        {
            long health = unit.CurrentHealth;
            for (int i = 1; i < path.Count; i++)
            {
                var trap = traps.Find(item => item.Position == path[i] && item.Team != unit.Team);
                if (trap == null) continue;
                health -= trap.Damage;
                if (health > 0) continue;
                var shortened = new List<HexCoordinates>();
                for (int j = 0; j <= i; j++) shortened.Add(path[j]);
                return shortened.AsReadOnly();
            }
            return path;
        }

        internal IReadOnlyList<TrapHit> TriggerPath(UnitRuntimeState unit, IReadOnlyList<HexCoordinates> path)
        {
            var hits = new List<TrapHit>();
            for (int i = 1; i < path.Count && unit.IsAlive; i++)
            {
                var hit = TriggerEntry(unit, path[i]);
                if (hit != null) hits.Add(hit);
            }
            return hits.AsReadOnly();
        }

        internal TrapHit TriggerEntry(UnitRuntimeState unit, HexCoordinates position)
        {
            var trap = traps.Find(item => item.Position == position && item.Team != unit.Team);
            if (trap == null || !unit.IsAlive) return null;
            traps.Remove(trap);
            int applied = unit.ApplyDamage(trap.Damage);
            return new TrapHit(position, applied, !unit.IsAlive);
        }
    }
}
