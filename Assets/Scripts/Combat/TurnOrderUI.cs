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
            if (controller == null || controller.Turns == null) return;
            var turns = controller.Turns;
            var text = new StringBuilder("Round " + turns.Round + "  |  ");
            foreach (var unit in turns.Order)
            {
                if (unit == turns.ActiveUnit) text.Append("> ");
                text.Append(unit.Definition.DisplayName).Append(" (")
                    .Append(unit.Definition.Initiative).Append(")  ");
            }
            GUI.Label(new Rect(24, 112, Screen.width - 48, 40), text.ToString());
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && controller.isActiveAndEnabled &&
                turns.State == TurnState.SelectingAction;
            if (GUI.Button(new Rect(24, 158, 140, 28), "End turn")) controller.TryEndTurn();
            GUI.enabled = GUI.enabled && turns.ActionAvailable && turns.State == TurnState.SelectingAction;
            if (GUI.Button(new Rect(176, 158, 180, 28), "Wait (test action)")) controller.TryWaitAction();
            GUI.enabled = previousEnabled;
        }
    }
}
