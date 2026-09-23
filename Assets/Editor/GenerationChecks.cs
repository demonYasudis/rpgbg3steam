using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class GenerationChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Dungeon and Encounters")]
        public static void Run()
        {
            var layouts = new HashSet<string>();
            int bosses = 0;
            for (int seed = -50; seed < 150; seed++)
            {
                var map = DungeonGenerator.Generate(seed);
                var repeat = DungeonGenerator.Generate(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Require(DungeonValidator.IsValid(map, new DungeonGenerationConfig()), "Connected map and safe spawns");
                string signature = Signature(map);
                layouts.Add(signature);
                Require(signature == Signature(repeat) && map.Objective == repeat.Objective &&
                    map.EnemyCandidates.SequenceEqual(repeat.EnemyCandidates), "Deterministic numeric/string seed");
                var encounter = EncounterGenerator.Generate(map);
                var second = EncounterGenerator.Generate(repeat);
                Require(encounter.Count >= 3 && encounter.Count <= 5 && encounter.Sum(e => e.Archetype.Cost) <= 10,
                    "Count and budget");
                Require(encounter.Select(e => e.Position).Distinct().Count() == encounter.Count, "Unique placement");
                Require(encounter.Select(e => e.Archetype.Unit.Id + e.Position).SequenceEqual(
                    second.Select(e => e.Archetype.Unit.Id + e.Position)), "Encounter replay");
                bosses += encounter.Count(e => e.Archetype.IsMiniBoss);
                for (int i = 0; i < 4; i++)
                    Require(UnitRuntimeState.TrySpawn(map.Grid, "hero" + i, HeroDefinitions.Defaults[i],
                        map.PlayerSpawns[i], out _), "Party spawn");
                for (int i = 0; i < encounter.Count; i++)
                {
                    var enemy = encounter[i];
                    Require(map.PlayerSpawns.All(p => p.DistanceTo(enemy.Position) >= 4) &&
                        enemy.Position != map.Objective, "Spawn distance and objective reserved");
                    Require(UnitRuntimeState.TrySpawn(map.Grid, "enemy" + i, enemy.Archetype.Unit,
                        enemy.Position, out _, UnitTeam.Enemy), "Legal enemy spawn");
                }
                foreach (var hero in map.PlayerSpawns)
                    Require(map.Grid.GetNeighbors(hero).Any(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied),
                        "Party can leave occupied spawn region");
            }
            Require(layouts.Count > 20 && bosses > 0 && bosses < 200, "Variety and occasional mini-boss");
            var textMap = DungeonGenerator.Generate("склеп-alpha");
            Require(Signature(textMap) == Signature(DungeonGenerator.Generate("склеп-alpha")), "Unicode seed replay");
            Require(GenerationRandom.ParseSeed(" 123 ") == 123, "Seed normalization");
            var fallback = DungeonGenerator.Generate(1, new DungeonGenerationConfig
                { RoomRadius = 1, RoomCount = 3, MaxAttempts = 1, MinimumWalkableCells = 100 });
            Require(fallback.UsedFallback && DungeonValidator.IsValid(fallback,
                new DungeonGenerationConfig { MinimumWalkableCells = 100 }), "Validated fallback");
            var noDecoration = DungeonGenerator.Generate(3, new DungeonGenerationConfig { PitPercent = 0, HighGroundPercent = 0 });
            Require(noDecoration.Grid.Cells.All(c => c.Terrain != TerrainType.Pit && c.Terrain != TerrainType.HighGround),
                "Optional terrain disabled");
            Require(EncounterGenerator.Generate(textMap, new EncounterConfig { MiniBossPercent = 0 }).All(e => !e.Archetype.IsMiniBoss),
                "Boss disabled");
            Require(EncounterGenerator.Generate(textMap, new EncounterConfig { Budget = 20, MiniBossPercent = 100 })
                .Count(e => e.Archetype.IsMiniBoss) == 1, "Exactly one forced affordable boss");
            Require(EncounterGenerator.Generate(textMap, new EncounterConfig { Budget = 3, MaxEnemies = 3, MiniBossPercent = 100 })
                .Sum(e => e.Archetype.Cost) == 3, "Tight budget reserves minimum count");
            Reject(() => DungeonGenerator.Generate(" "));
            Reject(() => DungeonGenerator.Generate(0, new DungeonGenerationConfig { MaxAttempts = 0 }));
            Reject(() => EncounterGenerator.Generate(textMap, new EncounterConfig { Budget = 1 }));
            foreach (var position in textMap.EnemyCandidates) textMap.Grid.GetCell(position).Terrain = TerrainType.Pit;
            bool refused = false;
            try { EncounterGenerator.Generate(textMap); } catch (InvalidOperationException) { refused = true; }
            Require(refused && textMap.Grid.Cells.All(c => !c.IsOccupied), "No partial spawn when cells unavailable");
            Debug.Log("WP-11/12 passed: 200 seeds, replay, connectivity, fallback, placement, budget and bosses.");
        }

        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            var units = bootstrap.GetComponentInChildren<PlayerUnitController>();
            Require(bootstrap.Dungeon != null && units != null && units.Units.Count == 4, "Generated scene wiring");
            Require(units.Expedition != null && units.GetComponent<GuildTactics.Expeditions.ExpeditionUI>() != null,
                "Expedition and result UI wired in saved scene");
            Require(units.Enemies.Count >= 3 && units.Enemies.Count <= 5, "Generated scene encounter");
            foreach (var unit in units.Units.Concat(units.Enemies))
                Require(bootstrap.Grid.GetCell(unit.Position).OccupantId == unit.InstanceId &&
                    TerrainRules.CanWalk(bootstrap.Grid.GetCell(unit.Position).Terrain), "Runtime occupancy");
        }

        public static void RunBatch() { HexPresentationChecks.RunBatch(); }
        private static string Signature(DungeonMap map) => string.Join(",", map.Grid.Cells.Select(c => (int)c.Terrain));
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid config rejected");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-11/12: " + message); }
    }
}
