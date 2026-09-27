using System;
using System.IO;
using System.Linq;
using GuildTactics.Meta;
using GuildTactics.Expeditions;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class SaveChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Save Checkpoints")]
        public static void Run()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GuildTacticsSaveChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = System.IO.Path.Combine(directory, "guild.json");
                var store = new GuildSaveStore(path);
                Require(!store.TryLoad(out _, out _, out _), "Missing save");
                var guild = new GuildState(240);
                var offers = new ExpeditionSelection("checkpoint");
                offers.TrySelect(0); offers.RecordLaunch(); offers.RecordLaunch();
                var data = GuildSaveData.Capture(guild, offers);
                data.roster[0].health = 9;
                data.roster[1].health = 0; data.roster[1].status = (int)AdventurerStatus.BodyRecovered;
                data.roster[2].health = 0; data.roster[2].status = (int)AdventurerStatus.Lost;
                data.selected = new[] { "warrior-1", "mage-1", "ranger-2", "rogue-2" };
                data.items = new[] { "iron-edge", "crypt-mail", "healing-draught", "healing-draught" };
                File.WriteAllText(path, JsonUtility.ToJson(data));
                Require(store.TryLoad(out guild, out offers, out _), "Restore complete state");
                Require(guild.Gold == 240 && guild.Roster[0].Health == 9 && guild.Inventory.Count == 4 &&
                    guild.SelectedIds.SequenceEqual(data.selected) && offers.LaunchedCount == 2 && offers.SelectedIndex == 0,
                    "Gold, wounds, items, party and progression survive reload");
                Require(guild.TryResurrect("rogue-1") && !guild.TryResurrect("ranger-1") && guild.TryHeal("warrior-1"), "Death semantics survive reload");
                Require(store.TrySave(guild, offers, out _), "Atomic replacement");
                var nextStore = new GuildSaveStore(path);
                Require(nextStore.TryLoad(out var again, out var nextOffers, out _) && again.Gold == 205 &&
                    nextOffers.NextSeed == offers.NextSeed && again.Roster[1].Status == AdventurerStatus.Alive, "Reopen checkpoint");
                File.WriteAllText(path, "{broken");
                Require(nextStore.TryLoad(out again, out _, out var recovery) && recovery != null && again.Gold == 240, "Last good backup");
                Require(store.TrySave(again, offers, out _) && File.ReadAllText(path + ".bak").Contains("240"), "Corruption cannot poison backup");
                File.Delete(path + ".bak");
                foreach (var invalid in new[] { "{}", "null", "{broken", JsonUtility.ToJson(new GuildSaveData { version = 999 }) })
                { File.WriteAllText(path, invalid); Require(!store.TryLoad(out _, out _, out _), "Bad schema rejected"); }
                data.version = 1; data.roster[0].health = -1;
                File.WriteAllText(path, JsonUtility.ToJson(data));
                Require(!store.TryLoad(out _, out _, out _), "Invalid health rejected");
                data.roster[0].health = 9; data.items = new[] { "unknown" };
                File.WriteAllText(path, JsonUtility.ToJson(data));
                Require(!store.TryLoad(out _, out _, out _), "Unknown item rejected");
                guild = new GuildState();
                Require(store.TrySave(guild, new ExpeditionSelection("new"), out _) &&
                    store.TryLoad(out again, out _, out _) && again.Gold == 100 && again.Inventory.Count == 0 &&
                    again.Roster.All(a => a.Status == AdventurerStatus.Alive), "New guild replaces progress");
                guild.BeginExpedition();
                bool rejected = false;
                try { GuildSaveData.Capture(guild, offers); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Active battles cannot overwrite checkpoints");
                var inaccessible = new GuildSaveStore(System.IO.Path.Combine(path, "child.json"));
                Require(!inaccessible.TrySave(new GuildState(), offers, out var error) && error != null, "IO failure is reported");
                Debug.Log("WP-19 passed: round trip, progression, death, backup recovery, corruption, schema, reset and IO failures.");
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-19: " + message); }
    }
}
