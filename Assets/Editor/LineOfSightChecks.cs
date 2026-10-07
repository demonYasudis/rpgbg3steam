using System;
using System.Linq;
using GuildTactics.Abilities;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class LineOfSightChecks
    {
        private sealed class MaxDice : IDice { public int Roll(int sides) => sides; }
        [MenuItem("Tools/Guild Tactics/Validate Wall Occlusion")]
        public static void Run()
        {
            var grid = new GridModel(8, 8);
            foreach (var a in grid.Cells)
                foreach (var b in grid.Cells) Require(HexLineOfSight.CanSee(grid, a.Coordinates, b.Coordinates), "Open field");
            grid.GetCell(new HexCoordinates(3, 3)).Terrain = TerrainType.Blocked;
            foreach (var a in grid.Cells)
                foreach (var b in grid.Cells)
                    Require(HexLineOfSight.CanSee(grid, a.Coordinates, b.Coordinates) ==
                        HexLineOfSight.CanSee(grid, b.Coordinates, a.Coordinates), "Symmetric reverse rays");
            Require(!HexLineOfSight.CanSee(grid, new HexCoordinates(1, 3), new HexCoordinates(5, 3)) &&
                HexLineOfSight.CanSee(grid, new HexCoordinates(1, 3), new HexCoordinates(3, 3)) &&
                !HexLineOfSight.CanShoot(grid, new HexCoordinates(1, 3), new HexCoordinates(3, 3)), "Wall visible, blocks shots and beyond");
            Require(!HexLineOfSight.CanSee(grid, new HexCoordinates(-1, 0), new HexCoordinates(0, 0)), "Invalid edge");
            for (int d = 0; d < 6; d++)
            {
                var center = new HexCoordinates(3, 3); var neighbor = center.GetNeighbor(d);
                Require(HexLineOfSight.CanSee(grid, neighbor, center), "All six adjacent directions");
            }
            var seam = new GridModel(3, 3);
            seam.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.Blocked;
            Require(!HexLineOfSight.CanSee(seam, new HexCoordinates(0, 0), new HexCoordinates(1, 1)), "Closed shared-edge supercover");
            seam.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.Ground;
            seam.GetCell(new HexCoordinates(0, 1)).Terrain = TerrainType.Blocked;
            Require(!HexLineOfSight.CanSee(seam, new HexCoordinates(0, 0), new HexCoordinates(1, 1)), "Other tied cell also blocks");

            grid = new GridModel(7, 5);
            var abilities = HeroDefinitions.Defaults[3].Abilities.Concat(HeroDefinitions.Defaults[2].Abilities).ToArray();
            UnitRuntimeState.TrySpawn(grid, "caster", new UnitDefinition("caster", "Caster", 4, 10,
                attackRange: 6, abilities: abilities, visionRange: 6), new HexCoordinates(1, 2), out var actor);
            UnitRuntimeState.TrySpawn(grid, "ally", HeroDefinitions.Defaults[0], new HexCoordinates(4, 1), out var ally);
            UnitRuntimeState.TrySpawn(grid, "enemy", new UnitDefinition("enemy", "Enemy", 3, attackRange: 6, visionRange: 6),
                new HexCoordinates(4, 2), out var enemy, UnitTeam.Enemy);
            grid.GetCell(new HexCoordinates(2, 2)).Terrain = TerrainType.Blocked;
            using var fog = new FogOfWarSystem(grid, new[] { actor });
            Require(!fog.IsVisible(enemy.Position) && fog.IsVisible(new HexCoordinates(2, 2)), "Walls hide enemy but remain visible");
            fog.Register(ally);
            Require(fog.IsVisible(enemy.Position), "Ally reveals other side");
            var turns = new TurnManager(grid, new[] { actor, ally, enemy }, fog); turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new MaxDice());
            var system = new AbilitySystem(grid, turns, new MaxDice(), turns.Order);
            Require(!combat.CanAttack(actor, enemy) && !combat.TryAttack(actor, enemy, out _) && turns.ActionAvailable, "Party vision does not permit shots through walls");
            Require(!system.CanUse(actor, abilities[2], enemy.Position, out _) &&
                !system.CanUse(actor, abilities[0], enemy.Position, out _), "Aimed shot and burst center need line");
            Require(system.CanUse(actor, abilities[1], new HexCoordinates(3, 2), out _) &&
                system.TryUse(actor, abilities[1], new HexCoordinates(3, 2), out _, out _), "Blink crosses wall to visible free ground");
            turns.TryCompleteAction(actor); turns.TryEndTurn(actor); turns.TryStartNextTurn();
            turns.TryEndTurn(ally); turns.TryStartNextTurn();
            Require(turns.ActiveUnit == enemy && turns.CanSee(enemy, actor.Position) && combat.CanAttack(enemy, actor), "Enemy uses shared geometry");
            actor.TryRelocate(grid, new HexCoordinates(1, 2));
            Require(!turns.CanSee(enemy, actor.Position) && !combat.CanAttack(enemy, actor), "Enemy sight blocked after relocation");
            ally.ApplyDamage(ally.CurrentHealth);
            Require(!fog.IsVisible(enemy.Position) && fog.GetState(enemy.Position) == CellVisibility.Explored, "Death removes ally vision; memory remains");
            actor.TryRelocate(grid, new HexCoordinates(3, 2));
            Require(fog.IsVisible(enemy.Position), "Movement refreshes occlusion");
            CheckBlastOcclusion();
            Debug.Log("WP-25 passed: symmetry, edges, supercover ties, wall endpoints, player/enemy sight, direct attacks, Blink and vision refresh.");
        }
        private static void CheckBlastOcclusion()
        {
            var grid = new GridModel(8, 5);
            var burst = new AbilityDefinition("test-burst", "Test burst", "", AbilityEffect.FireBurst, 6, radius: 3);
            UnitRuntimeState.TrySpawn(grid, "caster", new UnitDefinition("caster", "Caster", 3, 10, abilities: new[] { burst }),
                new HexCoordinates(1, 2), out var actor);
            UnitRuntimeState.TrySpawn(grid, "ally", HeroDefinitions.Defaults[0], new HexCoordinates(4, 1), out var ally);
            UnitRuntimeState.TrySpawn(grid, "shielded", HeroDefinitions.Defaults[0], new HexCoordinates(4, 2), out var shielded, UnitTeam.Enemy);
            UnitRuntimeState.TrySpawn(grid, "exposed", HeroDefinitions.Defaults[0], new HexCoordinates(2, 3), out var exposed, UnitTeam.Enemy);
            grid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.Blocked;
            using var fog = new FogOfWarSystem(grid, new[] { actor, ally });
            var turns = new TurnManager(grid, new[] { actor, ally, shielded, exposed }, fog); turns.TryStartNextTurn();
            var system = new AbilitySystem(grid, turns, new MaxDice(), turns.Order);
            var center = new HexCoordinates(2, 2);
            Require(fog.IsVisible(shielded.Position) && system.CanUse(actor, burst, center, out _) &&
                system.TryUse(actor, burst, center, out var result, out _) && result.Attacks.Count == 1 &&
                shielded.CurrentHealth == shielded.Definition.MaxHealth && exposed.CurrentHealth < exposed.Definition.MaxHealth,
                "Blast hits only visible enemies with a clear center-to-victim line");
            grid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.HighGround;
            Require(HexLineOfSight.CanShoot(grid, center, shielded.Position), "High ground does not block");
            grid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.Pit;
            Require(HexLineOfSight.CanShoot(grid, center, shielded.Position), "Pits do not block");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-25: " + message); }
    }
}
