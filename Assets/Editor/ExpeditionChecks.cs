using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class ExpeditionChecks
    {
        private sealed class Fixture : IDisposable
        {
            public DungeonMap Map { get; }
            public List<UnitRuntimeState> Party { get; } = new List<UnitRuntimeState>();
            public List<UnitRuntimeState> Enemies { get; } = new List<UnitRuntimeState>();
            public TurnManager Turns { get; }
            public ExpeditionRun Run { get; }
            public UnitRuntimeState Actor => Party[0];
            public Fixture(int seed, bool fog = false)
            {
                Map = DungeonGenerator.Generate(seed);
                for (int i = 0; i < 4; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, "hero" + i, HeroDefinitions.Defaults[i],
                        Map.PlayerSpawns[i], out var hero), "Spawn party");
                    Party.Add(hero);
                }
                foreach (var placement in EncounterGenerator.Generate(Map))
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, "enemy" + Enemies.Count, placement.Archetype.Unit,
                        placement.Position, out var enemy, UnitTeam.Enemy), "Spawn encounter");
                    Enemies.Add(enemy);
                }
                Turns = new TurnManager(Map.Grid, Party.Concat(Enemies), fog ? new FogOfWarSystem(Map.Grid, Party) : null);
                Run = new ExpeditionRun(Map, Turns);
                Turns.TryStartNextTurn();
                NextActor();
            }
            public void NextActor()
            {
                for (int i = 0; i <= Turns.Order.Count; i++)
                {
                    if (Turns.ActiveUnit == Actor && Turns.CanSelectAction(Actor)) return;
                    Turns.TryEndTurn(Turns.ActiveUnit);
                    Turns.TryStartNextTurn();
                }
                throw new InvalidOperationException("Actor not available.");
            }
            public void WalkTo(HexCoordinates destination)
            {
                for (int step = 0; step < 300 && Actor.Position != destination; step++)
                {
                    if (Turns.RemainingMovement == 0)
                    { Turns.TryEndTurn(Actor); Turns.TryStartNextTurn(); NextActor(); }
                    var path = HexPathfinder.FindPath(Map.Grid, Actor.Position, destination);
                    Require(path.Count > 1, "Reachable objective after occupancy");
                    Require(Turns.TryBeginMovement(Actor, path[1], out _), "Step toward objective");
                    Require(!Run.TryOpenChest(Actor) && !Run.TryExtract(Actor), "No interaction during movement");
                    Require(Turns.TryCompleteMovement(Actor), "Movement complete");
                }
                Require(Actor.Position == destination, "Finite travel");
            }
            public void Dispose() => Turns.Vision?.Dispose();
        }

        [MenuItem("Tools/Guild Tactics/Validate Expedition")]
        public static void Run()
        {
            var rewards = new HashSet<string>();
            for (int seed = 0; seed < 50; seed++)
            {
                using (var fixture = new Fixture(seed))
                {
                    var run = fixture.Run;
                    Require(!run.TryOpenChest(fixture.Actor) && fixture.Turns.ActionAvailable, "Distant chest spends nothing");
                    Require(!run.TryExtract(fixture.Actor), "No extraction before chest and combat");
                    foreach (var enemy in fixture.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                    fixture.WalkTo(run.Chest);
                    Require(!run.TryOpenChest(fixture.Party[1]), "Wrong actor rejected");
                    Require(fixture.Turns.TryBeginAction(fixture.Actor), "Begin action lock");
                    Require(!run.TryOpenChest(fixture.Actor), "Resolving action lock");
                    fixture.Turns.TryCompleteAction(fixture.Actor);
                    Require(!run.TryOpenChest(fixture.Actor), "Spent action rejected");
                    fixture.Turns.TryEndTurn(fixture.Actor); fixture.Turns.TryStartNextTurn(); fixture.NextActor();
                    Require(run.TryOpenChest(fixture.Actor) && !fixture.Turns.ActionAvailable, "Chest spends one action");
                    string reward = run.CollectedGold + ":" + string.Join(",", run.CollectedItems.Select(i => i.Id));
                    rewards.Add(reward);
                    Require(run.CollectedGold >= 20 && run.CollectedGold <= 60 && run.CollectedItems.Count == 2,
                        "Gold and items bounds");
                    Require(!run.TryOpenChest(fixture.Actor), "No duplicate reward");
                    fixture.Actor.ApplyDamage(3);
                    fixture.WalkTo(run.Extraction);
                    Require(run.TryExtract(fixture.Actor), "Post-combat return and extraction");
                    var result = run.Result;
                    Require(result.Outcome == ExpeditionOutcome.Extracted && result.Gold == run.CollectedGold &&
                        result.Adventurers.Count == 4 && result.Adventurers.First(a => a.InstanceId == fixture.Actor.InstanceId).Health == 17,
                        "Snapshot preserves rewards and health");
                    Require(!run.TryExtract(fixture.Actor) && !run.TryOpenChest(fixture.Actor), "Finished run is immutable");
                    fixture.Actor.ApplyDamage(17); run.RefreshOutcome();
                    Require(ReferenceEquals(result, run.Result) && result.Adventurers.First(a => a.InstanceId == fixture.Actor.InstanceId).Health == 17,
                        "Result does not retain mutable unit state");
                    using (var replay = new Fixture(seed))
                    {
                        Require(replay.Actor.TryRelocate(replay.Map.Grid, replay.Run.Chest), "Replay chest placement");
                        Require(replay.Run.TryOpenChest(replay.Actor), "Chest usable before victory");
                        Require(reward == replay.Run.CollectedGold + ":" + string.Join(",", replay.Run.CollectedItems.Select(i => i.Id)),
                            "Loot independent of battle and opening time");
                        replay.Actor.TryRelocate(replay.Map.Grid, replay.Run.Extraction);
                        Require(!replay.Run.TryExtract(replay.Actor), "Living enemies prevent extraction");
                        foreach (var hero in replay.Party) hero.ApplyDamage(hero.CurrentHealth);
                        replay.Run.RefreshOutcome();
                        Require(replay.Run.Result.Outcome == ExpeditionOutcome.Defeated && replay.Run.Result.Gold == 0 &&
                            replay.Run.Result.Items.Count == 0 && replay.Run.Result.Adventurers.All(a => !a.Survived),
                            "Defeat loses collected rewards and records deaths");
                    }
                }
            }
            Require(rewards.Count > 10, "Loot varies across seeds");
            using (var fog = new Fixture(7, true))
            {
                Require(!fog.Turns.Vision.IsVisible(fog.Run.Chest) && !fog.Run.TryOpenChest(fog.Actor), "Unknown chest hidden");
                var adjacent = fog.Map.Grid.GetNeighbors(fog.Run.Chest).First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied);
                Require(fog.Actor.TryRelocate(fog.Map.Grid, adjacent.Coordinates) && fog.Run.TryOpenChest(fog.Actor),
                    "Adjacent visible chest interaction");
                Require(!fog.Run.TryOpenChest(fog.Enemies[0]), "Enemy cannot collect loot");
            }
            Require(ItemDefinitions.All.Select(i => i.Category).Distinct().Count() == 3 &&
                ItemDefinitions.All.Select(i => i.Id).Distinct().Count() == 3, "Three unique item categories");
            Debug.Log("WP-13 passed: 50 expedition round trips, seeded rewards, action locks, duplicate prevention, fog, defeat and snapshots.");
        }

        private static PlayerUnitController presentation;
        private static DungeonMap presentationMap;
        private static int stage;
        public static void BeginPresentation(Camera camera)
        {
            presentationMap = DungeonGenerator.Generate(13);
            var root = new GameObject("WP13 Expedition Checks");
            var layout = new HexLayout();
            var view = root.AddComponent<HexGridView>(); view.Initialize(presentationMap.Grid, layout);
            var input = root.AddComponent<HexGridInteraction>(); input.Initialize(presentationMap.Grid, layout, view, camera);
            presentation = root.AddComponent<PlayerUnitController>();
            presentation.Initialize(presentationMap.Grid, layout, view, input, 0.02f, enableFog: true,
                playerSpawns: presentationMap.PlayerSpawns, encounter: EncounterGenerator.Generate(presentationMap),
                expeditionMap: presentationMap);
            root.AddComponent<ExpeditionUI>().Initialize(presentation, layout, camera);
            foreach (var enemy in presentation.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            stage = 0;
        }
        public static bool PollPresentation()
        {
            if (stage == 0)
            {
                if (presentation.Outcome != BattleOutcome.Victory || !presentation.CanPlayerAct) return false;
                var cell = presentation.CurrentRange.Costs.Keys.First(p => p != presentation.SelectedUnit.Position);
                Require(presentation.TryMoveSelected(cell), "Controller permits post-combat movement");
                Require(!presentation.TryOpenChest() && !presentation.TryExtract(), "Animation blocks interaction");
                stage = 1; return false;
            }
            if (presentation.IsMoving) return false;
            var actor = presentation.SelectedUnit;
            foreach (var hero in presentation.Units)
                if (hero != actor && hero.Position == presentation.Expedition.Extraction) hero.ApplyDamage(hero.CurrentHealth);
            Require(actor.TryRelocate(presentationMap.Grid, presentation.Expedition.Chest), "Move to chest fixture");
            presentation.CancelTargeting();
            Require(presentation.TryOpenChest() && !presentation.TryOpenChest(), "Controller single reward");
            Require(actor.TryRelocate(presentationMap.Grid, presentation.Expedition.Extraction), "Return fixture");
            presentation.CancelTargeting();
            Require(presentation.TryExtract() && presentation.Expedition.Result != null, "Controller result flow");
            Require(!presentation.CanPlayerAct && !presentation.TryEndTurn() && !presentation.TryExtract() &&
                !presentation.TryMoveSelected(presentation.Expedition.Chest), "Result blocks commands");
            UnityEngine.Object.DestroyImmediate(presentation.gameObject);
            Debug.Log("WP-13 Play Mode checks passed: post-combat movement, locks, chest and extraction result.");
            return true;
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-13: " + message); }
    }
}
