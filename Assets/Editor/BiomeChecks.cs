using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class BiomeChecks
    {
        private sealed class Section
        {
            public DungeonMap Map { get; }
            public ExpeditionRun Run { get; }
            public TurnManager Turns { get; }
            public List<UnitRuntimeState> Heroes { get; } = new List<UnitRuntimeState>();
            private readonly List<UnitRuntimeState> enemies = new List<UnitRuntimeState>();
            public Section(ExpeditionJourney journey, GuildState guild, IReadOnlyList<GuildAdventurer> party)
            {
                Map = DungeonGenerator.Generate(journey.NextSectionSeed, journey.DungeonConfig);
                foreach (var hero in party)
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, hero.Id, hero.Definition, Map.PlayerSpawns[Heroes.Count], out var unit), "Spawn hero");
                    unit.SetLoadout(hero.Weapon, hero.Armor, hero.HealingPotions);
                    unit.SetTraining(hero.TrainingAttack, hero.TrainingDefense);
                    unit.ApplyDamage(unit.Definition.MaxHealth - hero.Health);
                    Heroes.Add(unit);
                }
                foreach (var placement in EncounterGenerator.Generate(Map, journey.EncounterConfig))
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, "enemy-" + enemies.Count, placement.Archetype.Unit,
                        placement.Position, out var unit, UnitTeam.Enemy), "Spawn actual encounter");
                    enemies.Add(unit);
                }
                Turns = new TurnManager(Map.Grid, Heroes.Concat(enemies));
                Run = new ExpeditionRun(Map, Turns, ExpeditionSelection.Offers[journey.OfferIndex].Mission, journey.ResultComposer(guild));
                Turns.TryStartNextTurn();
                while (Turns.ActiveUnit.Team != UnitTeam.Player)
                { Turns.TryEndTurn(Turns.ActiveUnit); Turns.TryStartNextTurn(); }
            }
            public void Complete()
            {
                foreach (var enemy in enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                RetreatChecks.ReachExit(Turns, Run);
                Require(Run.MissionCompleted && Run.TryExtract(Turns.ActiveUnit, true), "Clear cellar and extract without relic");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Biomes")]
        public static void Run()
        {
            var cellar = ExpeditionSelection.Offers[3];
            var layouts = new HashSet<string>();
            var archetypes = new HashSet<string>();
            var eventTypes = new HashSet<string>();
            for (int seed = -10; seed < 40; seed++)
            {
                var config = cellar.CreateDungeonConfig();
                var map = DungeonGenerator.Generate(seed, config);
                var replay = DungeonGenerator.Generate(seed, cellar.CreateDungeonConfig());
                var crypt = DungeonGenerator.Generate(seed);
                Require(map.Grid.Width == 12 && map.Grid.Height == 12 && map.Biome == DungeonBiome.FloodedCellar &&
                    DungeonValidator.IsValid(map, config), "Compact connected cellar with reachable objectives");
                string signature = Signature(map);
                layouts.Add(signature);
                Require(signature == Signature(replay) && map.Objective == replay.Objective &&
                    map.EnemyCandidates.SequenceEqual(replay.EnemyCandidates), "Map replay");
                Require(!map.UsedFallback && map.Grid.Cells.Count(c => c.Terrain == TerrainType.Pit) >= 12 &&
                    map.Grid.Cells.Count(c => c.Terrain == TerrainType.Blocked) < crypt.Grid.Cells.Count(c => c.Terrain == TerrainType.Blocked),
                    "Open water basins change movement and wall occlusion");
                Require(signature != Signature(crypt), "Biome changes tactical layout for same seed");
                var encounter = EncounterGenerator.Generate(map, cellar.CreateEncounterConfig());
                var repeatedEncounter = EncounterGenerator.Generate(replay, cellar.CreateEncounterConfig());
                Require(encounter.Count >= 3 && encounter.Count <= 4 && encounter.Sum(e => e.Archetype.Cost) <= 8 &&
                    encounter.Select(e => e.Position).Distinct().Count() == encounter.Count, "Cellar encounter count, budget and unique placement");
                Require(encounter.Select(e => e.Archetype.Unit.Id + e.Position).SequenceEqual(
                    repeatedEncounter.Select(e => e.Archetype.Unit.Id + e.Position)), "Encounter replay");
                foreach (var enemy in encounter)
                {
                    archetypes.Add(enemy.Archetype.Unit.Id);
                    Require(!enemy.Archetype.IsMiniBoss && (enemy.Archetype.Unit.Id == "ash-crawler" ||
                        enemy.Archetype.Unit.Id == "crypt-bowman" || enemy.Archetype.Unit.Id == "hollow-brute") &&
                        TerrainRules.CanWalk(map.Grid.GetCell(enemy.Position).Terrain) && enemy.Position != map.Objective &&
                        map.PlayerSpawns.All(p => p.DistanceTo(enemy.Position) >= 4), "Distinct pool and legal placements");
                }
                for (int section = 1; section <= cellar.Sections; section++)
                {
                    var definition = ExplorationEvents.At(seed, section, DungeonBiome.FloodedCellar);
                    eventTypes.Add(definition.Id);
                    Require(definition.Id != "altar" && definition == ExplorationEvents.At(seed, section, DungeonBiome.FloodedCellar), "Cellar events fit biome and replay");
                }
                ValidateJourney(seed);
            }
            Require(layouts.Count >= 20 && archetypes.Count == 3 && eventTypes.SetEquals(new[] { "cache", "spring", "snare" }), "Map, encounter and event variety");
            var forced = cellar.CreateDungeonConfig(); forced.MinimumWalkableCells = 100; forced.MaxAttempts = 1;
            var fallback = DungeonGenerator.Generate(1, forced);
            Require(fallback.UsedFallback && fallback.Biome == DungeonBiome.FloodedCellar && DungeonValidator.IsValid(fallback, forced), "Fallback preserves biome and connectivity");
            Reject(() => DungeonGenerator.Generate(1, new DungeonGenerationConfig { Biome = (DungeonBiome)99 }));
            Reject(() => EncounterGenerator.Generate(fallback, new EncounterConfig()));
            ValidateTerrain();
            Render(DungeonBiome.Crypt, "Logs/wp32-crypt.png");
            Render(DungeonBiome.FloodedCellar, "Logs/wp32-cellar.png");
            Debug.Log("WP-32 model checks passed: 50 seeds, distinct connected 12x12 layouts, water rules, encounters/events/reward profiles, two-section journeys, pending/resolved load, rollback, guild payment and legacy v6.");
        }

        private static void ValidateJourney(int seed)
        {
            string directory = Path.Combine(Path.GetTempPath(), "BiomeChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var guild = new GuildState();
                var offers = new ExpeditionSelection(seed, 3, 0);
                var data = GuildSaveData.Capture(guild, offers);
                data.items = new[] { ItemDefinitions.HealingDraught.Id };
                guild = GuildState.Restore(data);
                guild.TryTransferPotion("warrior-1", true);
                var departure = GuildSaveData.Capture(guild, offers);
                var journey = new ExpeditionJourney(offers.NextSeed, 3, 31, offers.Selected.CreateDungeonConfig(), offers.Selected.CreateEncounterConfig());
                var first = new Section(journey, guild, guild.BeginExpedition());
                guild.AttachRun(first.Run);
                first.Heroes[0].ApplyDamage(2);
                first.Complete(); journey.Complete(first.Run.Result); offers.RecordLaunch();
                var store = new GuildSaveStore(Path.Combine(directory, "save.json"));
                Require(store.TrySaveCheckpoint(departure, offers, journey.Capture(), out _), "Save pending cellar boundary");
                Require(store.TryLoad(out var restored, out var loadedOffers, out _), "Load pending cellar boundary");
                journey = ExpeditionJourney.Restore(store.LoadedCheckpoint, restored);
                Require(journey.EventPending && journey.DungeonConfig.Biome == DungeonBiome.FloodedCellar &&
                    journey.EncounterConfig.Biome == DungeonBiome.FloodedCellar && loadedOffers.SelectedIndex == 3, "Loading preserves all biome profiles");
                var eventHero = journey.Capture().party.First(h => h.health > 0).id;
                Require(journey.TryResolveEvent(restored, seed % 2 == 0 ? 0 : 1, eventHero) &&
                    store.TrySaveCheckpoint(departure, loadedOffers, journey.Capture(), out _), "Resolve and persist biome event");
                int previousGold = journey.CarriedGold;
                var savedHealth = journey.Capture().party.Select(h => h.health).ToArray();
                restored.BeginExpedition();
                var next = new Section(journey, restored, journey.ContinuingParty(restored));
                restored.AttachContinuingRun(next.Run);
                Require(next.Heroes.Select(h => h.CurrentHealth).SequenceEqual(savedHealth) &&
                    next.Heroes.First(h => h.InstanceId == "warrior-1").HealingPotions == 1, "State crosses biome sections without healing/resupply");
                next.Heroes[0].ApplyDamage(2);
                Require(store.TryLoad(out var rollback, out _, out _) && !ExpeditionJourney.Restore(store.LoadedCheckpoint, rollback).EventPending &&
                    store.LoadedCheckpoint.party.Select(h => h.health).SequenceEqual(savedHealth), "Unfinished section rollback retains resolved event");
                next.Complete(); journey.Complete(next.Run.Result);
                Require(journey.TryResolveEvent(restored, 0) && journey.Completed == 2 &&
                    journey.CarriedGold >= previousGold + 30 && journey.CarriedGold <= previousGold + 50, "Final cellar reward matches advertised profile");
                Require(store.TrySaveCheckpoint(departure, loadedOffers, journey.Capture(), out _), "Save final boundary");
                Require(store.TryLoad(out var finalGuild, out loadedOffers, out _), "Final boundary reload");
                journey = ExpeditionJourney.Restore(store.LoadedCheckpoint, finalGuild);
                finalGuild.BeginExpedition();
                Require(finalGuild.TryReturn(journey.BoundaryResult(finalGuild)) && !finalGuild.TryReturn(journey.BoundaryResult(finalGuild)) &&
                    finalGuild.Gold == 100 + journey.CarriedGold && finalGuild.Inventory.Count == 4, "Cellar rewards reach guild once");
                Require(store.TrySave(finalGuild, loadedOffers, out _) && store.TryLoad(out finalGuild, out _, out _) &&
                    store.LoadedCheckpoint == null && finalGuild.CanLaunch, "Full guild loop persists and can relaunch");
                var legacy = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection(seed, 1, 0));
                legacy.version = 6;
                File.WriteAllText(store.Path, JsonUtility.ToJson(legacy));
                Require(store.TryLoad(out _, out var legacyOffers, out _) && legacyOffers.SelectedIndex == 1, "Legacy v6 remains crypt");
                var legacyGuild = new GuildState();
                legacyOffers = new ExpeditionSelection(seed, 0, 0);
                legacy = GuildSaveData.Capture(legacyGuild, legacyOffers);
                var legacyJourney = new ExpeditionJourney(legacyOffers.NextSeed, 0, 31,
                    legacyOffers.Selected.CreateDungeonConfig(), legacyOffers.Selected.CreateEncounterConfig());
                var legacySection = new Section(legacyJourney, legacyGuild, legacyGuild.BeginExpedition());
                // Arrange the old relic mission as well as clearing its encounter.
                foreach (var enemy in legacySection.Turns.Order.Where(u => u.Team == UnitTeam.Enemy)) enemy.ApplyDamage(enemy.CurrentHealth);
                if (legacySection.Turns.ActiveUnit.Team != UnitTeam.Player)
                { legacySection.Turns.TryEndTurn(legacySection.Turns.ActiveUnit); legacySection.Turns.TryStartNextTurn(); }
                var actor = legacySection.Turns.ActiveUnit;
                Require(actor.TryRelocate(legacySection.Map.Grid, legacySection.Run.Chest) && legacySection.Run.TryOpenChest(actor), "Legacy relic");
                RetreatChecks.ReachExit(legacySection.Turns, legacySection.Run);
                Require(legacySection.Run.TryExtract(actor, true), "Legacy section complete");
                legacyJourney.Complete(legacySection.Run.Result);
                legacyJourney.TryResolveEvent(legacyGuild, 1, actor.InstanceId);
                legacy.version = 6; legacy.launchedCount = 1; legacy.hasJourney = true; legacy.journey = legacyJourney.Capture();
                string oldJson = JsonUtility.ToJson(legacy).Replace("\"Biome\":0,", "");
                File.WriteAllText(store.Path, oldJson);
                Require(store.TryLoad(out legacyGuild, out legacyOffers, out _) && store.LoadedCheckpoint.dungeon.Biome == DungeonBiome.Crypt &&
                    !ExpeditionJourney.Restore(store.LoadedCheckpoint, legacyGuild).EventPending &&
                    store.LoadedCheckpoint.events[0].roll == legacyJourney.LastEvent.roll, "Actual v6 journey without biome fields keeps resolved event");
                var mismatch = journey.Capture(); mismatch.encounter.Biome = DungeonBiome.Crypt;
                Reject(() => mismatch.Validate(GuildState.Restore(departure)));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void ValidateTerrain()
        {
            var map = DungeonGenerator.Generate(8, ExpeditionSelection.Offers[3].CreateDungeonConfig());
            var water = map.Grid.Cells.First(c => c.Terrain == TerrainType.Pit).Coordinates;
            Require(!TerrainRules.CanWalk(TerrainType.Pit) && TerrainRules.CanPushInto(TerrainType.Pit) &&
                !UnitRuntimeState.TrySpawn(map.Grid, "water", HeroDefinitions.Defaults[0], water, out _), "Deep water is impassable using existing pit rules");
            // Pit push/death is already exercised by TerrainChecks; here assert water leaves sight lines open.
            bool checkedLine = false;
            foreach (var basin in map.Grid.Cells.Where(c => c.Terrain == TerrainType.Pit))
            for (int direction = 0; direction < 3; direction++)
            {
                var from = basin.Coordinates.GetNeighbor(direction);
                var to = basin.Coordinates.GetNeighbor(direction + 3);
                if (!map.Grid.TryGetCell(from, out var first) || !map.Grid.TryGetCell(to, out var second) ||
                    !TerrainRules.CanWalk(first.Terrain) || !TerrainRules.CanWalk(second.Terrain) ||
                    !HexLineOfSight.CanShoot(map.Grid, from, to)) continue;
                basin.Terrain = TerrainType.Blocked;
                Require(!HexLineOfSight.CanShoot(map.Grid, from, to), "Water permits a shot that a wall would block");
                basin.Terrain = TerrainType.Pit;
                checkedLine = true;
            }
            Require(checkedLine, "Cellar contains sight lines across water");
        }

        private static void Render(DungeonBiome biome, string path)
        {
            var root = new GameObject("Biome art validation");
            var cameraObject = new GameObject("Biome camera");
            var target = new RenderTexture(960, 720, 24) { filterMode = FilterMode.Point };
            var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var config = biome == DungeonBiome.Crypt ? ExpeditionSelection.Offers[1].CreateDungeonConfig() : ExpeditionSelection.Offers[3].CreateDungeonConfig();
                var map = DungeonGenerator.Generate(8, config);
                var layout = new HexLayout();
                var view = root.AddComponent<HexGridView>(); view.Initialize(map.Grid, layout, biome);
                Require(view.TileCount == 144 && view.Biome == biome, "View owns the correct biome presentation");
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>())
                {
                    renderer.gameObject.layer = 30;
                    Require(renderer.sprite.texture.filterMode == FilterMode.Point && renderer.sprite.texture.mipmapCount == 1, "Biome retains crisp point filtering");
                }
                var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 30;
                camera.orthographic = true; camera.allowMSAA = false;
                var bounds = layout.GetBounds(map.Grid);
                camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / (960f / 720f)) * 1.08f;
                camera.transform.position = bounds.center + new Vector3(0, 0, -10);
                camera.backgroundColor = new Color(0.025f, 0.035f, 0.045f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            var previous = bootstrap.ActiveController;
            AdvancePlayer(previous);
            RetreatChecks.ReachExit(previous.Turns, previous.Expedition);
            Require(previous.TryRetreat(true) && bootstrap.TryReturnToGuild(), "Return prior crypt expedition");
            Require(bootstrap.TrySelectExpedition(3) && bootstrap.TryLaunchExpedition(), "Select and launch flooded cellars");
            for (int section = 1; section <= 2; section++)
            {
                var current = bootstrap.ActiveController;
                Require(bootstrap.Dungeon.Biome == DungeonBiome.FloodedCellar && current.GetComponent<HexGridView>().Biome == DungeonBiome.FloodedCellar &&
                    current.Enemies.Count >= 3 && current.Enemies.Count <= 4, "Runtime biome and encounter wiring");
                foreach (var enemy in current.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                AdvancePlayer(current);
                RetreatChecks.ReachExit(current.Turns, current.Expedition);
                Require(current.TryExtract(true), "Complete actual cellar section");
                bootstrap.CaptureJourneyBoundary();
                Require(bootstrap.TryResolveExplorationEvent(0), "Resolve cellar boundary event");
                if (section == 1) Require(bootstrap.TryContinueJourney() && !current.gameObject.activeSelf, "Continue to second cellar section with old input disabled");
            }
            int reward = bootstrap.JourneyBoundary.Gold, gold = bootstrap.Guild.Gold;
            Require(bootstrap.TryReturnToGuild() && bootstrap.Guild.Gold == gold + reward && !bootstrap.TryReturnToGuild(), "Return cellar reward once");
            Require(bootstrap.TrySelectExpedition(1) && bootstrap.TryLaunchExpedition() && bootstrap.Dungeon.Biome == DungeonBiome.Crypt &&
                bootstrap.ActiveController.GetComponent<HexGridView>().Biome == DungeonBiome.Crypt, "Switch back to crypt presentation");
            Debug.Log("WP-32 Play Mode passed: guild offer, actual cellar encounter/view, two sections/events, reward return and switch back to crypt.");
        }

        private static void AdvancePlayer(PlayerUnitController controller)
        {
            for (int i = 0; i <= controller.Turns.Order.Count && controller.Turns.ActiveUnit.Team != UnitTeam.Player; i++)
            { controller.Turns.TryEndTurn(controller.Turns.ActiveUnit); controller.SendMessage("StartNextTurn"); }
        }

        private static string Signature(DungeonMap map) => string.Join(",", map.Grid.Cells.Select(c => (int)c.Terrain));
        private static void Reject(Action action)
        { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Require(rejected, "Invalid biome profile rejected"); }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-32: " + message); }
    }
}
