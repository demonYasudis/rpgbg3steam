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
        private bool confirmingRetreat;
        private bool showingLog;
        private Vector2 resultScroll;

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
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && (controller.CanPlayerAct || confirmingRetreat || showingLog);
            if (GUI.Button(new Rect(Screen.width - 140, 6, 124, 26), "Return / retreat"))
            { confirmingRetreat = !confirmingRetreat; showingLog = false; controller.InterfaceBlocked = confirmingRetreat; }
            GUI.enabled = enabledBefore;
            if (GUI.Button(new Rect(Screen.width - 140, 32, 124, 24), "Last combat result"))
            {
                // Only freeze idle player turns; combat animations and AI are never interrupted.
                if (controller.CanPlayerAct || showingLog)
                { showingLog = !showingLog; confirmingRetreat = false; controller.InterfaceBlocked = showingLog; }
            }
            if (confirmingRetreat || showingLog)
            {
                GUI.Box(new Rect(8, 58, Screen.width - 16, 168), "");
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                string message = confirmingRetreat ?
                    "Retreat to the guild? Living heroes keep their wounds. All collected loot and dead adventurers are permanently lost." :
                    controller.GetComponent<CombatText>()?.LastMessage ?? "No combat result yet.";
                resultScroll = GUI.BeginScrollView(new Rect(16, 64, Screen.width - 32, 110), resultScroll,
                    new Rect(0, 0, Screen.width - 56, Mathf.Max(100, style.CalcHeight(new GUIContent(message), Screen.width - 56))));
                GUI.Label(new Rect(0, 0, Screen.width - 56, Mathf.Max(100, style.CalcHeight(new GUIContent(message), Screen.width - 56))), message, style);
                GUI.EndScrollView();
                if (confirmingRetreat && GUI.Button(new Rect(16, 186, 210, 28), "Confirm retreat and loss"))
                {
                    if (run.TryRetreat(true))
                    { controller.InterfaceBlocked = false; confirmingRetreat = false; }
                }
                if (GUI.Button(new Rect(Screen.width - 126, 186, 110, 28), "Back to battle"))
                { confirmingRetreat = showingLog = false; controller.InterfaceBlocked = false; }
                return;
            }
            DrawMarker(run.Chest, run.ChestOpened ? "EMPTY" : "CHEST");
            DrawMarker(run.Extraction, "EXIT");
            foreach (var body in run.Bodies) DrawMarker(body.Position, body.Recoverable ? "BODY" : "LOST");
            if (confirmingAbandonment)
            {
                GUI.Box(new Rect(16, 166, Screen.width - 32, 60), "Unreachable bodies will be permanently lost.");
                if (GUI.Button(new Rect(16, 194, 220, 28), "Confirm permanent loss"))
                { controller.InterfaceBlocked = false; controller.TryExtract(true); confirmingAbandonment = false; }
                if (GUI.Button(new Rect(244, 194, 100, 28), "Cancel"))
                { confirmingAbandonment = false; controller.InterfaceBlocked = false; }
                return;
            }
            string goal = !run.ChestOpened ? "Find CHEST. Open it from the same or an adjacent visible hex (1 action)." :
                controller.Outcome == BattleOutcome.Victory ? "Area cleared. Bring one survivor to EXIT to extract the party." :
                "Loot collected. Defeat remaining enemies, then return to EXIT.";
            GUI.Label(new Rect(16, 168, Screen.width - 32, 26), goal);
            bool previous = GUI.enabled;
            GUI.enabled = previous && controller.CanPlayerAct && run.CanOpenChest(controller.SelectedUnit);
            if (GUI.Button(new Rect(16, 198, 120, 28), new GUIContent("Open chest", "Stand next to the visible chest with one action remaining."))) controller.TryOpenChest();
            GUI.enabled = previous && controller.CanPlayerAct && run.CanExtract(controller.SelectedUnit);
            if (GUI.Button(new Rect(144, 198, 120, 28), new GUIContent("Extract party", "Open the chest, defeat every enemy, then stand on EXIT.")))
            {
                if (run.HasUnrecoverableBodies) { confirmingAbandonment = true; controller.InterfaceBlocked = true; }
                else controller.TryExtract();
            }
            GUI.enabled = previous;
            GUI.Label(new Rect(274, 200, Screen.width - 290, 24),
                $"Loot: {run.CollectedGold} gold / {run.CollectedItems.Count} items");
        }

        private void DrawMarker(HexCoordinates coordinate, string label)
        {
            if (controller.Visibility != null && !controller.Visibility.IsVisible(coordinate)) return;
            var point = worldCamera.WorldToScreenPoint(layout.ToWorld(coordinate));
            float y = Screen.height - point.y;
            if (point.z <= 0 || y < HexGridInteraction.HudHeight || y > Screen.height - 28) return;
            // Place objective labels above the unit instead of covering its marker and HP.
            GUI.Label(new Rect(point.x - 30, y - 27, 60, 18), label,
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
        }

        private void DrawResult(ExpeditionResult result)
        {
            var text = new StringBuilder(result.Outcome == ExpeditionOutcome.Extracted ?
                "EXPEDITION COMPLETE" : result.Outcome == ExpeditionOutcome.Retreated ? "PARTY RETREATED" : "EXPEDITION LOST");
            text.Append("\nGold recovered: ").Append(result.Gold);
            if (bootstrap != null && bootstrap.DebugMode) text.Append(" | Seed: ").Append(result.Seed);
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
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), "Expedition result");
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            float height = style.CalcHeight(new GUIContent(text.ToString()), Screen.width - 80) + 12;
            resultScroll = GUI.BeginScrollView(new Rect(28, 44, Screen.width - 56, Screen.height - 114), resultScroll,
                new Rect(0, 0, Screen.width - 80, height));
            GUI.Label(new Rect(0, 0, Screen.width - 80, height), text.ToString(), style);
            GUI.EndScrollView();
            if (bootstrap != null && GUI.Button(new Rect(28, Screen.height - 58, 220, 32), "Return to guild")) bootstrap.TryReturnToGuild();
        }
    }
}
