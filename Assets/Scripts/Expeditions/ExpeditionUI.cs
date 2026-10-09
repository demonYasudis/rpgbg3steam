using L = GuildTactics.Core.Localization;
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
            if (units.Expedition.Mission.RequiresRelic)
            {
                var chest = new GameObject("Relic chest");
                chest.transform.SetParent(transform, false);
                chest.AddComponent<ChestView>().Initialize(units, hexLayout);
            }
        }

        private void OnGUI()
        {
            if (controller == null) return;
            var run = controller.Expedition;
            if (run.Result != null) { DrawResult(run.Result); return; }
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && !confirmingAbandonment &&
                ((controller.CanPlayerAct && run.CanRetreat(controller.SelectedUnit)) || confirmingRetreat);
            if (GUI.Button(new Rect(Screen.width - 140, 6, 124, 26), new GUIContent(L.T("Return / retreat"), L.T("Retreat requires the active hero on EXIT. No action is required."))))
            { confirmingRetreat = !confirmingRetreat; showingLog = false; controller.InterfaceBlocked = confirmingRetreat; }
            GUI.enabled = enabledBefore;
            if (GUI.Button(new Rect(Screen.width - 140, 32, 124, 24), L.T("Last combat result")))
            {
                // Only freeze idle player turns; combat animations and AI are never interrupted.
                if (controller.CanPlayerAct || showingLog)
                { showingLog = !showingLog; confirmingRetreat = false; controller.InterfaceBlocked = showingLog; }
            }
            if (confirmingRetreat || showingLog)
            {
                GUI.Box(new Rect(8, 58, Screen.width - 16, 168), "");
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                string message = confirmingRetreat ? RetreatMessage(run) :
                    controller.GetComponent<CombatText>()?.LastMessage ?? L.T("No combat result yet.");
                resultScroll = GUI.BeginScrollView(new Rect(16, 64, Screen.width - 32, 110), resultScroll,
                    new Rect(0, 0, Screen.width - 56, Mathf.Max(100, style.CalcHeight(new GUIContent(message), Screen.width - 56))));
                GUI.Label(new Rect(0, 0, Screen.width - 56, Mathf.Max(100, style.CalcHeight(new GUIContent(message), Screen.width - 56))), message, style);
                GUI.EndScrollView();
                if (confirmingRetreat && GUI.Button(new Rect(16, 186, 210, 28), L.T("Confirm retreat and loss")))
                {
                    controller.InterfaceBlocked = false;
                    controller.TryRetreat(true);
                    confirmingRetreat = false;
                }
                if (GUI.Button(new Rect(Screen.width - 126, 186, 110, 28), L.T("Back to battle")))
                { confirmingRetreat = showingLog = false; controller.InterfaceBlocked = false; }
                return;
            }
            if (run.Mission.RequiresRelic) DrawMarker(run.Chest, run.ChestOpened ? L.T("EMPTY") : L.T("RELIC"));
            if (run.MissionTarget != null && run.MissionTarget.IsAlive) DrawMarker(run.MissionTarget.Position, L.T("TARGET"));
            DrawMarker(run.Extraction, L.T("EXIT"));
            foreach (var body in run.Bodies) DrawMarker(body.Position, body.Recoverable ? L.T("BODY") : L.T("LOST"));
            if (confirmingAbandonment)
            {
                GUI.Box(new Rect(16, 166, Screen.width - 32, 60), L.T("Unrecovered bodies will be permanently lost."));
                if (GUI.Button(new Rect(16, 194, 220, 28), L.T("Confirm permanent loss")))
                { controller.InterfaceBlocked = false; controller.TryExtract(true); confirmingAbandonment = false; }
                if (GUI.Button(new Rect(244, 194, 100, 28), L.T("Cancel")))
                { confirmingAbandonment = false; controller.InterfaceBlocked = false; }
                return;
            }
            string goal = run.MissionCompleted ? L.T("Objective complete. Reach EXIT to extract.") :
                run.Mission.Type == MissionType.EliminateTarget ? L.T("Find and defeat TARGET, then return to EXIT.") :
                run.Mission.Type == MissionType.ClearArea ? L.F("Clear area: {0} enemies remaining", run.RemainingEnemies) : !run.ChestOpened ? L.T("Find RELIC. Take it from the same or an adjacent visible hex (1 action).") :
                controller.Outcome == BattleOutcome.Victory ? L.T("Area cleared. Bring one survivor to EXIT to extract the party.") :
                L.T("Loot collected. Defeat remaining enemies, then return to EXIT.");
            GUI.Label(new Rect(16, 168, Screen.width - 32, 26), goal);
            bool previous = GUI.enabled;
            GUI.enabled = previous && controller.CanPlayerAct && run.CanOpenChest(controller.SelectedUnit);
            if (run.Mission.RequiresRelic && GUI.Button(new Rect(16, 198, 120, 28), new GUIContent(L.T("Take relic"), L.T("Stand next to the visible chest with one action remaining.")))) controller.TryOpenChest();
            GUI.enabled = previous && controller.CanPlayerAct && run.CanExtract(controller.SelectedUnit);
            if (GUI.Button(new Rect(144, 198, 120, 28), new GUIContent(L.T("Extract party"), L.T("Complete the mission, then stand on EXIT."))))
            {
                if (run.HasUnrecoverableBodies) { confirmingAbandonment = true; controller.InterfaceBlocked = true; }
                else controller.TryExtract();
            }
            GUI.enabled = previous;
            GUI.Label(new Rect(274, 200, Screen.width - 290, 24),
                L.F("Loot: {0} gold / {1} items", run.CollectedGold, run.CollectedItems.Count));
        }

        private string RetreatMessage(ExpeditionRun run)
        {
            var preview = run.PreviewRetreat(controller.SelectedUnit);
            if (preview == null) return L.T("Retreat requires the active hero on EXIT. No action is required.");
            var text = new StringBuilder(L.T("Retreat without the mission reward? All living heroes escape with their wounds, equipment and remaining potions. Bodies return only from reachable ground after all enemies are defeated."));
            text.Append("\n").Append(L.F("Loot forfeited: {0} gold / {1} items. Mission reward: none. Survivors gain 25 XP.", run.CollectedGold, run.CollectedItems.Count));
            foreach (var hero in preview.Adventurers)
                text.Append("\n").Append(L.AdventurerName(hero.InstanceId)).Append(hero.Survived ? L.F(": {0}/{1} HP", hero.Health, hero.MaxHealth) :
                    hero.BodyRecovered ? L.T(": DEAD — body recovered") : L.T(": PERMANENTLY LOST"));
            return text.ToString();
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
                L.T("EXPEDITION COMPLETE") : result.Outcome == ExpeditionOutcome.Retreated ? L.T("PARTY RETREATED") : L.T("EXPEDITION LOST"));
            text.Append(L.T("\nGold recovered: ")).Append(result.Gold);
            if (bootstrap != null && bootstrap.DebugMode) text.Append(L.T(" | Seed: ")).Append(result.Seed);
            foreach (var item in result.Items)
            {
                text.Append("\n").Append(L.T(item.Name)).Append(" — ");
                if (item.Category == ItemCategory.Weapon)
                    text.Append(L.T("weapon, d")).Append(item.DamageDie).Append(L.T(", attack +")).Append(item.AttackBonus);
                else if (item.Category == ItemCategory.Armor) text.Append(L.T("armor, defense +")).Append(item.DefenseBonus);
                else text.Append(L.T("healing consumable, ")).Append(item.Healing).Append(L.T(" HP"));
            }
            foreach (var unit in result.Adventurers)
                text.Append("\n").Append(L.AdventurerName(unit.InstanceId)).Append(unit.Survived ? L.F(": {0}/{1} HP", unit.Health, unit.MaxHealth) :
                    unit.BodyRecovered ? L.T(": DEAD — body recovered") : L.T(": PERMANENTLY LOST"));
            text.Append(L.T("\nRecovered bodies can be resurrected in the guild for 30 gold."));
            text.Append("\n").Append(L.T("Survivors gain 100 XP on extraction, 25 on retreat. Dead heroes gain none; resurrection keeps training."));
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24), L.T("Expedition result"));
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            float height = style.CalcHeight(new GUIContent(text.ToString()), Screen.width - 80) + 12;
            resultScroll = GUI.BeginScrollView(new Rect(28, 44, Screen.width - 56, Screen.height - 114), resultScroll,
                new Rect(0, 0, Screen.width - 80, height));
            GUI.Label(new Rect(0, 0, Screen.width - 80, height), text.ToString(), style);
            GUI.EndScrollView();
            if (bootstrap != null && GUI.Button(new Rect(28, Screen.height - 58, 220, 32), L.T("Return to guild"))) bootstrap.TryReturnToGuild();
        }
    }
}
