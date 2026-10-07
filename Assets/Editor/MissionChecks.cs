using System;
using System.Linq;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class MissionChecks
    {
        public static void Run()
        {
            foreach (var mission in new[] { MissionDefinition.Relic, MissionDefinition.Hunt, MissionDefinition.Clear })
            for (int seed = 0; seed < 40; seed++)
            {
                string replay = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var map = DungeonGenerator.Generate(seed);
                    var party = new List<UnitRuntimeState>();
                    var enemies = new List<UnitRuntimeState>();
                    for (int i = 0; i < 4; i++)
                    {
                        Require(UnitRuntimeState.TrySpawn(map.Grid, "hero" + i, HeroDefinitions.Defaults[i], map.PlayerSpawns[i], out var hero), "Spawn hero");
                        party.Add(hero);
                    }
                    foreach (var placement in EncounterGenerator.Generate(map))
                    {
                        Require(UnitRuntimeState.TrySpawn(map.Grid, "enemy" + enemies.Count, placement.Archetype.Unit, placement.Position, out var enemy, UnitTeam.Enemy), "Spawn enemy");
                        enemies.Add(enemy);
                    }
                    var turns = new TurnManager(map.Grid, party.Concat(enemies));
                    var run = new ExpeditionRun(map, turns, mission);
                    turns.TryStartNextTurn();
                    for (int i = 0; i <= turns.Order.Count && turns.ActiveUnit != party[0]; i++)
                    { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
                    var actor = party[0];
                    Require(!run.CanExtract(actor) && !run.MissionCompleted, "Incomplete mission blocks extraction");
                    if (mission.Type == MissionType.RecoverRelic)
                    {
                        Require(actor.TryRelocate(map.Grid, run.Chest) && run.TryOpenChest(actor), "Relic pickup");
                        Require(run.RemainingEnemies == enemies.Count, "Relic needs no kills");
                    }
                    else if (mission.Type == MissionType.EliminateTarget)
                    {
                        Require(!run.CanOpenChest(actor), "Hunt needs no chest");
                        run.MissionTarget.ApplyDamage(run.MissionTarget.CurrentHealth);
                        Require(run.RemainingEnemies > 0, "Hunt needs no full clear");
                    }
                    else
                    {
                        enemies[0].ApplyDamage(enemies[0].CurrentHealth);
                        Require(!run.MissionCompleted, "Partial clear insufficient");
                        foreach (var enemy in enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                        Require(!run.ChestOpened, "Clear needs no chest");
                    }
                    Require(run.MissionCompleted, "Objective completion");
                    Require(actor.Position == run.Extraction || actor.TryRelocate(map.Grid, run.Extraction), "Reach exit");
                    bool partial = mission.Type != MissionType.ClearArea;
                    if (partial)
                    {
                        party[1].ApplyDamage(party[1].CurrentHealth);
                        Require(run.HasUnrecoverableBodies && !run.TryExtract(actor), "Unsafe bodies require confirmation");
                    }
                    Require(run.TryExtract(actor, partial) && !run.TryExtract(actor, true), "One extraction only");
                    Require(run.Result.Gold >= mission.MinimumGold && run.Result.Gold <= mission.MaximumGold && run.Result.Items.Count == 2, "Mission reward range");
                    if (partial) Require(run.Result.Adventurers.All(a => !a.BodyRecovered), "No recovery before clear");
                    string signature = run.MissionTarget?.InstanceId + ":" + run.Result.Gold + ":" + string.Join(",", run.Result.Items.Select(item => item.Id));
                    if (attempt == 0) replay = signature;
                    else Require(replay == signature, "Seed reproduces target and reward");
                }
            }
            Require(ExpeditionSelection.Offers.Count == 3 && ExpeditionSelection.Offers[0].Mission == MissionDefinition.Relic &&
                ExpeditionSelection.Offers[1].Mission == MissionDefinition.Clear && ExpeditionSelection.Offers[2].Mission == MissionDefinition.Hunt, "Stable offer indices");
            Debug.Log("WP-28 passed: three mission types across 40 seeds, partial extraction, body loss confirmation, repeatable targets/rewards and single payout.");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-28: " + message); }
    }
}
