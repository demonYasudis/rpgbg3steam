using System;
using System.IO;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Meta;
using GuildTactics.Expeditions;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class AcceptanceSetup
    {
        public static void PrepareManualSave()
        {
            string directory = Path.GetFullPath("Logs");
            string manual = Path.Combine(directory, "wp42-manual-save.json");
            var guild = new GuildState(); var expeditions = new ExpeditionSelection(1, 0, 0);
            var data = GuildSaveData.Capture(guild, expeditions);
            data.items = new[] { ItemDefinitions.Weapon.Id, ItemDefinitions.Armor.Id,
                ItemDefinitions.HealingDraught.Id, ItemDefinitions.HealingDraught.Id };
            guild = GuildState.Restore(data);
            Require(new GuildSaveStore(manual).TrySave(guild, expeditions, out _), "Isolated manual save");
            Require(GuildSaveLocation.Resolve(directory, new[] { "--qa-save", manual }, true) == manual, "Development override");
            Require(GuildSaveLocation.Resolve(directory, new[] { "--qa-save", manual }, false) == Path.Combine(directory, "guild-v1.json"), "Release ignores QA flag");
            bool invalid = false;
            try { GuildSaveLocation.Resolve(directory, new[] { "--qa-save", "relative.json" }, true); } catch (ArgumentException) { invalid = true; }
            Require(invalid, "Invalid override cannot fall back to user progress");
            Debug.Log("WP-42 isolated save prepared: " + manual);
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
