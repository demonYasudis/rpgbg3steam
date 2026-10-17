using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
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
        private string shortText;
        private readonly Queue<string> history = new Queue<string>();
        public int Revision { get; private set; }
        public IReadOnlyList<string> History => history.Select(Core.Localization.CombatMessage).ToArray();
        public AttackResult LastResult { get; private set; }
        public bool IsShowing => remaining > 0;
        public string LastMessage => Core.Localization.CombatMessage(text);

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
            shortText = "d20(" + result.AttackRoll + ") " + (result.Hit ? "HIT · -" + result.AppliedDamage + " HP" : "MISS") + (result.Killed ? " · DEAD" : "");
            Record();
        }

        public void Show(AbilityResult result, Vector3 worldPosition)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var message = new StringBuilder(result.Ability.Name);
            if (result.FellIntoPit) message.Append(" / PIT / DEAD");
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
            if (result.Attacks.Count > 0)
                shortText = result.Ability.Name + "\n" + string.Join("\n", result.Attacks.Select(a => "d20(" + a.AttackRoll + ") " + (a.Hit ? "HIT · -" + a.AppliedDamage + " HP" : "MISS") + (a.Killed ? " · DEAD" : "")));
        }

        public void ShowMessage(string message, Vector3 worldPosition)
        {
            LastResult = null;
            text = message;
            shortText = message;
            position = worldPosition;
            remaining = DisplaySeconds;
            boxHeight = Math.Max(52, 22 + message.Split('\n').Length * 18);
            Record();
        }

        private void Record() { history.Enqueue(text); while (history.Count > 40) history.Dequeue(); unchecked { Revision++; } }

        private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

        private void Update() => remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);

        private void OnGUI()
        {
            if (!IsShowing || worldCamera == null) return;
            Vector3 screen = worldCamera.WorldToScreenPoint(position);
            if (screen.z <= 0) return;
            float age = DisplaySeconds - remaining;
            if (age < 0.3f)
            {
                var previous = GUI.color;
                GUI.color = LastResult != null && LastResult.Killed ? new Color(0.8f, 0.3f, 0.25f, 1 - age / 0.3f) :
                    new Color(1f, 0.8f, 0.45f, 1 - age / 0.3f);
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI / 2;
                    GUI.DrawTexture(new Rect(Mathf.Round(screen.x + Mathf.Cos(angle) * (5 + age * 36)),
                        Mathf.Round(Screen.height - screen.y + Mathf.Sin(angle) * (5 + age * 36)), 3, 3), Texture2D.whiteTexture);
                }
                GUI.color = previous;
            }
            bool hud = UI.UiInputGate.UsesBattleHud(this);
            var area = hud ? GetComponent<HexGrid.HexGridInteraction>().MapScreenRect : new Rect(0, 0, Screen.width, Screen.height);
            string message = hud ? Core.Localization.CombatMessage(shortText) : LastMessage;
            float width = Mathf.Min(hud ? 290 : 470, area.width);
            var style = new GUIStyle(GUI.skin.box) { wordWrap = true };
            float height = Mathf.Max(hud ? 42 : boxHeight, style.CalcHeight(new GUIContent(message), width));
            float x = Mathf.Clamp(screen.x - width * 0.5f, area.xMin, Mathf.Max(area.xMin, area.xMax - width));
            float y = Mathf.Clamp(Screen.height - screen.y - height - 18 - 25 * (1 - remaining / DisplaySeconds),
                Screen.height - area.yMax, Mathf.Max(Screen.height - area.yMax, Screen.height - area.yMin - height));
            GUI.Box(new Rect(x, y, width, height), message, style);
        }
    }
}
