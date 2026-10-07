using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class BossAttackChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Boss Telegraph")]
        public static void Run()
        {
            var grid = new GridModel(9, 7);
            UnitRuntimeState.TrySpawn(grid, "boss", EnemyDefinitions.MiniBoss.Unit, new HexCoordinates(4, 3), out var boss, UnitTeam.Enemy);
            UnitRuntimeState.TrySpawn(grid, "fast", new UnitDefinition("fast", "Fast", 4, 5), new HexCoordinates(2, 3), out var fast);
            UnitRuntimeState.TrySpawn(grid, "slow", new UnitDefinition("slow", "Slow", 4, -2), new HexCoordinates(2, 4), out var slow);
            using var fog = new FogOfWarSystem(grid, new[] { fast, slow });
            var turns = new TurnManager(grid, new[] { fast, boss, slow }, fog);
            using var system = new BossAttackSystem(turns, boss, new[] { fast, slow });
            turns.TryStartNextTurn(); Require(!system.TryArm(), "Only boss can arm");
            turns.TryEndTurn(fast); turns.TryStartNextTurn();
            Require(system.TryArm() && system.Pending && system.Zone.Count == 7 && !system.TryResolve(out _), "Arm action, fixed seven-cell area, no immediate strike");
            var warned = system.Zone.ToArray();
            turns.TryEndTurn(boss);
            var opportunities = new HashSet<string>();
            while (turns.TryStartNextTurn() && turns.ActiveUnit != boss)
            {
                opportunities.Add(turns.ActiveUnit.InstanceId);
                if (turns.ActiveUnit == fast) Require(fast.TryRelocate(grid, new HexCoordinates(0, 0)), "Escape fixed zone");
                turns.TryEndTurn(turns.ActiveUnit);
            }
            Require(opportunities.SetEquals(new[] { "fast", "slow" }) && warned.SequenceEqual(system.Zone), "All initiative positions get a turn; zone does not follow target");
            Require(system.TryResolve(out int hits) && hits == 1 && slow.CurrentHealth == 10 && fast.CurrentHealth == 20 &&
                !system.Pending && !system.TryResolve(out _) && !turns.ActionAvailable, "Exact zone, one-time damage and spent action");
            turns.TryEndTurn(boss); turns.TryStartNextTurn(); turns.TryEndTurn(slow); turns.TryStartNextTurn();
            turns.TryEndTurn(fast); turns.TryStartNextTurn();
            Require(system.TryArm(), "Arm again on later turn"); boss.ApplyDamage(boss.CurrentHealth);
            Require(!system.Pending && !system.TryResolve(out _), "Boss death cancels immediately");
            var hiddenGrid = new GridModel();
            UnitRuntimeState.TrySpawn(hiddenGrid, "boss", EnemyDefinitions.MiniBoss.Unit, new HexCoordinates(3, 2), out var hiddenBoss, UnitTeam.Enemy);
            UnitRuntimeState.TrySpawn(hiddenGrid, "hero", new UnitDefinition("hero", "Hero", 3, -1, visionRange: 1), new HexCoordinates(1, 2), out var hero);
            using var hiddenFog = new FogOfWarSystem(hiddenGrid, new[] { hero });
            var hiddenTurns = new TurnManager(hiddenGrid, new[] { hiddenBoss, hero }, hiddenFog); hiddenTurns.TryStartNextTurn();
            using var hiddenSystem = new BossAttackSystem(hiddenTurns, hiddenBoss, new[] { hero });
            Require(!hiddenSystem.TryArm() && !hiddenSystem.Pending && hiddenTurns.ActionAvailable, "Hidden boss cannot leak a warning");
            hero.TryRelocate(hiddenGrid, new HexCoordinates(2, 2));
            Require(hiddenSystem.TryArm(), "Visible boss can warn"); hiddenSystem.Cancel();
            Require(!hiddenSystem.TryResolve(out _) && hero.CurrentHealth == 20, "Cancellation cannot deal damage");
            var visibilityRoot = new GameObject("Boss warning visibility checks");
            try
            {
                var view = visibilityRoot.AddComponent<HexGridView>(); view.Initialize(hiddenGrid, new HexLayout()); view.SetFog(hiddenFog);
                var coordinate = hero.Position; view.SetDangerCells(new[] { coordinate });
                var tile = visibilityRoot.transform.Find("Hex " + coordinate).GetComponent<SpriteRenderer>();
                Require(tile.color.r > 0.9f && tile.color.g < 0.3f, "Visible warning rendered");
                hero.TryRelocate(hiddenGrid, new HexCoordinates(0, 0)); view.RefreshAll();
                Require(tile.color.r < 0.9f, "Explored cells hide warning");
                view.ToggleVisibilityDebug(); Require(tile.color.r < 0.9f, "Debug vision does not leak warning");
            }
            finally { UnityEngine.Object.DestroyImmediate(visibilityRoot); }
            var previous = Localization.Language;
            Localization.SetLanguage(InterfaceLanguage.Russian, false);
            Require(Localization.CombatMessage("Cinder burst\n2 x -10 HP").StartsWith("Вспышка углей") &&
                Localization.CombatMessage("Cinder burst armed: leave red hexes before the next boss turn.").StartsWith("Вспышка углей готовится"), "Localized warnings and results");
            Localization.SetLanguage(previous, false);
            Debug.Log("WP-27 passed: initiative reaction window, fixed zone, one-time damage, action cost, death/cancellation and hidden warnings.");
        }
        private static GameObject root;
        private static PlayerUnitController controller;
        private static bool observedWarning, escaped;
        private static bool strikeVerified, ending;
        private static readonly HashSet<string> reactions = new HashSet<string>();
        private static double deadline;
        internal static void BeginPresentation(PlayerUnitController scene)
        {
            var grid = new GridModel(); var layout = new HexLayout();
            var camera = scene.GetComponent<HexGridInteraction>().GridCamera;
            root = new GameObject("WP27 Boss Checks"); var view = root.AddComponent<HexGridView>(); view.Initialize(grid, layout);
            var interaction = root.AddComponent<HexGridInteraction>(); interaction.Initialize(grid, layout, view, camera);
            var feedback = root.AddComponent<CombatText>(); feedback.Initialize(camera, 27);
            controller = root.AddComponent<PlayerUnitController>();
            controller.Initialize(grid, layout, view, interaction, 0, feedback: feedback, enableFog: true,
                playerSpawns: new[] { new HexCoordinates(3, 3), new HexCoordinates(4, 3), new HexCoordinates(3, 4), new HexCoordinates(4, 4) },
                encounter: new[] { new EnemyPlacement(EnemyDefinitions.MiniBoss, new HexCoordinates(5, 3)) });
            deadline = EditorApplication.timeSinceStartup + 20;
        }
        internal static bool PollPresentation()
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Boss loop must finish");
            var system = controller.BossAttack;
            if (ending)
            {
                if (system.Pending) return false;
                Require(controller.Outcome == BattleOutcome.Defeat, "Combat end cancels the armed strike");
                UnityEngine.Object.Destroy(root);
                Debug.Log("WP-27 Play Mode passed: red warning cells, four reaction turns, escape, exact strike and cancellation on defeat.");
                return true;
            }
            if (strikeVerified && system.Pending)
            {
                foreach (var hero in controller.Units) hero.ApplyDamage(hero.CurrentHealth);
                ending = true; return false;
            }
            if (system.Pending && !observedWarning)
            {
                observedWarning = true;
                foreach (var coordinate in system.Zone.Where(controller.Visibility.IsVisible))
                {
                    var tile = root.transform.Find("Hex " + coordinate);
                    // Grid tiles are children of the view object, which is this fixture root.
                    Require(tile != null && tile.GetComponent<SpriteRenderer>().color.r > 0.9f &&
                        tile.GetComponent<SpriteRenderer>().color.g < 0.3f, "Threatened visible hexes rendered red");
                }
            }
            int damage = controller.Units.Sum(u => u.Definition.MaxHealth - u.CurrentHealth);
            if (observedWarning && !strikeVerified && !system.Pending && damage > 0)
            {
                Require(escaped && reactions.Count == 4 && damage == 20, "Four hero reaction turns, escape and exactly two hits");
                strikeVerified = true;
            }
            if (controller.CanPlayerAct)
            {
                if (system.Pending)
                {
                    reactions.Add(controller.SelectedUnit.InstanceId);
                    if (!escaped && controller.SelectedUnit == controller.Units[0])
                    {
                        var escape = controller.CurrentRange.Costs.Keys.First(c => !system.Zone.Contains(c) && c != controller.SelectedUnit.Position);
                        Require(controller.TryMoveSelected(escape), "Escape telegraph using real movement"); escaped = true; return false;
                    }
                }
                Require(controller.TryEndTurn(), "Pass hero turn");
            }
            return false;
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-27: " + message); }
    }
}
