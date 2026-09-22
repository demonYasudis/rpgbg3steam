using System;
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
            text = $"d20({result.AttackRoll}) {Signed(result.AttackBonus)} = {result.AttackTotal} / DEF {result.Defense}: " +
                (result.Hit ? "HIT" : "MISS");
            if (result.Hit)
                text += $"\nd{result.DamageDie}({result.DamageRoll}) {Signed(result.DamageBonus)} = {result.Damage} | -{result.AppliedDamage} HP";
            if (result.Killed) text += " | DEAD";
        }

        private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

        private void Update() => remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);

        private void OnGUI()
        {
            GUI.Label(new Rect(24, 192, Screen.width - 48, 24), $"Combat seed: {seed} | Click adjacent enemy to attack.");
            if (!IsShowing || worldCamera == null) return;
            Vector3 screen = worldCamera.WorldToScreenPoint(position);
            if (screen.z <= 0) return;
            const float width = 350;
            float x = Mathf.Clamp(screen.x - width * 0.5f, 0, Mathf.Max(0, Screen.width - width));
            float y = Mathf.Max(0, Screen.height - screen.y - 70 - 25 * (1 - remaining / DisplaySeconds));
            GUI.Box(new Rect(x, y, width, 52), text);
        }
    }
}
