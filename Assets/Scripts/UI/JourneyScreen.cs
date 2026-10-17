using System;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEngine;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    /// <summary>Events, section boundaries and results. The existing model owns every effect and payout.</summary>
    public sealed class JourneyScreen : MonoBehaviour
    {
        private GameBootstrap owner;
        private UiInputGate gate;
        private UIDocument document;
        private PanelSettings settings;
        private VisualElement shell;
        private enum Confirmation { None, Retreat, Abandonment }
        private Confirmation confirmation;
        private PlayerUnitController confirmedController;
        private bool legacy, dirty, pending;
        private string focusName, failure;
        private int signature, escapeFrame = -1;
        private Vector2 scrollPosition;
        private object context;
        public bool IsOpen => shell != null;
        public bool UsesNewPresentation => !legacy && isActiveAndEnabled && owner != null;
        public VisualElement ScreenRoot => shell;

        public void Initialize(GameBootstrap bootstrap)
        {
            owner = bootstrap; gate = owner.GetComponent<UiInputGate>(); legacy = Application.isBatchMode;
            settings = Instantiate(Resources.Load<PanelSettings>("UI/FoundationPanel")); settings.sortingOrder = 100;
            document = gameObject.AddComponent<UIDocument>(); document.panelSettings = settings;
            var root = document.rootVisualElement;
            root.style.position = Position.Absolute; root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.display = DisplayStyle.None;
        }
        public void Show() { legacy = false; dirty = true; }
        public void ShowLegacy() { legacy = true; confirmation = Confirmation.None; Hide(); }
        public void ConfirmRetreat(PlayerUnitController controller) => Confirm(controller, Confirmation.Retreat);
        public void ConfirmAbandonment(PlayerUnitController controller) => Confirm(controller, Confirmation.Abandonment);
        private void Confirm(PlayerUnitController controller, Confirmation value)
        {
            if (!UsesNewPresentation || controller != owner.ActiveController || !controller.CanPlayerAct) return;
            if (value == Confirmation.Retreat ? !controller.Expedition.CanRetreat(controller.SelectedUnit) :
                !controller.Expedition.CanExtract(controller.SelectedUnit) || !controller.Expedition.HasUnrecoverableBodies) return;
            confirmedController = controller; confirmation = value; failure = null; dirty = true;
            gate.JourneyOpen = true;
        }
        private string Text(string en, string ru) => L.Language == InterfaceLanguage.Russian ? ru : en;
        private void LateUpdate()
        {
            if (owner == null) return;
            if (confirmation != Confirmation.None && (confirmedController != owner.ActiveController || confirmedController.Expedition.Result != null))
                confirmation = Confirmation.None;
            if (!UsesNewPresentation || !owner.Guild.IsAway || gate.PreviewOpen || gate.ModalOpen ||
                (confirmation == Confirmation.None && owner.JourneyBoundary == null && owner.ActiveController?.Expedition.Result == null))
            { Hide(); return; }
            owner.CaptureJourneyBoundary();
            object nextContext = (object)owner.JourneyBoundary ?? owner.ActiveController?.Expedition.Result ?? (object)confirmedController;
            if (!ReferenceEquals(context, nextContext)) { context = nextContext; scrollPosition = Vector2.zero; failure = null; dirty = true; }
            int next = HashCode.Combine(nextContext, (int)confirmation, (int)L.Language, owner.BoundarySaved, owner.SaveMessage, owner.CanContinueJourney);
            if (!IsOpen || dirty || signature != next) { Build(); signature = next; }
            var focused = document.rootVisualElement.focusController?.focusedElement as VisualElement;
            if (Input.GetKeyDown(KeyCode.Escape) && (focused == null || !shell.Contains(focused))) Escape();
        }
        private void Hide()
        {
            if (!IsOpen) { if (gate != null) gate.JourneyOpen = false; return; }
            document.rootVisualElement.Clear(); document.rootVisualElement.style.display = DisplayStyle.None;
            shell = null; gate.JourneyOpen = false; gate.ConsumeClosingFrame();
        }
        private void Escape()
        {
            if (escapeFrame == Time.frameCount) return; escapeFrame = Time.frameCount;
            if (confirmation == Confirmation.None) return; // Results/events require an explicit choice.
            confirmation = Confirmation.None; Hide();
        }
        private Label Label(VisualElement parent, string name, string text, string css = null)
        {
            var label = new Label(text) { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList("journey-label"); if (css != null) label.AddToClassList(css); parent.Add(label); return label;
        }
        private Button Button(VisualElement parent, string name, string title, Func<bool> command, bool enabled = true)
        {
            var page = shell; var controller = owner.ActiveController; var boundary = owner.JourneyBoundary;
            var result = controller?.Expedition.Result;
            var button = UiComponents.Button(title, () =>
            {
                if (pending || !ReferenceEquals(shell, page) || controller != owner.ActiveController || boundary != owner.JourneyBoundary || result != controller?.Expedition.Result) return;
                pending = true;
                failure = null;
                if (!command()) failure = Text("The command is no longer available. Check the current state.", "Команда уже недоступна. Проверьте текущее состояние.");
                dirty = true; focusName = name; gate.ConsumeClosingFrame();
                foreach (var item in page.Query<Button>().ToList()) item.SetEnabled(false);
            });
            button.name = name; button.SetEnabled(enabled); parent.Add(button); return button;
        }
        private void Build()
        {
            var oldScroll = shell?.Q<ScrollView>("journey-scroll"); if (oldScroll != null) scrollPosition = oldScroll.scrollOffset;
            string focused = focusName ?? (document.rootVisualElement.focusController?.focusedElement as VisualElement)?.name;
            focusName = null; dirty = pending = false; gate.JourneyOpen = true;
            var root = document.rootVisualElement; root.Clear(); root.styleSheets.Clear();
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Foundation")); root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Journey"));
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); root.style.display = DisplayStyle.Flex;
            shell = new VisualElement { name = "journey-screen" }; shell.AddToClassList("journey-screen"); root.Add(shell);
            shell.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Escape(); evt.StopImmediatePropagation(); } }, TrickleDown.TrickleDown);
            var panel = new VisualElement(); panel.AddToClassList("journey-panel"); shell.Add(panel);
            var header = new VisualElement(); header.AddToClassList("journey-row"); panel.Add(header);
            bool boundary = owner.JourneyBoundary != null;
            var result = owner.JourneyBoundary ?? owner.ActiveController?.Expedition.Result;
            string title = confirmation != Confirmation.None ? L.T(confirmation == Confirmation.Retreat ? "Confirm retreat and loss" : "Confirm permanent loss") :
                owner.Journey?.EventPending == true ? L.T("Exploration event") : result.Outcome == ExpeditionOutcome.Defeated ? L.T("EXPEDITION LOST") :
                result.Outcome == ExpeditionOutcome.Retreated ? L.T("PARTY RETREATED") : boundary && owner.Journey.Completed < owner.Journey.Sections ?
                    L.F("Section {0}/{1} complete", owner.Journey.Completed, owner.Journey.Sections) : L.T("EXPEDITION COMPLETE");
            Label(header, "journey-title", title, "journey-title");
            Button(header, "journey-ru", "RU", () => { L.SetLanguage(InterfaceLanguage.Russian, !Application.isBatchMode); return true; });
            Button(header, "journey-en", "EN", () => { L.SetLanguage(InterfaceLanguage.English, !Application.isBatchMode); return true; });
            if (owner.DebugMode) Button(header, "journey-legacy", Text("Legacy UI", "Старый UI"), () => { ShowLegacy(); return true; });
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "journey-scroll" }; scroll.AddToClassList("journey-scroll"); panel.Add(scroll);
            var journey = owner.Journey;
            if (journey != null)
            {
                var illustration = new ExpeditionIllustration(journey.DungeonConfig.Biome, ExpeditionSelection.Offers[journey.OfferIndex].Mission.Type);
                illustration.AddToClassList("journey-illustration"); scroll.Add(illustration);
                Label(scroll, "journey-location", L.T(Generation.Biomes.Name(journey.DungeonConfig.Biome)), "journey-subtitle");
            }
            if (confirmation != Confirmation.None) BuildConfirmation(scroll);
            else
            {
                if (boundary && journey.EventPending) BuildEvent(scroll);
                BuildResult(scroll, result);
                if (boundary && !journey.EventPending) BuildEvent(scroll);
                if (boundary && !journey.IsDefeated && journey.Completed < journey.Sections)
                {
                    Label(scroll, "journey-risk", L.T("Return now with all carried loot, or continue without healing or new supplies. Carried bodies remain dead. A total defeat loses everything carried."));
                    Label(scroll, "journey-checkpoint", L.T(owner.BoundarySaved ?
                        "This boundary is saved. Closing the game in the next section restores this choice and rolls back that section. Before the first boundary, the pre-departure guild is restored." :
                        "Checkpoint not saved. Retry saving before continuing. Closing may restore the previous boundary."));
                }
            }
            if (!string.IsNullOrEmpty(owner.SaveMessage)) Label(panel, "journey-save-error", L.SaveMessage(owner.SaveMessage), "journey-error");
            if (failure != null) Label(panel, "journey-command-error", failure, "journey-error");
            var footer = new VisualElement(); footer.AddToClassList("journey-row"); panel.Add(footer);
            if (confirmation != Confirmation.None)
            {
                Button(footer, "journey-cancel", L.T("Back to battle"), () => { confirmation = Confirmation.None; Hide(); return true; });
                Button(footer, "journey-confirm", L.T(confirmation == Confirmation.Retreat ? "Confirm retreat and loss" : "Confirm permanent loss"), CommitConfirmation);
            }
            else
            {
                if (boundary && !owner.BoundarySaved) Button(footer, "journey-retry", L.T("Retry checkpoint save"), owner.RetryJourneySave);
                Button(footer, "journey-return", L.T("Return to guild"), owner.TryReturnToGuild, journey?.EventPending != true);
                if (boundary && journey.Completed < journey.Sections)
                    Button(footer, "journey-continue", L.T("Continue to next section"), owner.TryContinueJourney, owner.CanContinueJourney);
            }
            scroll.schedule.Execute(() => { if (scroll.panel != null) scroll.scrollOffset = scrollPosition; });
            var focus = shell.Q<Button>(focused ?? "") ?? shell.Q<Button>(confirmation != Confirmation.None ? "journey-cancel" : "journey-return");
            if (focus == null || !focus.enabledInHierarchy) focus = shell.Query<Button>().ToList().FirstOrDefault(b => b.enabledInHierarchy);
            shell.schedule.Execute(() => { if (focus?.panel != null) focus.Focus(); });
        }
        private bool CommitConfirmation()
        {
            if (confirmedController != owner.ActiveController) return false;
            bool success;
            gate.JourneyOpen = false; // Synchronous command revalidation needs the idle player turn.
            try { success = confirmation == Confirmation.Retreat ? confirmedController.TryRetreat(true) : confirmedController.TryExtract(true); }
            finally { gate.JourneyOpen = true; }
            if (success) confirmation = Confirmation.None;
            return success;
        }
        private void BuildConfirmation(VisualElement parent)
        {
            var run = confirmedController.Expedition;
            if (confirmation == Confirmation.Retreat)
            {
                Label(parent, "journey-confirm-warning", L.T("Retreat without the mission reward? All living heroes escape with their wounds, equipment and remaining potions. Bodies return only from reachable ground after all enemies are defeated."));
                Label(parent, "journey-forfeited", L.F("Loot forfeited: {0} gold / {1} items. Mission reward: none. Survivors gain 25 XP.", run.CollectedGold, run.CollectedItems.Count));
                var preview = run.PreviewRetreat(confirmedController.SelectedUnit);
                if (preview != null) BuildResult(parent, preview);
            }
            else
            {
                Label(parent, "journey-confirm-warning", L.T("Unrecovered bodies will be permanently lost."), "journey-error");
                foreach (var body in run.Bodies)
                    Label(parent, "body-" + body.InstanceId, L.AdventurerName(body.InstanceId) + " · " + L.T(body.Recoverable ? ": DEAD — body recovered" : ": PERMANENTLY LOST"));
            }
        }
        private void BuildEvent(VisualElement parent)
        {
            var journey = owner.Journey; var definition = journey.CurrentEvent;
            if (definition == null) return;
            var panel = new VisualElement(); panel.AddToClassList("journey-event"); parent.Add(panel);
            Label(panel, "journey-event-name", L.T(definition.Name), "journey-subtitle");
            Label(panel, "journey-event-description", L.T(definition.Description));
            if (journey.EventPending)
            {
                Label(panel, "journey-event-rule", L.F("Roll d20 >= {0}. Success: +{1} gold, heal {2} HP, take {3} damage. Failure: take {4} damage.", definition.Difficulty, definition.Gold, definition.Healing, definition.SuccessDamage, definition.FailureDamage));
                Label(panel, "journey-event-warning", L.T("Choose a living hero to investigate, or leave safely. Damage can kill; survivors carry the body. If the last hero dies, all loot and bodies are lost."));
                var choices = new VisualElement(); choices.AddToClassList("journey-choices"); panel.Add(choices);
                foreach (var hero in owner.JourneyBoundary.Adventurers.Where(h => h.Survived))
                {
                    string id = hero.InstanceId;
                    Button(choices, "journey-investigate-" + id, L.F("Investigate with {0} ({1}/{2} HP)", L.AdventurerName(id), hero.Health, hero.MaxHealth), () => owner.TryResolveExplorationEvent(1, id), owner.BoundarySaved);
                }
                Button(choices, "journey-skip", L.T("Leave safely"), () => owner.TryResolveExplorationEvent(0), owner.BoundarySaved);
            }
            else if (journey.LastEvent != null)
            {
                var last = journey.LastEvent;
                Label(panel, "journey-event-outcome", last.choice == 0 ? L.T("Left safely. No roll or effects.") :
                    L.F("d20({0}) >= {1}: {2}. {3}: HP {4} -> {5}; +{6} gold.", last.roll, definition.Difficulty,
                        L.T(last.roll >= definition.Difficulty ? "SUCCESS" : "FAILURE"), L.AdventurerName(last.hero), last.healthBefore, last.healthAfter, last.gold));
                if (owner.BoundarySaved) Label(panel, "journey-event-saved", L.T("This choice and its effects are saved with the boundary; loading cannot apply them again."));
            }
        }
        private void BuildResult(VisualElement parent, ExpeditionResult result)
        {
            Label(parent, "journey-gold", Text("Gold recovered: ", "Золото к возвращению: ") + result.Gold, "journey-subtitle");
            var heroes = new VisualElement(); heroes.AddToClassList("journey-heroes"); parent.Add(heroes);
            foreach (var hero in result.Adventurers)
            {
                var card = new VisualElement { name = "result-hero-" + hero.InstanceId }; card.AddToClassList("journey-hero"); heroes.Add(card);
                card.EnableInClassList("journey-fallen", !hero.Survived);
                var portrait = new VisualElement(); portrait.AddToClassList("journey-portrait"); portrait.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("UI/Art/" + hero.DefinitionId)); card.Add(portrait);
                Label(card, "result-name-" + hero.InstanceId, L.AdventurerName(hero.InstanceId));
                Label(card, "result-status-" + hero.InstanceId, hero.Survived ? "HP " + hero.Health + "/" + hero.MaxHealth :
                    L.T(hero.BodyRecovered ? ": DEAD — body recovered" : ": PERMANENTLY LOST").TrimStart(':', ' '));
                var original = owner.Guild.Roster.First(h => h.Id == hero.InstanceId);
                int xp = !hero.Survived ? 0 : result.Outcome == ExpeditionOutcome.Extracted ? HeroProgression.ExtractionExperience : result.Outcome == ExpeditionOutcome.Retreated ? HeroProgression.RetreatExperience : 0;
                xp = Math.Min(xp, HeroProgression.MaximumExperience - original.Experience);
                Label(card, "result-xp-" + hero.InstanceId, Text("On return: +", "При возвращении: +") + xp + Text(" XP", " опыта"));
            }
            Label(parent, "journey-xp-note", L.T("Experience is awarded once on return to the guild."));
            var loot = new VisualElement(); loot.AddToClassList("journey-loot"); parent.Add(loot);
            if (result.Items.Count == 0) Label(loot, "journey-empty-loot", Text("No items recovered", "Предметы не получены"));
            foreach (var group in result.Items.GroupBy(i => i.Id))
            {
                var item = group.First(); var card = new VisualElement { name = "result-item-" + item.Id }; card.AddToClassList("journey-item"); loot.Add(card);
                var icon = new ItemIcon(item.Category); icon.AddToClassList("journey-item-icon"); card.Add(icon);
                Label(card, "item-name-" + item.Id, L.T(item.Name) + " × " + group.Count());
            }
        }
        internal void SetValidationTarget(RenderTexture target) => settings.targetTexture = target;
        private void OnDisable() { confirmation = Confirmation.None; Hide(); }
        private void OnDestroy() { Hide(); if (document != null) Destroy(document); if (settings != null) Destroy(settings); }
    }
}
