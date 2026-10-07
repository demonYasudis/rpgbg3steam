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
                { bootstrap.TryStartNewGuild(); confirmingNewGuild = false; scroll = Vector2.zero; }
                if (GUI.Button(new Rect(220, 106, 100, 32), L.T("Cancel"))) confirmingNewGuild = false;
                return;
            }
            var guild = bootstrap.Guild;
            float extraRows = (guild.Roster.Count - 8) * 42;
            float recruitmentY = 1316 + extraRows;
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), L.T("ADVENTURERS' GUILD"));
            float width = Mathf.Max(320, Screen.width - 64);
            scroll = GUI.BeginScrollView(new Rect(24, 44, Screen.width - 48, Screen.height - 64), scroll,
                new Rect(0, 0, width, 1762 + extraRows));
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
                GUI.Label(new Rect(94, y + 4, width - 224, 28), L.AdventurerName(adventurer.Id) + " | " + state);
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
            GUI.BeginGroup(new Rect(0, extraRows, width, 1316));
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
            GUI.Label(new Rect(0, 668, width, 52), L.T("Select replacements from the reserve when someone dies.\nFewer than four living heroes: hire candidates, resurrect bodies or start a new guild."));
            var items = new System.Text.StringBuilder(L.T("Storage:\n"));
            foreach (var definition in Expeditions.ItemDefinitions.All)
            {
                int count = 0;
                foreach (var item in guild.Inventory) if (item.Id == definition.Id) count++;
                items.Append(L.T(definition.Name)).Append(" x").Append(count).Append("; ");
            }
            GUI.Label(new Rect(0, 730, width, 60), items.ToString());
            DrawLoadout(guild, width, previous);
            GUI.EndGroup();
            GUI.Label(new Rect(0, recruitmentY, width, 24), L.T("Recruitment - 20 gold per hero"));
            GUI.Label(new Rect(0, recruitmentY + 28, width, 52), L.T("Candidates refresh after returning from an expedition. Offers and hires are saved."), new GUIStyle(GUI.skin.label) { wordWrap = true });
            int candidateRow = 0;
            string hireId = null;
            foreach (var id in guild.Candidates)
            {
                float y = recruitmentY + 84 + candidateRow++ * 36;
                GUI.Label(new Rect(0, y, width - 130, 28), L.AdventurerName(id));
                GUI.enabled = previous && guild.CanHire;
                if (GUI.Button(new Rect(width - 130, y, 130, 28), L.T("Hire (20)"))) hireId = id;
            }
            GUI.enabled = previous;
            if (hireId != null) guild.TryHire(hireId);
            if (!guild.CanRebuildParty) GUI.Label(new Rect(0, recruitmentY + 232, width, 60), L.T("Not enough heroes and gold to rebuild a party. Start a new guild to continue."), new GUIStyle(GUI.skin.label) { wordWrap = true });
            if (GUI.Button(new Rect(0, recruitmentY + 300, 180, 32), L.T("New guild..."))) confirmingNewGuild = true;
            GUI.Label(new Rect(0, recruitmentY + 342, width, 72), L.SaveMessage(bootstrap.SaveMessage) ??
                L.T("Guild progress saves automatically. Quitting during an expedition restores the guild before departure."),
                new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.EndScrollView();
        }

        private void DrawLoadout(GuildState guild, float width, bool previous)
        {
            float half = (width - 8) / 2;
            if (GUI.Button(new Rect(0, 804, half, 28), L.T("Previous hero"))) loadoutIndex = (loadoutIndex + guild.Roster.Count - 1) % guild.Roster.Count;
            if (GUI.Button(new Rect(half + 8, 804, half, 28), L.T("Next hero"))) loadoutIndex = (loadoutIndex + 1) % guild.Roster.Count;
            loadoutIndex %= guild.Roster.Count;
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
                hero.Definition.Attack + hero.TrainingAttack + weapon.AttackBonus, hero.DamageDie, weapon.DamageDie, weapons), new GUIStyle(GUI.skin.label) { wordWrap = true });
            bool alive = previous && hero.Status == AdventurerStatus.Alive;
            GUI.enabled = alive && weapons > 0 && hero.Weapon != weapon;
            if (GUI.Button(new Rect(0, 936, half, 28), L.T("Equip weapon"))) guild.TryEquip(hero.Id, weapon.Id);
            GUI.enabled = alive && hero.Weapon != null;
            if (GUI.Button(new Rect(half + 8, 936, half, 28), L.T("Remove weapon"))) guild.TryUnequip(hero.Id, Expeditions.ItemCategory.Weapon);
            GUI.enabled = previous;
            GUI.Label(new Rect(0, 970, width, 40), L.F("Armor: {0}. Equip: DEF {1} -> {2} (stock {3})",
                hero.Armor == null ? L.T("None") : L.T(hero.Armor.Name), hero.Defense, hero.Definition.Defense + hero.TrainingDefense + armor.DefenseBonus, armors), new GUIStyle(GUI.skin.label) { wordWrap = true });
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
            GUI.Label(new Rect(0, 1200, width, 24), L.F("Level {0}/5 | XP {1}/{2} | Upgrades {3} | Training ATK +{4}, DEF +{5}",
                hero.Level, hero.Experience, HeroProgression.NextThreshold(hero.Experience), hero.AvailableUpgrades, hero.TrainingAttack, hero.TrainingDefense));
            GUI.enabled = alive && hero.AvailableUpgrades > 0;
            if (GUI.Button(new Rect(0, 1232, half, 28), L.T("Train attack +1"))) guild.TryUpgrade(hero.Id, HeroUpgrade.Attack);
            if (GUI.Button(new Rect(half + 8, 1232, half, 28), L.T("Train defense +1"))) guild.TryUpgrade(hero.Id, HeroUpgrade.Defense);
            GUI.enabled = previous;
            GUI.Label(new Rect(0, 1268, width, 42), L.T("Survivors gain 100 XP on extraction, 25 on retreat. Dead heroes gain none; resurrection keeps training."), new GUIStyle(GUI.skin.label) { wordWrap = true });

        }
    }
}
