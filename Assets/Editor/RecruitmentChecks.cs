using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class RecruitmentChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Recruitment")]
        public static void Run()
        {
            var guild = new GuildState(200);
            Require(guild.Candidates.Count == 4 && guild.Candidates.Select(c => c.Definition).SequenceEqual(HeroDefinitions.Defaults), "Four existing classes");
            string id = guild.Candidates[0].Id;
            int changes = 0;
            guild.Changed += () => changes++;
            Require(!guild.TryHire(null) && !guild.TryHire("unknown"), "Invalid candidate is harmless");
            Require(guild.TryHire(id) && !guild.TryHire(id) && guild.Gold == 160 && changes == 1, "One payment and notification per offer");
            var hired = guild.Roster.Single(a => a.Id == id);
            Require(hired.Health == hired.Definition.MaxHealth && hired.Status == AdventurerStatus.Alive &&
                hired.Weapon == null && hired.Armor == null && hired.HealingPotions == 0, "Healthy unequipped recruit");
            Require(guild.TryToggleSelection("warrior-1") && guild.TryToggleSelection(id) && guild.CanLaunch, "Recruit selectable");
            var before = guild.Candidates.Select(c => c.Id).ToArray();
            RoundTrip(ref guild);
            Require(guild.Roster.Count == 9 && guild.SelectedIds.Contains(id) &&
                guild.Candidates.Select(c => c.Id).SequenceEqual(before) && !guild.TryHire(id), "Save/load cannot refresh or duplicate a hire");

            for (int seed = 0; seed < 20; seed++)
            {
                var party = guild.BeginExpedition();
                Require(party.Any(a => a.Id == id) && !guild.TryHire(guild.Candidates[0].Id), "Hired hero launches; away hire blocked");
                var map = DungeonGenerator.Generate(seed);
                var units = new List<UnitRuntimeState>();
                for (int i = 0; i < party.Count; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(map.Grid, party[i].Id, party[i].Definition, map.PlayerSpawns[i], out var unit), "Recruit spawns on legal cell");
                    units.Add(unit);
                }
                var turns = new TurnManager(map.Grid, units);
                var run = new ExpeditionRun(map, turns);
                guild.AttachRun(run); turns.TryStartNextTurn();
                Require(run.TryRetreat(true) && guild.TryReturn() && !guild.TryReturn(), "Return applied once");
                Require(guild.Candidates.Count == 4 && !guild.Candidates.Select(c => c.Id).SequenceEqual(before), "Board refreshed on accepted return");
                var candidate = guild.Candidates[seed % 4];
                if (guild.Gold >= GuildState.HiringCost) Require(guild.TryHire(candidate.Id), "Fresh candidate hire");
                before = guild.Candidates.Select(c => c.Id).ToArray();
                RoundTrip(ref guild);
                Require(guild.Candidates.Select(c => c.Id).SequenceEqual(before) &&
                    guild.Roster.Select(a => a.Id).Distinct().Count() == guild.Roster.Count, "Repeated loops preserve unique IDs and offers");
            }
            var poor = new GuildState(GuildState.HiringCost - 1);
            Require(!poor.TryHire(poor.Candidates[0].Id) && poor.Gold == 39 && poor.Candidates.Count == 4, "Insufficient gold consumes nothing");
            var exact = new GuildState(GuildState.HiringCost);
            Require(exact.TryHire(exact.Candidates[0].Id) && exact.Gold == 0, "Exact payment");
            var fullBoard = new GuildState(200);
            foreach (var offer in fullBoard.Candidates.ToArray()) Require(fullBoard.TryHire(offer.Id), "Hire each class once");
            RoundTrip(ref fullBoard);
            Require(fullBoard.Candidates.Count == 0 && fullBoard.Roster.Count == 12, "Empty board stays empty after reload");

            var recruitDeath = Capture(fullBoard);
            recruitDeath.roster[8].health = 0; recruitDeath.roster[8].status = (int)AdventurerStatus.Lost;
            var deadGuild = GuildState.Restore(recruitDeath);
            RoundTrip(ref deadGuild);
            Require(deadGuild.Roster[8].Status == AdventurerStatus.Lost &&
                !deadGuild.TryToggleSelection(deadGuild.Roster[8].Id), "Lost recruit cannot silently return");

            var limit = Capture(new GuildState(100000));
            var records = limit.roster.ToList();
            for (int i = 7; records.Count < GuildState.MaximumRosterSize; i++)
                records.Add(new SavedAdventurer { id = "warrior-" + i, definition = "warrior", health = HeroDefinitions.Defaults[0].MaxHealth });
            limit.roster = records.ToArray(); limit.nextRecruitNumber = GuildState.MaximumRosterSize + 10;
            var capped = GuildState.Restore(limit);
            Require(!capped.TryHire(capped.Candidates[0].Id) && capped.Gold == 100000 && capped.CanRebuildParty, "Roster boundary blocks payment but leaves party usable");

            foreach (int version in new[] { 1, 2 })
            {
                var legacy = Capture(new GuildState()); legacy.version = version;
                legacy.candidates = null; legacy.nextRecruitNumber = 0;
                var migrated = GuildState.Restore(legacy);
                Require(migrated.Roster.Count == 8 && migrated.Candidates.Count == 4 && migrated.TryHire(migrated.Candidates[0].Id), "Legacy schema migration");
                RoundTrip(ref migrated);
                Require(migrated.Roster.Count == 9, "Migrated hires persist");
            }
            var save = Capture(new GuildState());
            save.candidates[0].id = "warrior-1"; Reject(save);
            save = Capture(new GuildState()); save.candidates[0].definition = "unknown"; Reject(save);
            save = Capture(new GuildState()); save.candidates[0] = save.candidates[1]; Reject(save);
            save = Capture(new GuildState()); save.nextRecruitNumber = 3; Reject(save);
            save = Capture(new GuildState()); save.candidates = null; Reject(save);
            save = Capture(new GuildState()); save.roster[0].id = "warrior-03"; Reject(save);
            save = Capture(new GuildState()); save.roster[0].definition = "mage"; Reject(save);

            CheckRecovery(0, 0, false);
            CheckRecovery(4 * GuildState.HiringCost - 1, 0, false);
            CheckRecovery(4 * GuildState.HiringCost, 0, true);
            CheckRecovery(2 * GuildState.ResurrectionCost + 2 * GuildState.HiringCost, 2, true);
            CheckRecovery(4 * GuildState.ResurrectionCost - 1, 4, false);
            CheckRecovery(4 * GuildState.ResurrectionCost, 4, true);
            CheckDiskCheckpoint();
            Debug.Log("WP-23 passed: hiring, costs, locks, identities, repeated launches, board persistence, v1/v2 migration and recovery budgets.");
        }

        // Called only by the shared isolated Play Mode runner; never touches player checkpoints in batch mode.
        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            Require(!bootstrap.Guild.IsAway, "Recruitment scene starts at guild");
            string id = bootstrap.Guild.Candidates[0].Id;
            Require(bootstrap.Guild.TryHire(id) && bootstrap.Guild.TryToggleSelection("warrior-1") &&
                bootstrap.Guild.TryToggleSelection(id) && bootstrap.TryLaunchExpedition(), "Launch hired party through bootstrap");
            var recruit = bootstrap.ActiveController.Units.Single(u => u.InstanceId == id);
            Require(recruit.Definition == HeroDefinitions.Defaults[0] && recruit.CurrentHealth == recruit.Definition.MaxHealth &&
                bootstrap.Grid.GetCell(recruit.Position).OccupantId == id, "Recruit runtime identity and occupancy");
            Require(!bootstrap.Guild.TryHire(bootstrap.Guild.Candidates[0].Id), "Scene locks recruitment while away");
            Require(bootstrap.ActiveController.Expedition.TryRetreat(true) && bootstrap.TryReturnToGuild(), "Recruit returns to guild");
            Require(bootstrap.Guild.Roster.Any(a => a.Id == id) && bootstrap.TryLaunchExpedition() &&
                bootstrap.ActiveController.Units.Any(u => u.InstanceId == id), "Recruit participates in second expedition");
            Require(bootstrap.ActiveController.Expedition.TryRetreat(true) && bootstrap.TryReturnToGuild() &&
                bootstrap.TryStartNewGuild(), "New guild recovers without editor restart");
            Require(bootstrap.Guild.Roster.Count == 8 && bootstrap.Guild.Candidates.Count == 4, "New guild resets recruitment");
        }

        private static GuildSaveData Capture(GuildState guild) => GuildSaveData.Capture(guild, new ExpeditionSelection("recruitment"));
        private static void RoundTrip(ref GuildState guild) => guild = GuildState.Restore(JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(Capture(guild))));
        private static void Reject(GuildSaveData data)
        {
            try { GuildState.Restore(data); } catch (ArgumentException) { return; }
            throw new InvalidOperationException("WP-23: Corrupt recruitment save accepted.");
        }
        private static void CheckRecovery(int gold, int bodies, bool possible)
        {
            var data = Capture(new GuildState(gold)); data.selected = Array.Empty<string>();
            for (int i = 0; i < data.roster.Length; i++)
            { data.roster[i].health = 0; data.roster[i].status = (int)(i < bodies ? AdventurerStatus.BodyRecovered : AdventurerStatus.Lost); }
            var guild = GuildState.Restore(data);
            Require(guild.CanRebuildParty == possible && !guild.CanLaunch, "Recovery budget and terminal guild state");
            if (!possible) return;
            for (int i = 0; i < bodies && i < 4; i++) Require(guild.TryResurrect(guild.Roster[i].Id), "Recover bodies first");
            while (guild.Roster.Count(a => a.Status == AdventurerStatus.Alive) < 4)
                Require(guild.TryHire(guild.Candidates[0].Id), "Hire replacements");
            foreach (var hero in guild.Roster.Where(a => a.Status == AdventurerStatus.Alive).Take(4)) guild.TryToggleSelection(hero.Id);
            Require(guild.CanLaunch, "Recovery provides a playable party");
        }
        private static void CheckDiskCheckpoint()
        {
            string directory = Path.Combine(Path.GetTempPath(), "GuildTacticsRecruitment-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new GuildSaveStore(Path.Combine(directory, "guild.json"));
                var guild = new GuildState(); var offers = new ExpeditionSelection("hire");
                guild.Changed += () => Require(store.TrySave(guild, offers, out _), "Hire autosave callback");
                string id = guild.Candidates[0].Id;
                Require(guild.TryHire(id), "Hire triggers checkpoint");
                Require(new GuildSaveStore(store.Path).TryLoad(out var loaded, out _, out _) && loaded.Gold == 60 &&
                    loaded.Roster.Any(a => a.Id == id) && !loaded.TryHire(id), "Reopened checkpoint keeps payment and recruit");
                Require(new GuildState().Roster.Count == 8 && new GuildState().Candidates.Count == 4, "New game clears recruits and restores board");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-23: " + message); }
    }
}
