using System;
using GuildTactics.Units;
using UnityEngine;

namespace GuildTactics.Abilities
{
    /// <summary>Temporary action bar; all permissions and targeting come from gameplay code.</summary>
    public sealed class ActionBarUI : MonoBehaviour
    {
        private PlayerUnitController controller;
        public void Initialize(PlayerUnitController value) => controller = value != null ? value :
            throw new ArgumentNullException(nameof(value));

        private void OnGUI()
        {
            if (controller == null || controller.Expedition?.Result != null || !controller.IsUnitVisible(controller.SelectedUnit)) return;
            bool previous = GUI.enabled;
            float width = Mathf.Max(40, (Screen.width - 60) / 4f);
            GUI.enabled = previous && controller.CanPlayerAct;
            if (GUI.Button(new Rect(24, 222, width, 28), "Move / cancel")) controller.CancelTargeting();
            GUI.enabled = GUI.enabled && controller.Turns.ActionAvailable;
            if (GUI.Button(new Rect(28 + width, 222, width, 28),
                controller.IsTargetingAttack ? "> Basic attack" : "Basic attack")) controller.SelectBasicAttack();
            var unit = controller.SelectedUnit;
            for (int i = 0; i < unit.Definition.Abilities.Count; i++)
            {
                var ability = unit.Definition.Abilities[i];
                string label = (controller.SelectedAbility == ability ? "> " : "") + ability.Name;
                if (GUI.Button(new Rect(32 + 2 * width + i * (width + 4), 222, width, 28), label))
                    controller.SelectAbility(ability);
            }
            GUI.enabled = previous;
            GUI.Label(new Rect(24, 254, Screen.width - 48, 42), controller.ActionHint);
        }
    }
}
