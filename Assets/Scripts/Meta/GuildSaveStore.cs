using System;
using System.IO;
using System.Linq;
using System.Text;
using GuildTactics.Expeditions;
using UnityEngine;

namespace GuildTactics.Meta
{
    [Serializable]
    public sealed class SavedAdventurer
    {
        public string id, definition;
        public int health, status;
        public string weapon, armor;
        public int potions;
    }

    [Serializable]
    public sealed class SavedCandidate
    {
        public string id, definition;
    }

    [Serializable]
    public sealed class GuildSaveData
    {
        public const int CurrentVersion = 3;
        public int version, gold, initialSeed, selectedExpedition, launchedCount;
        public SavedAdventurer[] roster;
        public string[] selected, items;
        public int nextRecruitNumber;
        public SavedCandidate[] candidates;

        public static GuildSaveData Capture(GuildState guild, ExpeditionSelection expeditions)
        {
            if (guild.IsAway) throw new InvalidOperationException("Save only at the guild checkpoint.");
            return new GuildSaveData
            {
                version = CurrentVersion, gold = guild.Gold, initialSeed = expeditions.InitialSeed,
                selectedExpedition = expeditions.SelectedIndex, launchedCount = expeditions.LaunchedCount,
                roster = guild.Roster.Select(a => new SavedAdventurer
                { id = a.Id, definition = a.Definition.Id, health = a.Health, status = (int)a.Status,
                    weapon = a.Weapon?.Id, armor = a.Armor?.Id, potions = a.HealingPotions }).ToArray(),
                selected = guild.SelectedIds.ToArray(), items = guild.Inventory.Select(i => i.Id).ToArray(),
                nextRecruitNumber = guild.NextRecruitNumber,
                candidates = guild.Candidates.Select(a => new SavedCandidate { id = a.Id, definition = a.Definition.Id }).ToArray()
            };
        }
    }

    /// <summary>Versioned guild checkpoints, atomic replacement and last-good backup.</summary>
    public sealed class GuildSaveStore
    {
        private const long MaximumBytes = 8 * 1024 * 1024;
        public string Path { get; }
        public GuildSaveStore(string path) => Path = System.IO.Path.GetFullPath(path);

        public bool TryLoad(out GuildState guild, out ExpeditionSelection expeditions, out string message)
        {
            guild = null; expeditions = null; message = null;
            if (!File.Exists(Path) && !File.Exists(Path + ".bak")) return false;
            if (TryRead(Path, out guild, out expeditions)) return true;
            if (TryRead(Path + ".bak", out guild, out expeditions))
            { message = "Recovered the previous guild checkpoint from backup."; return true; }
            message = "Save unavailable or incompatible. A fresh guild is open; old files are kept until the next successful save.";
            return false;
        }

        private static bool TryRead(string path, out GuildState guild, out ExpeditionSelection expeditions)
        {
            guild = null; expeditions = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return false;
                var data = JsonUtility.FromJson<GuildSaveData>(File.ReadAllText(path));
                var restored = GuildState.Restore(data);
                expeditions = new ExpeditionSelection(data.initialSeed, data.selectedExpedition, data.launchedCount);
                guild = restored; return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { return false; }
        }

        public bool TrySave(GuildState guild, ExpeditionSelection expeditions, out string message)
        {
            message = null;
            try
            {
                var data = GuildSaveData.Capture(guild, expeditions);
                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                using (var file = new FileStream(Path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(Path))
                {
                    // Never replace a valid backup with a corrupt primary.
                    bool valid = TryRead(Path, out _, out _);
                    File.Replace(Path + ".tmp", Path, valid ? Path + ".bak" : null);
                }
                else File.Move(Path + ".tmp", Path);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { message = "Could not save guild: " + error.Message; return false; }
        }
    }
}
