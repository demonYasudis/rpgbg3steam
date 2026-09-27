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
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), L.T("ADVENTURERS' GUILD"));
            float width = Mathf.Max(320, Screen.width - 64);
            scroll = GUI.BeginScrollView(new Rect(24, 44, Screen.width - 48, Screen.height - 64), scroll,
                new Rect(0, 0, width, 930));
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
            GUI.Label(new Rect(0, 668, width, 52), L.T("Select replacements from the reserve when someone dies.\nFewer than four living heroes: resurrect recovered bodies or start a new guild."));
            var items = new System.Text.StringBuilder(L.T("Storage (equipment use comes later):\n"));
            foreach (var definition in Expeditions.ItemDefinitions.All)
            {
                int count = 0;
                foreach (var item in guild.Inventory) if (item.Id == definition.Id) count++;
                items.Append(L.T(definition.Name)).Append(" x").Append(count).Append("; ");
            }
            GUI.Label(new Rect(0, 730, width, 60), items.ToString());
            if (GUI.Button(new Rect(0, 804, 180, 32), L.T("New guild..."))) confirmingNewGuild = true;
            GUI.Label(new Rect(0, 846, width, 72), L.SaveMessage(bootstrap.SaveMessage) ??
                L.T("Guild progress saves automatically. Quitting during an expedition restores the guild before departure."),
                new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.EndScrollView();
        }
    }
}
