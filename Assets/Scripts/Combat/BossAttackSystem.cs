using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.HexGrid;
using GuildTactics.Units;

namespace GuildTactics.Combat
{
    /// <summary>A fixed telegraphed zone, armed on one boss turn and resolved on its next turn.</summary>
    public sealed class BossAttackSystem : IDisposable
    {
        public const int Damage = 10;
        public const int Range = 4;
        public const string BossId = "cinder-keeper";
        private readonly TurnManager turns;
        private readonly UnitRuntimeState boss;
        private readonly List<UnitRuntimeState> heroes;
        private readonly List<HexCoordinates> zone = new List<HexCoordinates>();
        private long armedTurn;
        private bool disposed;
        public IReadOnlyList<HexCoordinates> Zone { get; }
        public bool Pending => zone.Count > 0;
        public HexCoordinates Center { get; private set; }
        public event Action Changed;

        public BossAttackSystem(TurnManager turns, UnitRuntimeState boss, IEnumerable<UnitRuntimeState> heroes)
        {
            this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
            if (boss == null || boss.Team != UnitTeam.Enemy || boss.Definition.Id != BossId || !turns.Order.Contains(boss))
                throw new ArgumentException("Boss must participate in these turns.");
            this.boss = boss; this.heroes = new List<UnitRuntimeState>(heroes);
            var seen = new HashSet<UnitRuntimeState>();
            foreach (var hero in this.heroes)
                if (hero == null || hero.Team != UnitTeam.Player || !turns.Order.Contains(hero) || !seen.Add(hero)) throw new ArgumentException("Invalid boss targets.");
            Zone = zone.AsReadOnly(); boss.StateChanged += BossChanged;
        }
        private void BossChanged() { if (!boss.IsAlive) Cancel(); }

        public bool TryArm()
        {
            if (disposed || Pending || !turns.CanSelectAction(boss) || !turns.ActionAvailable ||
                (turns.Vision != null && !turns.Vision.IsVisible(boss.Position))) return false;
            UnitRuntimeState target = null;
            foreach (var hero in heroes)
                if (hero.IsPlacedOn(turns.Grid) && turns.CanSee(boss, hero.Position) &&
                    boss.Position.DistanceTo(hero.Position) <= Range && HexLineOfSight.CanShoot(turns.Grid, boss.Position, hero.Position))
                { target = hero; break; }
            if (target == null || !turns.TryBeginAction(boss)) return false;
            Center = target.Position; armedTurn = turns.TurnNumber;
            foreach (var cell in turns.Grid.Cells)
                if (TerrainRules.CanWalk(cell.Terrain) && Center.DistanceTo(cell.Coordinates) <= 1 &&
                    HexLineOfSight.CanShoot(turns.Grid, Center, cell.Coordinates)) zone.Add(cell.Coordinates);
            turns.TryCompleteAction(boss); Changed?.Invoke(); return true;
        }

        public bool TryResolve(out int hitCount)
        {
            hitCount = 0;
            if (disposed || !Pending || turns.TurnNumber <= armedTurn || !turns.CanSelectAction(boss) ||
                !turns.ActionAvailable || !turns.TryBeginAction(boss)) return false;
            var victims = heroes.FindAll(h => h.IsPlacedOn(turns.Grid) && zone.Contains(h.Position));
            // Clear before committing damage: state events and repeated calls cannot apply it twice.
            zone.Clear(); Changed?.Invoke();
            foreach (var hero in victims) { hero.ApplyDamage(Damage); hitCount++; }
            turns.TryCompleteAction(boss); return true;
        }
        public void Cancel() { if (!Pending) return; zone.Clear(); Changed?.Invoke(); }
        public void Dispose() { disposed = true; boss.StateChanged -= BossChanged; Cancel(); }
    }

}
