using System;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Abilities
{
    public sealed class AbilityResult
    {
        public AbilityDefinition Ability { get; }
        public HexCoordinates Position { get; }
        public IReadOnlyList<AttackResult> Attacks { get; }
        public TrapHit TrapHit { get; }
        public bool FellIntoPit { get; }
        internal AbilityResult(AbilityDefinition ability, HexCoordinates position,
            List<AttackResult> attacks, TrapHit trapHit, bool fellIntoPit)
        { Ability = ability; Position = position; Attacks = attacks.AsReadOnly(); TrapHit = trapHit; FellIntoPit = fellIntoPit; }
    }

    /// <summary>Single validation/commit pipeline for the eight signature abilities.</summary>
    public sealed class AbilitySystem
    {
        private readonly GridModel grid;
        private readonly TurnManager turns;
        private readonly IDice dice;
        private readonly List<UnitRuntimeState> units;

        public AbilitySystem(GridModel grid, TurnManager turns, IDice dice, IEnumerable<UnitRuntimeState> units)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
            this.dice = dice ?? throw new ArgumentNullException(nameof(dice));
            if (!ReferenceEquals(grid, turns.Grid)) throw new ArgumentException("Abilities and turns require the same grid.");
            if (units == null) throw new ArgumentNullException(nameof(units));
            this.units = new List<UnitRuntimeState>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                if (unit == null || !unit.IsPlacedOn(grid) || !ids.Add(unit.InstanceId))
                    throw new ArgumentException("Ability participants must be unique units on this grid.", nameof(units));
                this.units.Add(unit);
            }
            foreach (var unit in turns.Order)
                if (!this.units.Contains(unit)) throw new ArgumentException("Every turn participant must be included.", nameof(units));
        }

        public bool CanUse(UnitRuntimeState actor, AbilityDefinition ability, HexCoordinates target, out string reason)
        {
            reason = null;
            if (!turns.CanSelectAction(actor) || !turns.ActionAvailable || !actor.IsPlacedOn(grid))
                return Reject("No action available for this unit.", out reason);
            if (ability == null || !Owns(actor, ability)) return Reject("This unit does not know that ability.", out reason);
            if (!grid.Contains(target) || actor.Position.DistanceTo(target) > ability.Range)
                return Reject("Target is out of range.", out reason);
            if (!turns.CanSee(actor, target)) return Reject("Target is outside current vision.", out reason);
            var enemy = EnemyAt(actor, target);
            switch (ability.Effect)
            {
                case AbilityEffect.HeavyStrike:
                case AbilityEffect.AimedShot:
                    return enemy != null || Reject("Choose a living enemy.", out reason);
                case AbilityEffect.Backstab:
                    if (enemy == null) return Reject("Choose a living enemy.", out reason);
                    foreach (var ally in units)
                        if (ally != actor && ally.Team == actor.Team && ally.IsPlacedOn(grid) &&
                            ally.Position.DistanceTo(target) == 1) return true;
                    return Reject("Backstab needs another ally next to the enemy.", out reason);
                case AbilityEffect.Push:
                    return (enemy != null && actor.Position.DistanceTo(target) == 1 &&
                        turns.CanSee(actor, PushDestination(actor, enemy)) &&
                        FreePushLanding(PushDestination(actor, enemy))) ||
                        Reject("Push needs an adjacent enemy and free ground or a pit behind it.", out reason);
                case AbilityEffect.Evade:
                    return target == actor.Position || Reject("Choose your own hex.", out reason);
                case AbilityEffect.Trap:
                    return (FreeGround(target) && !turns.Traps.Contains(target)) ||
                        Reject("Choose free ground without a trap.", out reason);
                case AbilityEffect.Blink:
                    return FreeGround(target) || Reject("Choose free ground.", out reason);
                case AbilityEffect.FireBurst:
                    return AreaTargets(actor, target, ability.Radius).Count > 0 ||
                        Reject("The burst must reach at least one enemy.", out reason);
                default: return Reject("Unsupported ability.", out reason);
            }
        }

        public bool TryUse(UnitRuntimeState actor, AbilityDefinition ability, HexCoordinates target,
            out AbilityResult result, out string reason)
        {
            result = null;
            if (!CanUse(actor, ability, target, out reason) || !turns.TryBeginAction(actor)) return false;
            try
            {
                var attacks = new List<AttackResult>();
                TrapHit trapHit = null;
                bool fellIntoPit = false;
                switch (ability.Effect)
                {
                    case AbilityEffect.HeavyStrike:
                    case AbilityEffect.Backstab:
                    case AbilityEffect.AimedShot:
                    case AbilityEffect.FireBurst:
                        var targets = ability.Effect == AbilityEffect.FireBurst
                            ? AreaTargets(actor, target, ability.Radius)
                            : new List<UnitRuntimeState> { EnemyAt(actor, target) };
                        foreach (var victim in targets)
                            attacks.Add(CombatSystem.PrepareAttack(dice, actor, victim, ability.AttackBonus, ability.Power));
                        for (int i = 0; i < targets.Count; i++) targets[i].ApplyDamage(attacks[i].Damage);
                        break;
                    case AbilityEffect.Push:
                        var pushed = EnemyAt(actor, target);
                        target = PushDestination(actor, pushed);
                        if (!pushed.TryPushTo(grid, target)) throw new InvalidOperationException("Validated push failed.");
                        fellIntoPit = grid.GetCell(target).Terrain == TerrainType.Pit;
                        trapHit = turns.Traps.TriggerEntry(pushed, target);
                        break;
                    case AbilityEffect.Evade:
                        actor.SetEvasion(ability.Power);
                        break;
                    case AbilityEffect.Trap:
                        turns.Traps.Place(actor, target, ability.Power);
                        break;
                    case AbilityEffect.Blink:
                        if (!actor.TryRelocate(grid, target)) throw new InvalidOperationException("Validated blink failed.");
                        trapHit = turns.Traps.TriggerEntry(actor, target);
                        break;
                }
                result = new AbilityResult(ability, target, attacks, trapHit, fellIntoPit);
                return true;
            }
            catch
            {
                // Invalid dice spend the attempt but neither partially damage an area nor strand the turn.
                turns.TryCompleteAction(actor);
                throw;
            }
        }

        private UnitRuntimeState EnemyAt(UnitRuntimeState actor, HexCoordinates target) =>
            units.Find(unit => unit.Team != actor.Team && unit.Position == target && unit.IsPlacedOn(grid));

        private List<UnitRuntimeState> AreaTargets(UnitRuntimeState actor, HexCoordinates target, int radius) =>
            units.FindAll(unit => unit.Team != actor.Team && unit.IsPlacedOn(grid) &&
                turns.CanSee(actor, unit.Position) && unit.Position.DistanceTo(target) <= radius);

        private bool FreeGround(HexCoordinates target) => grid.TryGetCell(target, out var cell) &&
            !cell.IsOccupied && TerrainRules.CanWalk(cell.Terrain);

        private bool FreePushLanding(HexCoordinates target) => grid.TryGetCell(target, out var cell) &&
            !cell.IsOccupied && TerrainRules.CanPushInto(cell.Terrain);

        private static HexCoordinates PushDestination(UnitRuntimeState actor, UnitRuntimeState target) =>
            new HexCoordinates(checked(target.Position.Q + target.Position.Q - actor.Position.Q),
                checked(target.Position.R + target.Position.R - actor.Position.R));

        private static bool Owns(UnitRuntimeState actor, AbilityDefinition ability)
        {
            foreach (var known in actor.Definition.Abilities) if (ReferenceEquals(known, ability)) return true;
            return false;
        }

        private static bool Reject(string message, out string reason) { reason = message; return false; }
    }
}
