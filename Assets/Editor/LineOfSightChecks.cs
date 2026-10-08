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
        private sealed class CountingDice : IDice
        {
            public int Calls { get; private set; }
            public int Roll(int sides) { Calls++; return sides; }
        }

        [MenuItem("Tools/Guild Tactics/Validate Line Of Sight")]
        public static void Run()
        {
            CheckGeometry();
            CheckFogAndEnemyVision();
            CheckAttacksAndBlink();
            CheckBurst();
            Debug.Log("WP-25 model checks passed: symmetric geometry, corners/edges, wall memory, observer lifecycle, player/AI targeting, atomic rejection, blink and blast occlusion.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static PlayerUnitController controller;
        private static GridModel presentationGrid;
        private static HexGridView presentationView;
        private static UnitRuntimeState presentationEnemy;

        internal static void BeginPresentation(PlayerUnitController scene)
        {
            presentationGrid = new GridModel();
            WallColumn(presentationGrid, 3);
            var layout = new HexLayout();
            var root = new GameObject("WP25 Line Of Sight Checks");
            presentationView = root.AddComponent<HexGridView>();
            presentationView.Initialize(presentationGrid, layout);
            var input = root.AddComponent<HexGridInteraction>();
            input.Initialize(presentationGrid, layout, presentationView, scene.GetComponent<HexGridInteraction>().GridCamera);
            controller = root.AddComponent<PlayerUnitController>();
            controller.Initialize(presentationGrid, layout, presentationView, input, 0.02f, new CountingDice(), enableFog: true,
                playerSpawns: new[] { new HexCoordinates(4, 0), new HexCoordinates(1, 2),
                    new HexCoordinates(2, 1), new HexCoordinates(2, 2) });
            presentationEnemy = controller.Enemies[0];
            Require(presentationEnemy.TryRelocate(presentationGrid, new HexCoordinates(4, 1)), "Place enemy beyond wall");
            controller.GetComponentsInChildren<UnitView>(true).Single(view => view.State == presentationEnemy)
                .SnapTo(layout.ToWorld(presentationEnemy.Position));
            controller.CancelTargeting();
            Require(controller.TryEndTurn(), "Pass Rogue turn to Ranger");
        }

        internal static bool PollPresentation()
        {
            if (controller.Turns.State == TurnState.TurnComplete) return false;
            Require(controller.SelectedUnit.Definition.Id == "ranger", "Ranger owns targeting check");
            // Inspect the view's existing overlay state; no test-only API is added to production components.
            var targets = (System.Collections.Generic.HashSet<HexCoordinates>)typeof(HexGridView)
                .GetField("targets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(presentationView);
            Require(controller.IsUnitVisible(presentationEnemy) && controller.SelectBasicAttack() &&
                !targets.Contains(presentationEnemy.Position) && !controller.TryAttackSelected(presentationEnemy.Position) &&
                controller.Turns.ActionAvailable, "Visible blocked enemy is neither highlighted nor attackable");
            Require(controller.SelectAbility(controller.SelectedUnit.Definition.Abilities[0]) &&
                !targets.Contains(presentationEnemy.Position) && !controller.TryUseSelectedAbility(presentationEnemy.Position) &&
                controller.Turns.ActionAvailable, "Aimed Shot overlay agrees with commit");
            controller.Units[0].ApplyDamage(controller.Units[0].CurrentHealth);
            controller.SelectBasicAttack();
            var enemyView = controller.GetComponentsInChildren<UnitView>(true).Single(view => view.State == presentationEnemy);
            Require(!controller.IsUnitVisible(presentationEnemy) && !enemyView.gameObject.activeSelf &&
                !targets.Contains(presentationEnemy.Position), "Scout death hides enemy sprite and targeting overlay");
            presentationGrid.GetCell(new HexCoordinates(3, 1)).Terrain = TerrainType.Ground;
            controller.Visibility.Refresh();
            controller.SelectBasicAttack();
            Require(controller.IsUnitVisible(presentationEnemy) && controller.CanAttack(presentationEnemy) &&
                targets.Contains(presentationEnemy.Position), "Clear opening enables the same preview and commit permission");
            UnityEngine.Object.Destroy(controller.gameObject);
            Debug.Log("WP-25 Play Mode checks passed: wall targeting overlays, basic/aimed rejection, scout death, hidden sprite and opening.");
            return true;
        }

        private static void CheckGeometry()
        {
            var grid = new GridModel(6, 6);
            var origin = new HexCoordinates(0, 0);
            Require(HexLineOfSight.IsClear(grid, origin, origin), "Own hex is visible");
            Require(!HexLineOfSight.IsClear(grid, origin, new HexCoordinates(-1, 0)), "Invalid endpoint");
            grid.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.Blocked;
            Require(!HexLineOfSight.IsClear(grid, origin, new HexCoordinates(2, 0)), "Wall between centers");
            Require(!HexLineOfSight.IsClear(grid, origin, new HexCoordinates(2, 2)), "Touching wall edge blocks");
            Require(HexLineOfSight.IsClear(grid, origin, new HexCoordinates(1, 0), true) &&
                !HexLineOfSight.IsClear(grid, origin, new HexCoordinates(1, 0)), "Wall face visible but not a shot target");
            grid.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.Ground;
            grid.GetCell(new HexCoordinates(1, 1)).Terrain = TerrainType.Blocked;
            Require(!HexLineOfSight.IsClear(grid, origin, new HexCoordinates(2, 5)), "Touching single wall vertex blocks");
            Require(HexLineOfSight.IsClear(grid, origin, new HexCoordinates(1, 5)), "Ray missing the corner stays clear");
            foreach (var terrain in new[] { TerrainType.Ground, TerrainType.HighGround, TerrainType.Pit })
            {
                grid.GetCell(new HexCoordinates(1, 1)).Terrain = terrain;
                Require(HexLineOfSight.IsClear(grid, origin, new HexCoordinates(2, 2)), "Non-wall terrain is transparent");
            }
            // Every wall position and pair of free endpoints on a small board, including borders.
            var small = new GridModel(4, 4);
            foreach (var wall in small.Cells)
            {
                wall.Terrain = TerrainType.Blocked;
                foreach (var a in small.Cells.Where(cell => cell != wall))
                    foreach (var b in small.Cells.Where(cell => cell != wall))
                    {
                        bool clear = HexLineOfSight.IsClear(small, a.Coordinates, b.Coordinates);
                        Require(clear == HexLineOfSight.IsClear(small, b.Coordinates, a.Coordinates), "Reversal symmetry");
                        Require(clear != ReferenceWallIntersection(a.Coordinates, b.Coordinates, wall.Coordinates),
                            "Exact clipping agrees with independent polygon-edge intersection");
                        if (a.Coordinates.DistanceTo(b.Coordinates) <= 1)
                            Require(clear, "Adjacent free centers have no intervening wall");
                    }
                wall.Terrain = TerrainType.Ground;
            }
        }

        private static void CheckFogAndEnemyVision()
        {
            var grid = new GridModel(7, 5);
            WallColumn(grid, 3);
            var hero = Spawn(grid, "observer", new HexCoordinates(1, 2));
            var enemy = Spawn(grid, "enemy", new HexCoordinates(5, 2), UnitTeam.Enemy);
            using var fog = new FogOfWarSystem(grid, new[] { hero });
            Require(fog.IsVisible(new HexCoordinates(3, 2)) && !fog.IsVisible(enemy.Position), "Visible wall hides far side");
            var turns = new TurnManager(grid, new[] { enemy, hero }, fog);
            turns.TryStartNextTurn();
            Require(!turns.CanSee(enemy, hero.Position) &&
                MeleeBrain.ChooseDestination(grid, enemy, new[] { hero }, 4, turns) == enemy.Position,
                "AI cannot see or pursue through a wall");
            grid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.Ground;
            fog.Refresh();
            Require(fog.IsVisible(enemy.Position) && turns.CanSee(enemy, hero.Position), "Same opening reveals both directions");
            grid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.Blocked;
            fog.Refresh();
            Require(fog.GetState(enemy.Position) == CellVisibility.Explored, "Occlusion preserves explored memory");
            var scout = Spawn(grid, "scout", new HexCoordinates(4, 2));
            fog.Register(scout);
            Require(fog.IsVisible(enemy.Position), "New ally reveals far side");
            scout.ApplyDamage(scout.CurrentHealth);
            Require(!fog.IsVisible(enemy.Position), "Dead scout removes far-side vision");
            Require(hero.TryRelocate(grid, new HexCoordinates(4, 1)) && fog.IsVisible(enemy.Position), "Movement refreshes blocked vision");
            hero.ApplyDamage(hero.CurrentHealth);
            Require(grid.Cells.All(cell => !fog.IsVisible(cell.Coordinates)), "Last death removes all vision");
        }

        private static void CheckAttacksAndBlink()
        {
            var grid = new GridModel(7, 5);
            WallColumn(grid, 3);
            var actor = Spawn(grid, "shooter", new HexCoordinates(1, 2));
            var scout = Spawn(grid, "scout", new HexCoordinates(4, 1));
            var enemy = Spawn(grid, "target", new HexCoordinates(4, 2), UnitTeam.Enemy);
            var units = new[] { actor, scout, enemy };
            using var fog = new FogOfWarSystem(grid, units);
            var turns = new TurnManager(grid, units, fog);
            turns.TryStartNextTurn();
            var dice = new CountingDice();
            var combat = new CombatSystem(grid, turns, dice);
            var abilities = new AbilitySystem(grid, turns, dice, units);
            Require(fog.IsVisible(enemy.Position) && !combat.CanAttack(actor, enemy, out var reason) &&
                reason == "A wall blocks the line to that hex." && !combat.TryAttack(actor, enemy, out _),
                "Ally sight does not permit shooting through a wall");
            foreach (var ability in actor.Definition.Abilities.Where(a => a.Effect != AbilityEffect.Blink))
            {
                var target = ability.Effect == AbilityEffect.Trap ? new HexCoordinates(4, 0) : enemy.Position;
                Require(!abilities.CanUse(actor, ability, target, out reason) &&
                    reason == "A wall blocks the line to that hex." &&
                    !abilities.TryUse(actor, ability, target, out _, out _), "Preview and commit reject blocked ability");
            }
            Require(dice.Calls == 0 && turns.ActionAvailable && enemy.CurrentHealth == enemy.Definition.MaxHealth,
                "Rejected attacks spend no dice/action/health");
            var blink = actor.Definition.Abilities.First(a => a.Effect == AbilityEffect.Blink);
            var landing = new HexCoordinates(4, 2);
            Require(!abilities.CanUse(actor, blink, landing, out _), "Blink cannot land on occupied hex");
            landing = new HexCoordinates(4, 0);
            Require(actor.Position.DistanceTo(landing) == 3 && abilities.CanUse(actor, blink, landing, out _),
                "Party-visible free landing permits wall crossing");
            scout.ApplyDamage(scout.CurrentHealth);
            Require(!abilities.TryUse(actor, blink, landing, out _, out _) && turns.ActionAvailable,
                "Explored-only landing spends no action");
            var replacement = Spawn(grid, "replacement", new HexCoordinates(4, 1));
            fog.Register(replacement);
            Require(abilities.TryUse(actor, blink, landing, out _, out _) && actor.Position == landing &&
                turns.RemainingMovement == actor.Definition.Movement, "Blink crosses wall and retains movement");
            turns.TryCompleteAction(actor);
            Require(turns.TryEndTurn(actor), "Blink vision refresh does not strand turn");

            var debugTurns = new TurnManager(grid, new[] { replacement, enemy });
            debugTurns.TryStartNextTurn();
            // Fog-disabled model fixtures still enforce shot geometry.
            var farEnemy = Spawn(grid, "far", new HexCoordinates(1, 1), UnitTeam.Enemy);
            Require(!new CombatSystem(grid, debugTurns, dice).CanAttack(replacement, farEnemy), "Debug vision does not bypass wall shots");
        }

        private static void CheckBurst()
        {
            var dice = new CountingDice();
            var blast = new AbilityDefinition("wall-test-blast", "Burst", "Wall test", AbilityEffect.FireBurst, 4, radius: 2);
            // Use an owned radius-two test blast so an intervening wall can fit between center and victim.
            var testGrid = new GridModel(7, 5);
            testGrid.GetCell(new HexCoordinates(3, 2)).Terrain = TerrainType.Blocked;
            var caster = Spawn(testGrid, "caster", new HexCoordinates(1, 2), abilities: new[] { blast });
            var near = Spawn(testGrid, "near", new HexCoordinates(2, 3), UnitTeam.Enemy);
            var behind = Spawn(testGrid, "behind", new HexCoordinates(4, 2), UnitTeam.Enemy);
            var spotter = Spawn(testGrid, "spotter", new HexCoordinates(5, 2));
            using var testFog = new FogOfWarSystem(testGrid, new[] { caster, near, behind, spotter });
            var testTurns = new TurnManager(testGrid, new[] { caster, near, behind, spotter }, testFog);
            testTurns.TryStartNextTurn();
            var system = new AbilitySystem(testGrid, testTurns, dice, testTurns.Order);
            Require(testFog.IsVisible(behind.Position) && system.TryUse(caster, blast, new HexCoordinates(2, 2), out var result, out _) &&
                result.Attacks.Count == 1 && behind.CurrentHealth == behind.Definition.MaxHealth &&
                near.CurrentHealth < near.Definition.MaxHealth && spotter.CurrentHealth == spotter.Definition.MaxHealth,
                "Burst does not pass through wall or hurt allies, even with shared sight");
            Require(!system.TryUse(caster, blast, new HexCoordinates(2, 2), out _, out _) && dice.Calls == 2,
                "Burst cannot resolve twice");
        }

        private static UnitRuntimeState Spawn(GridModel grid, string id, HexCoordinates position,
            UnitTeam team = UnitTeam.Player, AbilityDefinition[] abilities = null)
        {
            var definition = new UnitDefinition(id, id, 4, maxHealth: 40, attackRange: 6, visionRange: 8,
                abilities: abilities ?? HeroDefinitions.Defaults[2].Abilities.Concat(HeroDefinitions.Defaults[3].Abilities).ToArray());
            Require(UnitRuntimeState.TrySpawn(grid, id, definition, position, out var unit, team), "Spawn " + id);
            return unit;
        }

        private static void WallColumn(GridModel grid, int q)
        { foreach (var cell in grid.Cells.Where(cell => cell.Coordinates.Q == q)) cell.Terrain = TerrainType.Blocked; }

        // Independent oracle: intersect the ray with six polygon edges in axial coordinates scaled by 3.
        private static bool ReferenceWallIntersection(HexCoordinates a, HexCoordinates b, HexCoordinates wall)
        {
            var vertices = new[] { (1, 1), (2, -1), (1, -2), (-1, -1), (-2, 1), (-1, 2) };
            var start = (3 * a.Q, 3 * a.R);
            var end = (3 * b.Q, 3 * b.R);
            for (int i = 0; i < vertices.Length; i++)
            {
                var first = vertices[i];
                var next = vertices[(i + 1) % vertices.Length];
                var c = (3 * wall.Q + first.Item1, 3 * wall.R + first.Item2);
                var d = (3 * wall.Q + next.Item1, 3 * wall.R + next.Item2);
                if (Math.Max(Math.Min(start.Item1, end.Item1), Math.Min(c.Item1, d.Item1)) <=
                    Math.Min(Math.Max(start.Item1, end.Item1), Math.Max(c.Item1, d.Item1)) &&
                    Math.Max(Math.Min(start.Item2, end.Item2), Math.Min(c.Item2, d.Item2)) <=
                    Math.Min(Math.Max(start.Item2, end.Item2), Math.Max(c.Item2, d.Item2)) &&
                    Orientation(start, end, c) * Orientation(start, end, d) <= 0 &&
                    Orientation(c, d, start) * Orientation(c, d, end) <= 0) return true;
            }
            return false;
        }

        private static long Orientation((int, int) a, (int, int) b, (int, int) c) =>
            ((long)b.Item1 - a.Item1) * (c.Item2 - a.Item2) - ((long)b.Item2 - a.Item2) * (c.Item1 - a.Item1);

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-25: " + message); }
    }
}
