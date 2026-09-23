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
        public void Initialize(GameBootstrap owner) => bootstrap = owner != null ? owner : throw new ArgumentNullException(nameof(owner));

        private void OnGUI()
        {
            if (bootstrap == null || bootstrap.Guild.IsAway) return;
            var guild = bootstrap.Guild;
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), "GUILD — session roster");
            float width = Mathf.Max(560, Screen.width - 64);
            scroll = GUI.BeginScrollView(new Rect(24, 44, Screen.width - 48, Screen.height - 64), scroll,
                new Rect(0, 0, width, 650));
            GUI.Label(new Rect(0, 0, width, 26), $"Gold: {guild.Gold} | Party: {guild.SelectedIds.Count}/4 | Stored items: {guild.Inventory.Count}");
            GUI.Label(new Rect(0, 28, width, 26), "Choose four living adventurers. Wounds persist; healing costs 5 gold.");
            bool previous = GUI.enabled;
            int row = 0;
            foreach (var adventurer in guild.Roster)
            {
                float y = 66 + row++ * 42;
                bool selected = false;
                foreach (var id in guild.SelectedIds) if (id == adventurer.Id) selected = true;
                GUI.enabled = previous && adventurer.Status == AdventurerStatus.Alive && (selected || guild.SelectedIds.Count < 4);
                if (GUI.Button(new Rect(0, y, 82, 32), selected ? "Remove" : "Select")) guild.TryToggleSelection(adventurer.Id);
                GUI.enabled = previous;
                string state = adventurer.Status == AdventurerStatus.Alive ? $"{adventurer.Health}/{adventurer.Definition.MaxHealth} HP" :
                    adventurer.Status == AdventurerStatus.BodyRecovered ? "DEAD — body recovered" : "PERMANENTLY LOST";
                GUI.Label(new Rect(94, y + 4, width - 224, 28), adventurer.Id + " | " + state);
                if (adventurer.Status == AdventurerStatus.BodyRecovered)
                {
                    GUI.enabled = previous && guild.Gold >= GuildState.ResurrectionCost;
                    if (GUI.Button(new Rect(width - 130, y, 130, 32), "Resurrect (30)")) guild.TryResurrect(adventurer.Id);
                }
                else if (adventurer.Status == AdventurerStatus.Alive)
                {
                    GUI.enabled = previous && guild.Gold >= GuildState.HealingCost && adventurer.Health < adventurer.Definition.MaxHealth;
                    if (GUI.Button(new Rect(width - 130, y, 130, 32), "Heal (5)")) guild.TryHeal(adventurer.Id);
                }
                GUI.enabled = previous;
            }
            GUI.enabled = previous && guild.CanLaunch;
            if (GUI.Button(new Rect(0, 412, 240, 36), "Start crypt expedition")) bootstrap.TryLaunchExpedition();
            GUI.enabled = previous;
            GUI.Label(new Rect(0, 462, width, 52), "Successful extraction recovers bodies on reachable ground.\nBodies in pits and all bodies after defeat are permanently lost.");
            GUI.Label(new Rect(0, 520, width, 52), "Select replacements from the reserve when someone dies.\nFewer than four living heroes: resurrect recovered bodies or restart Play for a new guild.");
            var items = new System.Text.StringBuilder("Storage (equipment use comes later):\n");
            foreach (var definition in Expeditions.ItemDefinitions.All)
            {
                int count = 0;
                foreach (var item in guild.Inventory) if (item.Id == definition.Id) count++;
                items.Append(definition.Name).Append(" x").Append(count).Append("; ");
            }
            GUI.Label(new Rect(0, 582, width, 60), items.ToString());
            GUI.EndScrollView();
        }
    }
}
