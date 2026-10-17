using System;
using System.Linq;
using GuildTactics.Abilities;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEngine;
using Grid = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class ActionForecastChecks
    {
        private sealed class Dice : IDice
        {
            public int RollValue = 20, Calls;
            public int Roll(int sides) { Calls++; return Math.Min(RollValue, sides); }
        }
        public static void Run()
        {
            // Enumerate every d20 outcome, including the project's non-critical natural 1/20.
            foreach (var hero in HeroDefinitions.Defaults)
                for (int roll = 1; roll <= 20; roll++)
                {
                    var grid = new Grid(9, 6);
                    var actor = Spawn(grid, "actor", hero, new HexCoordinates(1, 1));
                    var victim = Spawn(grid, "victim", new UnitDefinition("enemy", "Enemy", 1, maxHealth: 100), new HexCoordinates(2, 1), UnitTeam.Enemy);
                    var turns = new TurnManager(grid, new[] { actor, victim }); turns.TryStartNextTurn();
                    var dice = new Dice { RollValue = roll }; var combat = new CombatSystem(grid, turns, dice);
                    var preview = new AttackForecast(actor, victim);
                    Require(preview.HitPercent == 65 && preview.MinimumDamage == 3 && preview.MaximumDamage == 8, "Baseline exact probability and damage");
                    Require(dice.Calls == 0 && turns.ActionAvailable && victim.CurrentHealth == 100, "Preview spends nothing");
                    Require(combat.TryAttack(actor, victim, out var result) && preview.Hits(roll) == result.Hit &&
                        (!result.Hit || (result.Damage == preview.DamageForRoll(Math.Min(roll, actor.DamageDie)) && result.Damage >= preview.MinimumDamage && result.Damage <= preview.MaximumDamage)), "Every basic roll matches forecast for " + hero.Id);
                    Require(!combat.TryAttack(actor, victim, out _), "Duplicate basic attack rejected");
                }
            foreach (int attack in new[] { int.MinValue, -100, 100, int.MaxValue })
            {
                var grid = new Grid();
                var actor = Spawn(grid, "a", new UnitDefinition("a", "A", 1, attack: attack, damageBonus: int.MaxValue), new HexCoordinates(1, 1));
                var victim = Spawn(grid, "b", HeroDefinitions.Defaults[0], new HexCoordinates(2, 1), UnitTeam.Enemy);
                var preview = new AttackForecast(actor, victim, int.MaxValue, int.MaxValue);
                Require(preview.HitPercent == (attack == int.MinValue ? 40 : 100) && preview.MaximumDamage == int.MaxValue, "Saturating arithmetic matches combat");
                var impossible = new AttackForecast(actor, victim, int.MinValue, int.MinValue);
                Require(impossible.HitPercent == (attack == int.MaxValue ? 40 : 0) && impossible.MinimumDamage == 0, "Zero chance, zero floored damage");
            }
            foreach (var hero in HeroDefinitions.Defaults)
                foreach (var ability in hero.Abilities)
                {
                    var grid = new Grid(10, 7);
                    var actor = Spawn(grid, "actor", hero, new HexCoordinates(1, 1));
                    var victim = Spawn(grid, "victim", new UnitDefinition("enemy", "Enemy", 1, maxHealth: 100), new HexCoordinates(2, 1), UnitTeam.Enemy);
                    var ally = Spawn(grid, "ally", new UnitDefinition("ally", "Ally", 1), new HexCoordinates(2, 0));
                    var turns = new TurnManager(grid, new[] { actor, victim, ally }); turns.TryStartNextTurn();
                    var dice = new Dice(); var system = new AbilitySystem(grid, turns, dice, new[] { actor, victim, ally });
                    var target = ability.Effect == AbilityEffect.Evade ? actor.Position :
                        ability.Effect == AbilityEffect.Trap || ability.Effect == AbilityEffect.Blink ? new HexCoordinates(1, 2) : victim.Position;
                    for (int i = 0; i < 100; i++)
                    {
                        Require(system.CanUse(actor, ability, target, out _), "Signature valid: " + ability.Id);
                        system.PreviewAttacks(actor, ability, target);
                    }
                    var forecasts = system.PreviewAttacks(actor, ability, target);
                    Require(dice.Calls == 0 && turns.ActionAvailable && turns.RemainingMovement == hero.Movement && victim.CurrentHealth == 100, "All signatures preview without mutations");
                    Require(system.TryUse(actor, ability, target, out var result, out _), "Commit " + ability.Id);
                    Require(forecasts.Count == result.Attacks.Count, "Exact signature target set");
                    for (int i = 0; i < forecasts.Count; i++)
                        Require(forecasts[i].AttackBonus == result.Attacks[i].AttackBonus && forecasts[i].MaximumDamage == result.Attacks[i].Damage && forecasts[i].TargetPosition == result.Attacks[i].TargetPosition, "Signature modifier/target agreement");
                    Require(!system.TryUse(actor, ability, target, out _, out _), "Double signature rejected");
                }
            CheckVisibilityAndRange();
            Debug.Log("WP-40 FORECAST MODEL CHECKS PASSED: 80 basic outcomes, 0/100 percent and overflow, eight signatures, repeated pure previews, exact area targets, fog, walls, range, spent actions and duplicate commits.");
        }
        private static void CheckVisibilityAndRange()
        {
            var grid = new Grid(10, 7);
            var definition = new UnitDefinition("mage", "Mage", 4, 10, attackRange: 4, visionRange: 2, abilities: HeroDefinitions.Defaults[3].Abilities);
            var actor = Spawn(grid, "a", definition, new HexCoordinates(1, 1));
            var visible = Spawn(grid, "v", new UnitDefinition("v", "V", 1), new HexCoordinates(3, 1), UnitTeam.Enemy);
            var hidden = Spawn(grid, "h", new UnitDefinition("h", "H", 1), new HexCoordinates(4, 1), UnitTeam.Enemy);
            var units = new[] { actor, visible, hidden };
            using var fog = new FogOfWarSystem(grid, new[] { actor });
            var turns = new TurnManager(grid, units, fog); turns.TryStartNextTurn();
            var dice = new Dice(); var abilities = new AbilitySystem(grid, turns, dice, units);
            var fire = definition.Abilities[0];
            Require(abilities.PreviewAttacks(actor, fire, visible.Position).Count == 1 && abilities.PreviewAttacks(actor, fire, hidden.Position).Count == 0, "Area excludes hidden victims and hidden center");
            var combat = new CombatSystem(grid, turns, dice);
            Require(combat.CanAttack(actor, visible) && !combat.CanAttack(actor, hidden), "Fog validation");
            grid.GetCell(new HexCoordinates(2, 1)).Terrain = TerrainType.Blocked;
            Require(!combat.CanAttack(actor, visible, out var reason) && reason == "Walls block the line to this target." && abilities.PreviewAttacks(actor, fire, visible.Position).Count == 0, "Wall validation shared");
            grid.GetCell(new HexCoordinates(2, 1)).Terrain = TerrainType.Ground;
            Require(visible.TryRelocate(grid, new HexCoordinates(5, 1)), "Place range boundary");
            var noFogTurns = new TurnManager(grid, units); noFogTurns.TryStartNextTurn();
            var noFogCombat = new CombatSystem(grid, noFogTurns, dice);
            Require(noFogCombat.CanAttack(actor, visible), "Inclusive basic range");
            Require(visible.TryRelocate(grid, new HexCoordinates(6, 1)) && !noFogCombat.CanAttack(actor, visible), "Outside range");
            Require(visible.TryRelocate(grid, new HexCoordinates(3, 1)), "Restore victim");
            Require(turns.TryBeginAction(actor) && !combat.CanAttack(actor, visible) && abilities.PreviewAttacks(actor, fire, visible.Position).Count == 0 && dice.Calls == 0, "Spent action preview cannot execute or roll");
        }
        private static UnitRuntimeState Spawn(Grid grid, string id, UnitDefinition definition, HexCoordinates cell, UnitTeam team = UnitTeam.Player)
        { Require(UnitRuntimeState.TrySpawn(grid, id, definition, cell, out var unit, team), "Spawn " + id); return unit; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-40: " + message); }
    }
}
