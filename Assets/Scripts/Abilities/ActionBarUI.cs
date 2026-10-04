using L = GuildTactics.Core.Localization;
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
            if (controller == null || controller.InterfaceBlocked || controller.Expedition?.Result != null || !controller.IsUnitVisible(controller.SelectedUnit)) return;
            bool previous = GUI.enabled;
            var buttonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = Screen.width < 900 ? 10 : 12 };
            float width = Mathf.Max(40, (Screen.width - 48) / 5f);
            GUI.enabled = previous && controller.CanPlayerAct;
            if (GUI.Button(new Rect(16, 96, width, 28), new GUIContent(L.T("Move / cancel"), L.T("Click a green hex to move. Cancel a selected attack or ability.")), buttonStyle)) controller.CancelTargeting();
            GUI.enabled = GUI.enabled && controller.Turns.ActionAvailable;
            if (GUI.Button(new Rect(20 + width, 96, width, 28), new GUIContent(
                controller.IsTargetingAttack ? L.T("> Basic attack") : L.T("Basic attack"),
                L.F("Range {0}. Roll d20 + attack against defense; costs one action.", controller.SelectedUnit.Definition.AttackRange)), buttonStyle)) controller.SelectBasicAttack();
            var unit = controller.SelectedUnit;
            for (int i = 0; i < unit.Definition.Abilities.Count; i++)
            {
                var ability = unit.Definition.Abilities[i];
                string label = (controller.SelectedAbility == ability ? "> " : "") + L.T(ability.Name);
                if (GUI.Button(new Rect(24 + 2 * width + i * (width + 4), 96, width, 28), new GUIContent(label, L.T(ability.Description)), buttonStyle))
                    controller.SelectAbility(ability);
            }
            GUI.enabled = previous && controller.CanPlayerAct && Combat.ConsumableSystem.CanHeal(controller.Turns, unit);
            if (GUI.Button(new Rect(32 + 4 * width, 96, width, 28), new GUIContent(
                L.F("Potion ({0})", unit.HealingPotions),
                L.T("Heal yourself up to 8 HP for one action. Needs a potion and missing health.")), buttonStyle))
                controller.TryUseHealingPotion();
            GUI.enabled = previous;
            GUI.Label(new Rect(16, 128, Screen.width - 32, 40),
                string.IsNullOrEmpty(GUI.tooltip) ? controller.ActionHint : GUI.tooltip,
                new GUIStyle(GUI.skin.label) { wordWrap = true });
        }
    }
}
