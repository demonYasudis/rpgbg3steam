using System;
using System.Text;
using GuildTactics.Abilities;
using UnityEngine;

namespace GuildTactics.Combat
{
    /// <summary>Temporary floating IMGUI feedback, independent of the target's lifetime.</summary>
    public sealed class CombatText : MonoBehaviour
    {
        public const float DisplaySeconds = 1.2f;
        private Camera worldCamera;
        private int seed;
        private Vector3 position;
        private float remaining;
        private string text;
        private float boxHeight = 52;
        public AttackResult LastResult { get; private set; }
        public bool IsShowing => remaining > 0;

        public void Initialize(Camera camera, int combatSeed)
        {
            worldCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            seed = combatSeed;
        }

        public void Show(AttackResult result, Vector3 worldPosition)
        {
            LastResult = result ?? throw new ArgumentNullException(nameof(result));
            position = worldPosition;
            remaining = DisplaySeconds;
            boxHeight = 52;
            text = $"d20({result.AttackRoll}) {Signed(result.AttackBonus)} = {result.AttackTotal} / DEF {result.Defense}: " +
                (result.Hit ? "HIT" : "MISS");
            if (result.Hit)
                text += $"\nd{result.DamageDie}({result.DamageRoll}) {Signed(result.DamageBonus)} = {result.Damage} | -{result.AppliedDamage} HP";
            if (result.Killed) text += " | DEAD";
        }

        public void Show(AbilityResult result, Vector3 worldPosition)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var message = new StringBuilder(result.Ability.Name);
            if (result.Attacks.Count == 0)
            {
                switch (result.Ability.Effect)
                {
                    case AbilityEffect.Evade: message.Append(" / DEF +").Append(result.Ability.Power); break;
                    case AbilityEffect.Trap: message.Append(" / ARMED: ").Append(result.Ability.Power).Append(" damage"); break;
                    default: message.Append(" / ").Append(result.Position); break;
                }
            }
            foreach (var attack in result.Attacks)
            {
                message.Append('\n').Append(attack.TargetPosition).Append(" d20(").Append(attack.AttackRoll)
                    .Append(") ").Append(Signed(attack.AttackBonus)).Append(attack.Hit ? " HIT" : " MISS");
                if (attack.Hit) message.Append(" / d").Append(attack.DamageDie).Append('(').Append(attack.DamageRoll)
                    .Append(") ").Append(Signed(attack.DamageBonus)).Append(" / -").Append(attack.AppliedDamage).Append(" HP");
                if (attack.Killed) message.Append(" DEAD");
            }
            if (result.TrapHit != null)
                message.Append("\nTRAP -").Append(result.TrapHit.Damage).Append(result.TrapHit.Killed ? " HP / DEAD" : " HP");
            ShowMessage(message.ToString(), worldPosition);
        }

        public void ShowMessage(string message, Vector3 worldPosition)
        {
            LastResult = null;
            text = message;
            position = worldPosition;
            remaining = DisplaySeconds;
            boxHeight = Math.Max(52, 22 + message.Split('\n').Length * 18);
        }

        private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

        private void Update() => remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);

        private void OnGUI()
        {
            GUI.Label(new Rect(24, 192, Screen.width - 48, 24), $"Combat seed: {seed} | Each attack / ability costs one action.");
            if (!IsShowing || worldCamera == null) return;
            Vector3 screen = worldCamera.WorldToScreenPoint(position);
            if (screen.z <= 0) return;
            float width = Mathf.Min(470, Screen.width);
            float x = Mathf.Clamp(screen.x - width * 0.5f, 0, Mathf.Max(0, Screen.width - width));
            float y = Mathf.Clamp(Screen.height - screen.y - boxHeight - 18 - 25 * (1 - remaining / DisplaySeconds),
                0, Mathf.Max(0, Screen.height - boxHeight));
            GUI.Box(new Rect(x, y, width, boxHeight), text);
        }
    }
}
