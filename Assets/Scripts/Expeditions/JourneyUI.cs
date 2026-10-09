using System.Text;
using GuildTactics.Core;
using UnityEngine;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.Expeditions
{
    public sealed class JourneyUI : MonoBehaviour
    {
        private GameBootstrap bootstrap;
        private Vector2 scroll;
        public void Initialize(GameBootstrap owner) => bootstrap = owner;

        private void OnGUI()
        {
            var result = bootstrap?.JourneyBoundary;
            if (result == null) return;
            int previousDepth = GUI.depth;
            GUI.depth = -20;
            var journey = bootstrap.Journey;
            GUI.Box(new Rect(12, 12, Screen.width - 24, Screen.height - 24),
                L.F("Section {0}/{1} complete", journey.Completed, journey.Sections));
            var text = new StringBuilder(L.F("Carried loot: {0} gold / {1} items", result.Gold, result.Items.Count));
            text.Append("\n\n").Append(L.T("Return now with all carried loot, or continue without healing or new supplies. Carried bodies remain dead. A total defeat loses everything carried."));
            text.Append("\n\n").Append(L.T(bootstrap.BoundarySaved ?
                "This boundary is saved. Closing the game in the next section restores this choice and rolls back that section. Before the first boundary, the pre-departure guild is restored." :
                "Checkpoint not saved. Retry saving or return to the guild before closing the game."));
            text.Append("\n\n").Append(L.T("Retreat in a later section keeps earlier rewards and carried bodies, but forfeits that section's reward. New bodies require full clearance and a path to EXIT."));
            foreach (var hero in result.Adventurers)
                text.Append("\n").Append(L.AdventurerName(hero.InstanceId)).Append(hero.Survived ? L.F(": {0}/{1} HP", hero.Health, hero.MaxHealth) :
                    hero.BodyRecovered ? L.T(": DEAD — body recovered") : L.T(": PERMANENTLY LOST"));
            text.Append("\n").Append(L.T("Experience is awarded once on return to the guild."));
            if (!string.IsNullOrEmpty(bootstrap.SaveMessage)) text.Append("\n\n").Append(L.SaveMessage(bootstrap.SaveMessage));
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            float width = Mathf.Max(120, Screen.width - 80);
            float height = style.CalcHeight(new GUIContent(text.ToString()), width) + 12;
            scroll = GUI.BeginScrollView(new Rect(28, 44, Screen.width - 56, Screen.height - 186), scroll,
                new Rect(0, 0, width, height));
            GUI.Label(new Rect(0, 0, width, height), text.ToString(), style);
            GUI.EndScrollView();
            if (GUI.Button(new Rect(28, Screen.height - 96, Screen.width - 56, 30), L.T("Return to guild")))
                bootstrap.TryReturnToGuild();
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && bootstrap.CanContinueJourney;
            if (journey.Completed < journey.Sections &&
                GUI.Button(new Rect(28, Screen.height - 60, Screen.width - 56, 30), L.T("Continue to next section")))
                bootstrap.TryContinueJourney();
            GUI.enabled = enabledBefore;
            if (bootstrap.JourneyBoundary != null && !bootstrap.BoundarySaved &&
                GUI.Button(new Rect(28, Screen.height - 132, Screen.width - 56, 30), L.T("Retry checkpoint save")))
                bootstrap.RetryJourneySave();
            GUI.depth = previousDepth;
        }
    }
}
