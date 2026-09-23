using System;
using System.Text;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEngine;

namespace GuildTactics.Expeditions
{
    /// <summary>Temporary objective markers and result screen. Commands live in ExpeditionRun.</summary>
    public sealed class ExpeditionUI : MonoBehaviour
    {
        private PlayerUnitController controller;
        private HexLayout layout;
        private Camera worldCamera;

        public void Initialize(PlayerUnitController units, HexLayout hexLayout, Camera camera)
        {
            controller = units != null ? units : throw new ArgumentNullException(nameof(units));
            if (units.Expedition == null) throw new ArgumentException("Controller needs an expedition.");
            layout = hexLayout ?? throw new ArgumentNullException(nameof(hexLayout));
            worldCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
        }

        private void OnGUI()
        {
            if (controller == null) return;
            var run = controller.Expedition;
            if (run.Result != null) { DrawResult(run.Result); return; }
            DrawMarker(run.Chest, run.ChestOpened ? "EMPTY" : "CHEST");
            DrawMarker(run.Extraction, "EXIT");
            string goal = !run.ChestOpened ? "Find CHEST. Open it from the same or an adjacent visible hex (1 action)." :
                controller.Outcome == BattleOutcome.Victory ? "Area cleared. Bring one survivor to EXIT to extract the party." :
                "Loot collected. Defeat remaining enemies, then return to EXIT.";
            GUI.Label(new Rect(24, 326, Screen.width - 48, 24), goal);
            bool previous = GUI.enabled;
            GUI.enabled = previous && controller.CanPlayerAct && run.CanOpenChest(controller.SelectedUnit);
            if (GUI.Button(new Rect(24, 352, 140, 28), "Open chest")) controller.TryOpenChest();
            GUI.enabled = previous && controller.CanPlayerAct && run.CanExtract(controller.SelectedUnit);
            if (GUI.Button(new Rect(176, 352, 140, 28), "Extract party")) controller.TryExtract();
            GUI.enabled = previous;
            GUI.Label(new Rect(328, 354, Screen.width - 352, 24),
                $"Loot: {run.CollectedGold} gold / {run.CollectedItems.Count} items");
        }

        private void DrawMarker(HexCoordinates coordinate, string label)
        {
            if (controller.Visibility != null && !controller.Visibility.IsVisible(coordinate)) return;
            var point = worldCamera.WorldToScreenPoint(layout.ToWorld(coordinate));
            float y = Screen.height - point.y;
            if (point.z <= 0 || y < HexGridInteraction.HudHeight || y > Screen.height - 28) return;
            GUI.Box(new Rect(point.x - 30, y - 12, 60, 24), label);
        }

        private static void DrawResult(ExpeditionResult result)
        {
            var text = new StringBuilder(result.Outcome == ExpeditionOutcome.Extracted ?
                "EXPEDITION COMPLETE" : "EXPEDITION LOST");
            text.Append("\nSeed: ").Append(result.Seed).Append(" | Gold recovered: ").Append(result.Gold);
            foreach (var item in result.Items)
            {
                text.Append("\n").Append(item.Name).Append(" — ");
                if (item.Category == ItemCategory.Weapon)
                    text.Append("weapon, d").Append(item.DamageDie).Append(", attack +").Append(item.AttackBonus);
                else if (item.Category == ItemCategory.Armor) text.Append("armor, defense +").Append(item.DefenseBonus);
                else text.Append("healing consumable, ").Append(item.Healing).Append(" HP");
            }
            foreach (var unit in result.Adventurers)
                text.Append("\n").Append(unit.Name).Append(unit.Survived ? $": {unit.Health}/{unit.MaxHealth} HP" : ": DEAD");
            text.Append("\nRewards are expedition data; equipment and guild storage come later.");
            text.Append("\nStop and restart Play for a new expedition.");
            GUI.Box(new Rect(20, 84, Screen.width - 40, 210), "");
            GUI.Label(new Rect(32, 92, Screen.width - 64, 200), text.ToString());
        }
    }
}
