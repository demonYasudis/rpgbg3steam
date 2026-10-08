using L = GuildTactics.Core.Localization;
using System;
using GuildTactics.Core;
using UnityEngine;

namespace GuildTactics.Meta
{
    /// <summary>Temporary session guild screen; all resource mutations are validated by GuildState.</summary>
    public sealed class GuildUI : MonoBehaviour
    {
        private GameBootstrap bootstrap;
        private Vector2 scroll;
        private bool confirmingNewGuild;
        private int loadoutIndex;
        public void Initialize(GameBootstrap owner) => bootstrap = owner != null ? owner : throw new ArgumentNullException(nameof(owner));

        private void OnGUI()
        {
            if (bootstrap == null || bootstrap.Guild.IsAway) return;
            if (confirmingNewGuild)
            {
                GUI.Box(new Rect(12, 12, Screen.width - 24, 150), L.T("Start a new guild?"));
                GUI.Label(new Rect(28, 44, Screen.width - 56, 50), L.T("Current gold, roster and stored items will be replaced. This cannot be undone."),
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
                if (GUI.Button(new Rect(28, 106, 180, 32), L.T("Confirm new guild")))
                { bootstrap.TryStartNewGuild(); confirmingNewGuild = false; scroll = Vector2.zero; loadoutIndex = 0; }
                if (GUI.Button(new Rect(220, 106, 100, 32), L.T("Cancel"))) confirmingNewGuild = false;
                return;
            }
            var guild = bootstrap.Guild;
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), L.T("ADVENTURERS' GUILD"));
            float width = Mathf.Max(320, Screen.width - 64);
            float extraHeight = (guild.Roster.Count - 8) * 42 + 300;
            scroll = GUI.BeginScrollView(new Rect(24, 44, Screen.width - 48, Screen.height - 64), scroll,
                new Rect(0, 0, width, 1672 + extraHeight));
            GUI.Label(new Rect(0, 0, width, 26), L.F("Gold: {0} | Party: {1}/4 | Stored items: {2}", guild.Gold, guild.SelectedIds.Count, guild.Inventory.Count));
            GUI.Label(new Rect(0, 28, width, 26), L.T("Choose four living adventurers. Wounds persist; healing costs 5 gold."));
            bool previous = GUI.enabled;
            int row = 0;
            foreach (var adventurer in guild.Roster)
            {
                float y = 66 + row++ * 42;
                bool selected = false;
                foreach (var id in guild.SelectedIds) if (id == adventurer.Id) selected = true;
                GUI.enabled = previous && adventurer.Status == AdventurerStatus.Alive && (selected || guild.SelectedIds.Count < 4);
                if (GUI.Button(new Rect(0, y, 82, 32), selected ? L.T("Remove") : L.T("Select"))) guild.TryToggleSelection(adventurer.Id);
                GUI.enabled = previous;
                string state = adventurer.Status == AdventurerStatus.Alive ? L.F("{0}/{1} HP", adventurer.Health, adventurer.Definition.MaxHealth) :
                    adventurer.Status == AdventurerStatus.BodyRecovered ? L.T("DEAD — body recovered") : L.T("PERMANENTLY LOST");
                GUI.Label(new Rect(94, y + 4, width - 224, 28), L.AdventurerName(adventurer.Id) + " | " +
                    L.F("Lv {0}", adventurer.Progression.Level) + " | " + state);
                if (adventurer.Status == AdventurerStatus.BodyRecovered)
                {
                    GUI.enabled = previous && guild.Gold >= GuildState.ResurrectionCost;
                    if (GUI.Button(new Rect(width - 130, y, 130, 32), L.T("Resurrect (30)"))) guild.TryResurrect(adventurer.Id);
                }
                else if (adventurer.Status == AdventurerStatus.Alive)
                {
                    GUI.enabled = previous && guild.Gold >= GuildState.HealingCost && adventurer.Health < adventurer.Definition.MaxHealth;
                    if (GUI.Button(new Rect(width - 130, y, 130, 32), L.T("Heal (5)"))) guild.TryHeal(adventurer.Id);
                }
                GUI.enabled = previous;
            }
            DrawRecruitment(guild, width, 66 + row * 42, previous);
            // Keep the existing controls below the variable-length roster and recruitment board.
            GUI.BeginGroup(new Rect(0, extraHeight, width, 1672));
            GUI.Label(new Rect(0, 408, width, 24), L.T("Choose an expedition — crypt ruins"));
            for (int i = 0; i < Expeditions.ExpeditionSelection.Offers.Count; i++)
            {
                var offer = Expeditions.ExpeditionSelection.Offers[i];
                if (GUI.Toggle(new Rect(0, 438 + i * 28, width, 26), bootstrap.Expeditions.SelectedIndex == i,
                    L.T(offer.Name) + " | " + L.T(offer.Difficulty))) bootstrap.TrySelectExpedition(i);
            }
            GUI.Label(new Rect(0, 498, width, 26), L.T("Possible reward: ") + L.T(bootstrap.Expeditions.Selected.Reward));
            if (bootstrap.DebugMode) GUI.Label(new Rect(0, 524, width, 24), L.T("Next seed: ") + bootstrap.Expeditions.NextSeed);
            GUI.enabled = previous && guild.CanLaunch;
            if (GUI.Button(new Rect(0, 554, 290, 36), L.T("Start ") + L.T(bootstrap.Expeditions.Selected.Name))) bootstrap.TryLaunchExpedition();
            GUI.enabled = previous;
            if (!guild.CanLaunch) GUI.Label(new Rect(300, 554, width - 300, 46), L.T("Select four living adventurers to depart."), new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.Label(new Rect(0, 610, width, 52), L.T("Successful extraction recovers bodies on reachable ground.\nBodies in pits and all bodies after defeat are permanently lost."));
            GUI.Label(new Rect(0, 668, width, 52), L.T("Select replacements from the reserve or hire candidates.\nRecovered bodies can be resurrected for 30 gold."));
            var items = new System.Text.StringBuilder(L.T("Storage:\n"));
            foreach (var definition in Expeditions.ItemDefinitions.All)
            {
                int count = 0;
                foreach (var item in guild.Inventory) if (item.Id == definition.Id) count++;
                items.Append(L.T(definition.Name)).Append(" x").Append(count).Append("; ");
            }
            GUI.Label(new Rect(0, 730, width, 60), items.ToString());
            DrawLoadout(guild, width, previous);
            DrawDevelopment(guild, width, previous);
            if (GUI.Button(new Rect(0, 1538, 180, 32), L.T("New guild..."))) confirmingNewGuild = true;
            GUI.Label(new Rect(0, 1580, width, 72), L.SaveMessage(bootstrap.SaveMessage) ??
                L.T("Guild progress saves automatically. Quitting during an expedition restores the guild before departure."),
                new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.EndGroup();
            GUI.EndScrollView();
        }

        private void DrawRecruitment(GuildState guild, float width, float y, bool previous)
        {
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUI.Label(new Rect(0, y, width, 24), L.F("Recruitment — hire for {0} gold", GuildState.HiringCost));
            GUI.Label(new Rect(0, y + 26, width, 48), L.T("One candidate per class. The board refreshes after returning from an expedition. Opening the guild or loading does not refresh it."), wrap);
            // Keep class rows stable so a second click cannot hire the next candidate by accident.
            for (int row = 0; row < Units.HeroDefinitions.Defaults.Count; row++)
            {
                var definition = Units.HeroDefinitions.Defaults[row];
                GuildAdventurer candidate = null;
                foreach (var offer in guild.Candidates) if (offer.Definition == definition) candidate = offer;
                float rowY = y + 78 + row * 32;
                GUI.Label(new Rect(0, rowY, width - 140, 28), candidate == null ? L.T(definition.DisplayName) : L.AdventurerName(candidate.Id));
                GUI.enabled = previous && candidate != null && guild.Gold >= GuildState.HiringCost && guild.Roster.Count < GuildState.MaximumRosterSize;
                if (GUI.Button(new Rect(width - 140, rowY, 140, 28), candidate == null ? L.T("Unavailable") : L.F("Hire ({0})", GuildState.HiringCost)))
                    guild.TryHire(candidate?.Id);
                GUI.enabled = previous;
            }
            string message = !guild.CanRebuildParty
                ? "Cannot restore four heroes with the current gold and candidates. Start a new guild below (confirmation required)."
                : guild.Roster.Count >= GuildState.MaximumRosterSize
                    ? "Roster limit reached. Use living heroes or resurrect recovered bodies."
                    : "Hired heroes arrive healthy and unequipped. Select them for your party.";
            GUI.Label(new Rect(0, y + 214, width, 70), L.T(message), wrap);
        }

        private void DrawLoadout(GuildState guild, float width, bool previous)
        {
            loadoutIndex = Mathf.Clamp(loadoutIndex, 0, guild.Roster.Count - 1);
            float half = (width - 8) / 2;
            if (GUI.Button(new Rect(0, 804, half, 28), L.T("Previous hero"))) loadoutIndex = (loadoutIndex + guild.Roster.Count - 1) % guild.Roster.Count;
            if (GUI.Button(new Rect(half + 8, 804, half, 28), L.T("Next hero"))) loadoutIndex = (loadoutIndex + 1) % guild.Roster.Count;
            var hero = guild.Roster[loadoutIndex];
            GUI.Label(new Rect(0, 840, width, 24), L.T("Loadout: ") + L.AdventurerName(hero.Id));
            GUI.Label(new Rect(0, 866, width, 24), L.F("ATK {0} | DEF {1} | Damage d{2}+{3} | Potions {4}/2",
                hero.Attack, hero.Defense, hero.DamageDie, hero.Definition.DamageBonus, hero.HealingPotions));
            var weapon = Expeditions.ItemDefinitions.Weapon;
            var armor = Expeditions.ItemDefinitions.Armor;
            int weapons = 0, armors = 0, potions = 0;
            foreach (var item in guild.Inventory)
            { if (item == weapon) weapons++; if (item == armor) armors++; if (item == Expeditions.ItemDefinitions.HealingDraught) potions++; }
            GUI.Label(new Rect(0, 894, width, 40), L.F("Weapon: {0}. Equip: ATK {1} -> {2}, d{3} -> d{4} (stock {5})",
                hero.Weapon == null ? L.T("None") : L.T(hero.Weapon.Name), hero.Attack,
                hero.Definition.Attack + hero.Progression.AttackBonus + weapon.AttackBonus, hero.DamageDie, weapon.DamageDie, weapons), new GUIStyle(GUI.skin.label) { wordWrap = true });
            bool alive = previous && hero.Status == AdventurerStatus.Alive;
            GUI.enabled = alive && weapons > 0 && hero.Weapon != weapon;
            if (GUI.Button(new Rect(0, 936, half, 28), L.T("Equip weapon"))) guild.TryEquip(hero.Id, weapon.Id);
            GUI.enabled = alive && hero.Weapon != null;
            if (GUI.Button(new Rect(half + 8, 936, half, 28), L.T("Remove weapon"))) guild.TryUnequip(hero.Id, Expeditions.ItemCategory.Weapon);
            GUI.enabled = previous;
            GUI.Label(new Rect(0, 970, width, 40), L.F("Armor: {0}. Equip: DEF {1} -> {2} (stock {3})",
                hero.Armor == null ? L.T("None") : L.T(hero.Armor.Name), hero.Defense,
                hero.Definition.Defense + hero.Progression.DefenseBonus + armor.DefenseBonus, armors), new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.enabled = alive && armors > 0 && hero.Armor != armor;
            if (GUI.Button(new Rect(0, 1012, half, 28), L.T("Equip armor"))) guild.TryEquip(hero.Id, armor.Id);
            GUI.enabled = alive && hero.Armor != null;
            if (GUI.Button(new Rect(half + 8, 1012, half, 28), L.T("Remove armor"))) guild.TryUnequip(hero.Id, Expeditions.ItemCategory.Armor);
            GUI.enabled = alive && potions > 0 && hero.HealingPotions < GuildState.MaximumHealingPotions;
            if (GUI.Button(new Rect(0, 1048, half, 28), L.T("Give potion"))) guild.TryTransferPotion(hero.Id, true);
            GUI.enabled = alive && hero.HealingPotions > 0;
            if (GUI.Button(new Rect(half + 8, 1048, half, 28), L.T("Return potion"))) guild.TryTransferPotion(hero.Id, false);
            GUI.enabled = previous;
            GUI.Label(new Rect(0, 1088, width, 105), L.T("Equip living heroes before departure. Items must be in storage.\nSurvivors keep their loadout. Recovered bodies return items to storage; lost heroes lose their gear.\nPotions heal only their owner, up to 8 HP, for one action."), new GUIStyle(GUI.skin.label) { wordWrap = true });
        }

        private void DrawDevelopment(GuildState guild, float width, bool previous)
        {
            var hero = guild.Roster[loadoutIndex];
            var progression = hero.Progression;
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUI.Label(new Rect(0, 1204, width, 24), L.T("Development: ") + L.AdventurerName(hero.Id));
            string experience = progression.Level == Units.HeroProgression.MaximumLevel ? L.T("Maximum level") :
                L.F("Next level at {0} XP", progression.NextLevelExperience);
            GUI.Label(new Rect(0, 1230, width, 44), L.F("Level {0} | XP {1} | Pending {2} | Training ATK +{3}, DEF +{4}",
                progression.Level, progression.Experience, progression.PendingChoices, progression.AttackBonus, progression.DefenseBonus), wrap);
            GUI.Label(new Rect(0, 1278, width, 24), experience);
            // Each earned level keeps its own row after choosing; a repeated click cannot spend the next choice.
            for (int level = 2; level <= Units.HeroProgression.MaximumLevel; level++)
            {
                float y = 1308 + (level - 2) * 30;
                bool chosen = progression.Upgrades.Count >= level - 1;
                GUI.Label(new Rect(0, y, 100, 24), L.F("Level {0}", level));
                if (chosen)
                    GUI.Label(new Rect(108, y, width - 108, 24), L.T(progression.Upgrades[level - 2] == Units.HeroUpgrade.Accuracy ? "Attack +1" : "Defense +1"));
                else
                {
                    GUI.enabled = previous && hero.Status == AdventurerStatus.Alive && progression.PendingChoices > 0 &&
                        progression.NextChoiceLevel == level;
                    float half = (width - 116) / 2;
                    if (GUI.Button(new Rect(108, y, half, 26), L.T("Attack +1"))) guild.TryChooseUpgrade(hero.Id, level, Units.HeroUpgrade.Accuracy);
                    if (GUI.Button(new Rect(116 + half, y, half, 26), L.T("Defense +1"))) guild.TryChooseUpgrade(hero.Id, level, Units.HeroUpgrade.Guard);
                    GUI.enabled = previous;
                }
            }
            GUI.Label(new Rect(0, 1436, width, 92), L.T("Choose once per earned level, in order. Training is permanent and separate from gear.\nSurvivors earn 100 XP on extraction; retreat and defeat give none. Dead heroes keep previous development; resurrection preserves it."), wrap);
        }
    }
}
