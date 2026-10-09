using System.Text;
using System.Linq;
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
                L.T(Generation.Biomes.Name(journey.DungeonConfig.Biome)) + " · " +
                (journey.IsDefeated ? L.T("The expedition is lost.") : L.F("Section {0}/{1} complete", journey.Completed, journey.Sections)));
            var text = new StringBuilder(L.F("Carried loot: {0} gold / {1} items", result.Gold, result.Items.Count));
            if (!journey.IsDefeated) text.Append("\n\n").Append(L.T("Return now with all carried loot, or continue without healing or new supplies. Carried bodies remain dead. A total defeat loses everything carried."));
            text.Append("\n\n").Append(L.T(bootstrap.BoundarySaved ?
                "This boundary is saved. Closing the game in the next section restores this choice and rolls back that section. Before the first boundary, the pre-departure guild is restored." :
                "Checkpoint not saved. Retry saving before continuing. Closing may restore the previous boundary."));
            if (!journey.IsDefeated) text.Append("\n\n").Append(L.T("Retreat in a later section keeps earlier rewards and carried bodies, but forfeits that section's reward. New bodies require full clearance and a path to EXIT."));
            foreach (var hero in result.Adventurers)
                text.Append("\n").Append(L.AdventurerName(hero.InstanceId)).Append(hero.Survived ? L.F(": {0}/{1} HP", hero.Health, hero.MaxHealth) :
                    hero.BodyRecovered ? L.T(": DEAD — body recovered") : L.T(": PERMANENTLY LOST"));
            if (!journey.IsDefeated) text.Append("\n").Append(L.T("Experience is awarded once on return to the guild."));
            if (!string.IsNullOrEmpty(bootstrap.SaveMessage)) text.Append("\n\n").Append(L.SaveMessage(bootstrap.SaveMessage));
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            GUILayout.BeginArea(new Rect(28, 44, Screen.width - 56, Screen.height - 186));
            scroll = GUILayout.BeginScrollView(scroll);
            if (journey.EventPending)
            {
                var definition = journey.CurrentEvent;
                GUILayout.Label(L.T("Exploration event") + ": " + L.T(definition.Name), style);
                GUILayout.Label(L.T(definition.Description), style);
                GUILayout.Label(L.F("Roll d20 >= {0}. Success: +{1} gold, heal {2} HP, take {3} damage. Failure: take {4} damage.",
                    definition.Difficulty, definition.Gold, definition.Healing, definition.SuccessDamage, definition.FailureDamage), style);
                GUILayout.Label(L.T("Choose a living hero to investigate, or leave safely. Damage can kill; survivors carry the body. If the last hero dies, all loot and bodies are lost."), style);
                bool previous = GUI.enabled;
                GUI.enabled = previous && bootstrap.BoundarySaved;
                foreach (var hero in result.Adventurers.Where(h => h.Survived))
                    if (GUILayout.Button(L.F("Investigate with {0} ({1}/{2} HP)", L.AdventurerName(hero.InstanceId), hero.Health, hero.MaxHealth), GUILayout.MinHeight(30)))
                        bootstrap.TryResolveExplorationEvent(1, hero.InstanceId);
                if (GUILayout.Button(L.T("Leave safely"), GUILayout.MinHeight(30))) bootstrap.TryResolveExplorationEvent(0);
                GUI.enabled = previous;
                GUILayout.Space(12);
            }
            else if (journey.LastEvent != null)
            {
                var outcome = journey.LastEvent;
                GUILayout.Label(L.T("Exploration event") + ": " + L.T(journey.CurrentEvent.Name), style);
                GUILayout.Label(outcome.choice == 0 ? L.T("Left safely. No roll or effects.") :
                    L.F("d20({0}) >= {1}: {2}. {3}: HP {4} -> {5}; +{6} gold.", outcome.roll, journey.CurrentEvent.Difficulty,
                        L.T(outcome.roll >= journey.CurrentEvent.Difficulty ? "SUCCESS" : "FAILURE"),
                        L.AdventurerName(outcome.hero), outcome.healthBefore, outcome.healthAfter, outcome.gold), style);
                if (bootstrap.BoundarySaved)
                    GUILayout.Label(L.T("This choice and its effects are saved with the boundary; loading cannot apply them again."), style);
                GUILayout.Space(12);
            }
            GUILayout.Label(text.ToString(), style);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            bool returnEnabled = GUI.enabled;
            GUI.enabled = returnEnabled && !journey.EventPending;
            if (GUI.Button(new Rect(28, Screen.height - 96, Screen.width - 56, 30), L.T("Return to guild")))
                bootstrap.TryReturnToGuild();
            GUI.enabled = returnEnabled;
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
