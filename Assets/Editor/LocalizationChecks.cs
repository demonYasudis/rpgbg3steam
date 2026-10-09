using System;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Units;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class LocalizationChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Interface Languages")]
        public static void Run()
        {
            var previous = Localization.Language;
            try
            {
                var labels = HeroDefinitions.Defaults.Select(d => d.DisplayName)
                    .Concat(HeroDefinitions.Defaults.SelectMany(d => d.Abilities).SelectMany(a => new[] { a.Name, a.Description }))
                    .Concat(EnemyDefinitions.Regular.Select(d => d.Unit.DisplayName))
                    .Concat(new[] { EnemyDefinitions.MiniBoss.Unit.DisplayName })
                    .Concat(ItemDefinitions.All.Select(i => i.Name))
                    .Concat(new[] { "Train attack +1", "Train defense +1", "Walls block the line to this target.",
                        "Retreat requires the active hero on EXIT. No action is required.",
                        "Retreat without the mission reward? All living heroes escape with their wounds, equipment and remaining potions. Bodies return only from reachable ground after all enemies are defeated.",
                        "Loot forfeited: {0} gold / {1} items. Mission reward: none. Survivors gain 25 XP.",
                        "Level {0}/5 | XP {1}/{2} | Upgrades {3} | Training ATK +{4}, DEF +{5}",
                        "Survivors gain 100 XP on extraction, 25 on retreat. Dead heroes gain none; resurrection keeps training." })
                    .Concat(ExpeditionSelection.Offers.SelectMany(o => new[] { o.Name, o.Difficulty, o.Reward })).ToArray();
                Localization.SetLanguage(InterfaceLanguage.Russian, false);
                foreach (var label in labels) Require(Localization.T(label) != label, "Missing Russian content: " + label);
                Require(Localization.AdventurerName("warrior-1") == "Воин 1", "Localized roster label");
                Require(Localization.AdventurerName("custom-unit-1") == "custom-unit-1", "Unknown IDs retained");
                Require(Localization.F("Loot: {0} gold / {1} items", 42, 3) == "Добыча: 42 золота / 3 предметов", "Formatted values");
                const string combat = "Heavy Strike\n(1, 2) d20(19) +4 HIT / d8(7) +2 / -9 HP DEAD";
                Require(Localization.CombatMessage(combat) == "Тяжёлый удар\n(1, 2) d20(19) +4 ПОПАДАНИЕ / d8(7) +2 / -9 ОЗ ПОГИБ", "Combat numbers preserved");
                Require(Localization.T(null) == null && Localization.T("custom content") == "custom content", "Safe fallback");
                Require(Localization.ParsePreference("en") == InterfaceLanguage.English &&
                    Localization.ParsePreference("ru") == InterfaceLanguage.Russian &&
                    Localization.ParsePreference("invalid") == InterfaceLanguage.Russian, "Preference parsing");
                Localization.SetLanguage(InterfaceLanguage.English, false);
                foreach (var label in labels) Require(Localization.T(label) == label, "Original English retained");
                Require(Localization.CombatMessage(combat) == combat && Localization.AdventurerName("warrior-1") == "warrior-1", "Switch back without data mutation");
                Debug.Log("Localization checks passed: both languages, content coverage, format values, combat feedback, roster IDs, preference parsing.");
            }
            finally { Localization.SetLanguage(previous, false); }
        }

        public static void RunBatch()
        {
            Run();
            AbilityChecks.Run();
            GuildChecks.Run();
            ExpeditionSelectionChecks.Run();
            SaveChecks.Run();
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Localization: " + message); }
    }
}
