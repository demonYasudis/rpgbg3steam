using L = GuildTactics.Core.Localization;
using System;
using System.Text;
using GuildTactics.Units;
using UnityEngine;

namespace GuildTactics.Combat
{
    /// <summary>Temporary IMGUI turn readout; the controller owns all commands.</summary>
    public sealed class TurnOrderUI : MonoBehaviour
    {
        private PlayerUnitController controller;

        public void Initialize(PlayerUnitController unitController)
        {
            controller = unitController != null ? unitController :
                throw new ArgumentNullException(nameof(unitController));
        }

        private void OnGUI()
        {
            if (controller == null || controller.Turns == null || controller.Expedition?.Result != null) return;
            var turns = controller.Turns;
            if (controller.Outcome != BattleOutcome.Ongoing && controller.Expedition == null)
            {
                GUI.Label(new Rect(24, 112, Screen.width - 48, 40),
                    controller.Outcome == BattleOutcome.Victory ? L.T("VICTORY - All enemies defeated") : L.T("DEFEAT - The party has fallen"));
                return;
            }
            var text = new StringBuilder(L.T("Round ") + turns.Round + "  |  ");
            if (turns.ActiveUnit != null && !controller.IsUnitVisible(turns.ActiveUnit)) text.Append(L.T("Enemy turn  |  "));
            foreach (var unit in turns.Order)
            {
                if (!controller.IsUnitVisible(unit)) continue;
                if (unit == turns.ActiveUnit) text.Append("> ");
                text.Append(L.T(unit.Definition.DisplayName)).Append(" (")
                    .Append(unit.Definition.Initiative).Append(")  ");
            }
            var active = turns.ActiveUnit;
            string title = active == null ? L.T("Preparing next turn") : active.Team == UnitTeam.Enemy ? L.T("ENEMY TURN — please wait") :
                L.T("YOUR TURN — ") + L.T(active.Definition.DisplayName);
            GUI.Label(new Rect(16, 6, Screen.width - 170, 26), new GUIContent(title + L.T("  |  Round ") + turns.Round, text.ToString()),
                new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
            bool previousEnabled = GUI.enabled;
            if (controller.InterfaceBlocked) return;
            GUI.enabled = previousEnabled && controller.CanPlayerAct;
            if (GUI.Button(new Rect(16, 60, 110, 28), new GUIContent(L.T("End turn"), L.T("Finish this hero's turn, even if movement or action remain.")))) controller.TryEndTurn();
            GUI.enabled = GUI.enabled && turns.ActionAvailable && turns.State == TurnState.SelectingAction;
            if (GUI.Button(new Rect(134, 60, 110, 28), new GUIContent(L.T("Wait"), L.T("Spend your action without attacking. You may still move.")))) controller.TryWaitAction();
            GUI.enabled = previousEnabled;
            GUI.Label(new Rect(254, 61, Screen.width - 270, 26), new GUIContent(
                L.T(controller.BossAttack?.Pending == true ? "Red hexes: 10 damage next boss turn." : "Gold hex: active hero · Green: move · Purple: target"),
                controller.BossAttack?.Pending == true ? L.T("Cinder burst armed: leave red hexes before the next boss turn.") : ""));
        }
    }
}
