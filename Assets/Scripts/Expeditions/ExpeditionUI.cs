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
        private Core.GameBootstrap bootstrap;
        private bool confirmingAbandonment;

        public void Initialize(PlayerUnitController units, HexLayout hexLayout, Camera camera, Core.GameBootstrap owner = null)
        {
            controller = units != null ? units : throw new ArgumentNullException(nameof(units));
            if (units.Expedition == null) throw new ArgumentException("Controller needs an expedition.");
            layout = hexLayout ?? throw new ArgumentNullException(nameof(hexLayout));
            worldCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            bootstrap = owner;
        }

        private void OnGUI()
        {
            if (controller == null) return;
            var run = controller.Expedition;
            if (run.Result != null) { DrawResult(run.Result); return; }
            DrawMarker(run.Chest, run.ChestOpened ? "EMPTY" : "CHEST");
            DrawMarker(run.Extraction, "EXIT");
            foreach (var body in run.Bodies) DrawMarker(body.Position, body.Recoverable ? "BODY" : "LOST");
            if (confirmingAbandonment)
            {
                GUI.Box(new Rect(20, 322, Screen.width - 40, 64), "Unreachable bodies will be permanently lost.");
                if (GUI.Button(new Rect(24, 350, 220, 30), "Confirm permanent loss"))
                { controller.TryExtract(true); confirmingAbandonment = false; }
                if (GUI.Button(new Rect(256, 350, 100, 30), "Cancel")) confirmingAbandonment = false;
                return;
            }
            string goal = !run.ChestOpened ? "Find CHEST. Open it from the same or an adjacent visible hex (1 action)." :
                controller.Outcome == BattleOutcome.Victory ? "Area cleared. Bring one survivor to EXIT to extract the party." :
                "Loot collected. Defeat remaining enemies, then return to EXIT.";
            GUI.Label(new Rect(24, 326, Screen.width - 48, 24), goal);
            bool previous = GUI.enabled;
            GUI.enabled = previous && controller.CanPlayerAct && run.CanOpenChest(controller.SelectedUnit);
            if (GUI.Button(new Rect(24, 352, 140, 28), "Open chest")) controller.TryOpenChest();
            GUI.enabled = previous && controller.CanPlayerAct && run.CanExtract(controller.SelectedUnit);
            if (GUI.Button(new Rect(176, 352, 140, 28), "Extract party"))
            {
                if (run.HasUnrecoverableBodies) confirmingAbandonment = true;
                else controller.TryExtract();
            }
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

        private void DrawResult(ExpeditionResult result)
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
                text.Append("\n").Append(unit.InstanceId).Append(unit.Survived ? $": {unit.Health}/{unit.MaxHealth} HP" :
                    unit.BodyRecovered ? ": DEAD — body recovered" : ": PERMANENTLY LOST");
            text.Append("\nRecovered bodies can be resurrected in the guild for 30 gold.");
            GUI.Box(new Rect(20, 84, Screen.width - 40, 210), "");
            GUI.Label(new Rect(32, 92, Screen.width - 64, 200), text.ToString());
            if (bootstrap != null && GUI.Button(new Rect(32, 304, 200, 32), "Return to guild")) bootstrap.TryReturnToGuild();
        }
    }
}
