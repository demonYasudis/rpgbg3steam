using System;
using System.Linq;
using GuildTactics.Meta;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.Units;
using GuildTactics.Combat;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class RecruitmentChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Recruitment")]
        public static void Run()
        {
            var guild = new GuildState();
            int changes = 0; guild.Changed += () => changes++;
            var offers = guild.Candidates.ToArray();
            Require(offers.Length == 4 && guild.TryHire(offers[0]) && guild.Gold == 80 && changes == 1, "Hire once");
            Require(!guild.TryHire(offers[0]) && !guild.TryHire("unknown") && guild.Gold == 80 && changes == 1, "Reject duplicate and unknown");
            string id = offers[0];
            Require(guild.Roster.Last().Id == id && guild.Roster.Last().Health == guild.Roster.Last().Definition.MaxHealth, "Unique healthy recruit");
            guild.TryToggleSelection(guild.SelectedIds[0]);
            Require(guild.TryToggleSelection(id) && guild.CanLaunch, "Recruit can join party");
            var data = GuildSaveData.Capture(guild, new ExpeditionSelection("23"));
            guild = GuildState.Restore(JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(data)));
            Require(guild.Gold == 80 && guild.Roster.Count == 9 && guild.SelectedIds.Contains(id) &&
                guild.Candidates.SequenceEqual(offers.Skip(1)), "Save roundtrip preserves hires and consumed offers");
            guild.BeginExpedition();
            Require(!guild.TryHire(offers[1]), "Cannot hire during expedition");
            guild.CancelLaunch();
            Require(guild.Candidates.SequenceEqual(offers.Skip(1)), "Cancelled departure does not refresh");
            Require(guild.TryHire(offers[1]), "Hire after reload");
            var saved = GuildSaveData.Capture(guild, new ExpeditionSelection("23"));
            saved.candidates = new[] { id }; Reject(() => GuildState.Restore(saved));
            saved.candidates = new[] { "mage-99" }; Reject(() => GuildState.Restore(saved));
            saved.candidates = Array.Empty<string>(); saved.roster[8].id = saved.roster[0].id;
            Reject(() => GuildState.Restore(saved));
            foreach (int version in new[] { 1, 2 })
            {
                var legacy = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("23"));
                legacy.version = version; legacy.candidates = null; legacy.nextRecruitId = 0;
                Require(GuildState.Restore(legacy).Candidates.Count == 4, "Legacy migration");
            }
            data = GuildSaveData.Capture(new GuildState(19), new ExpeditionSelection("23"));
            foreach (var hero in data.roster) { hero.status = (int)AdventurerStatus.Lost; hero.health = 0; }
            data.selected = Array.Empty<string>();
            guild = GuildState.Restore(data);
            Require(!guild.CanHire && !guild.CanRebuildParty && !guild.TryHire(guild.Candidates[0]), "No funds has explicit new-game exit");
            data.gold = 80; guild = GuildState.Restore(data);
            Require(guild.CanRebuildParty, "Four replacements affordable");
            foreach (var candidate in guild.Candidates.ToArray()) Require(guild.TryHire(candidate) && guild.TryToggleSelection(candidate), "Rebuild");
            Require(guild.CanLaunch && guild.Gold == 0, "Recovered party can launch");
            var map = DungeonGenerator.Generate(23);
            var party = guild.BeginExpedition();
            var units = new System.Collections.Generic.List<UnitRuntimeState>();
            for (int i = 0; i < party.Count; i++)
            {
                Require(UnitRuntimeState.TrySpawn(map.Grid, party[i].Id, party[i].Definition, map.PlayerSpawns[i], out var unit), "Recruit spawns");
                units.Add(unit);
            }
            var turns = new TurnManager(map.Grid, units);
            var run = new ExpeditionRun(map, turns);
            guild.AttachRun(run);
            turns.TryStartNextTurn();
            Require(run.TryRetreat(true) && guild.TryReturn(), "Recruits return from expedition");
            Require(guild.Candidates.Count == 4 && guild.Candidates.All(c => !guild.Roster.Any(h => h.Id == c)), "Return refreshes unique offers");
            var refreshed = guild.Candidates.ToArray();
            Require(!guild.TryReturn() && guild.Candidates.SequenceEqual(refreshed), "Result cannot refresh twice");
            Debug.Log("WP-23 recruitment checks passed: hiring, cost, unique IDs, save migration, offer persistence, party rebuilding and failure paths.");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-23: " + message); }
        private static void Reject(Action action)
        { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid recruitment save accepted."); }
    }
}
