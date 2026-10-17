using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEngine;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    /// <summary>Battle presentation and inspection. All gameplay permissions stay in the controller.</summary>
    public sealed class BattleHud : MonoBehaviour
    {
        private GameBootstrap owner;
        private UiInputGate gate;
        private PlayerUnitController controller;
        private HexGridInteraction interaction;
        private UIDocument document;
        private PanelSettings settings;
        private VisualElement shell, party, queue, objective, controls, actionBar;
        private readonly List<VisualElement> solidPanels = new List<VisualElement>();
        private readonly Dictionary<string, UnitSpriteSheet> sheets = new Dictionary<string, UnitSpriteSheet>();
        private readonly List<UnitMiniature> miniatures = new List<UnitMiniature>();
        private UnitRuntimeState inspected;
        private bool legacy, dirty, logOpen, commandPending;
        private int signature;
        private float clock;
        private string focusName;
        private Vector2 queueScroll;
        private long queueTurn = -1;
        private int escapeFrame = -1;
        private VisualElement forecastPanel;
        private Label forecastSummary, forecastDetails;
        public bool IsOpen => shell != null;
        public VisualElement ScreenRoot => shell;
        public Rect SafeMapRect => interaction == null ? default : interaction.MapScreenRect;
        public UnitRuntimeState Inspected => inspected;

        public void Initialize(GameBootstrap bootstrap)
        {
            owner = bootstrap; gate = owner.GetComponent<UiInputGate>(); gate.Battle = this;
            legacy = Application.isBatchMode;
            settings = Instantiate(Resources.Load<PanelSettings>("UI/FoundationPanel")); settings.sortingOrder = 80;
            document = gameObject.AddComponent<UIDocument>(); document.panelSettings = settings;
            var root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute; root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.display = DisplayStyle.None;
        }
        public void Show() { legacy = false; dirty = true; }
        public void ShowLegacy() { legacy = true; Hide(); }
        private string Text(string en, string ru) => L.Language == InterfaceLanguage.Russian ? ru : en;
        private void LateUpdate()
        {
            if (owner == null) return;
            if (controller != owner.ActiveController)
            {
                Hide(); controller = owner.ActiveController;
                interaction = controller == null ? null : controller.GetComponent<HexGridInteraction>();
                inspected = null; logOpen = false; dirty = true;
                queueScroll = Vector2.zero; queueTurn = -1;
            }
            bool visible = !legacy && controller != null && controller.enabled && owner.Guild.IsAway &&
                controller.Expedition?.Result == null && owner.JourneyBoundary == null && !controller.HasBlockingInterface &&
                !gate.GuildOpen && !gate.PreviewOpen && !gate.ModalOpen && !gate.JourneyOpen;
            if (!visible) { Hide(); return; }
            if (inspected != null && inspected.Team == UnitTeam.Enemy && !controller.IsUnitVisible(inspected)) { inspected = null; dirty = true; }
            int next = StateSignature();
            if (!IsOpen || dirty || next != signature) { Build(); signature = next; }
            UpdateMapArea();
            UpdateTargetPreview();
            var focusedElement = document.rootVisualElement.focusController?.focusedElement as VisualElement;
            if (Input.GetKeyDown(KeyCode.Escape) && (focusedElement == null || !shell.Contains(focusedElement)))
                HandleEscape();
            bool reduced = owner.GuildScreen?.ReducedMotion ?? false;
            if (!reduced) clock += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            foreach (var image in miniatures) image.Render(clock, reduced);
        }
        private int StateSignature()
        {
            unchecked
            {
                int h = (int)controller.Turns.TurnNumber * 31 + (int)controller.Turns.State;
                h = h * 31 + controller.Turns.RemainingMovement; h = h * 31 + (controller.Turns.ActionAvailable ? 1 : 0);
                h = h * 31 + (controller.CanPlayerAct ? 1 : 0); h = h * 31 + (int)L.Language;
                h = h * 31 + (controller.SelectedAbility?.GetHashCode() ?? 0); h = h * 31 + (controller.IsTargetingAttack ? 1 : 0);
                h = h * 31 + controller.ActionHint.GetHashCode(); h = h * 31 + (controller.Expedition.ChestOpened ? 1 : 0);
                h = h * 31 + (controller.BossAttack?.Pending == true ? 1 : 0);
                foreach (var unit in controller.Turns.Order)
                { h = h * 31 + unit.CurrentHealth; h = h * 31 + unit.Position.GetHashCode(); h = h * 31 + unit.HealingPotions; h = h * 31 + (controller.IsUnitVisible(unit) ? 1 : 0); }
                if (logOpen) h = h * 31 + controller.GetComponent<CombatText>().Revision;
                return h;
            }
        }
        private void Hide()
        {
            if (!IsOpen) return;
            var oldQueue = shell.Q<ScrollView>("hud-queue-scroll"); if (oldQueue != null) queueScroll = oldQueue.scrollOffset;
            interaction?.SetMapSafeArea(null);
            controller?.GetComponent<HexGridView>()?.SetPathCells(null);
            document.rootVisualElement.Clear(); document.rootVisualElement.style.display = DisplayStyle.None;
            shell = null; solidPanels.Clear(); miniatures.Clear();
        }
        private void Build()
        {
            var oldQueue = shell?.Q<ScrollView>("hud-queue-scroll"); if (oldQueue != null) queueScroll = oldQueue.scrollOffset;
            string focused = focusName ?? (document.rootVisualElement.focusController?.focusedElement as VisualElement)?.name;
            focusName = null; dirty = commandPending = false;
            var root = document.rootVisualElement; root.Clear(); root.styleSheets.Clear();
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Foundation")); root.styleSheets.Add(Resources.Load<StyleSheet>("UI/BattleHud"));
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); root.style.display = DisplayStyle.Flex;
            solidPanels.Clear(); miniatures.Clear();
            shell = new VisualElement { name = "battle-hud", pickingMode = PickingMode.Ignore }; shell.AddToClassList("battle-screen"); root.Add(shell);
            shell.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                HandleEscape(); evt.StopImmediatePropagation();
            });
            BuildParty(); BuildQueue(); BuildObjective(); BuildControls(); BuildActionBar();
            if (inspected != null) BuildInspection();
            if (logOpen) BuildLog();
            BuildForecast();
            var target = shell.Q<Button>(focused ?? "");
            if (target != null && target.enabledInHierarchy) shell.schedule.Execute(() => { if (target.panel != null) target.Focus(); });
        }
        private VisualElement Panel(string name, string css)
        {
            var element = new VisualElement { name = name }; element.AddToClassList("hud-panel"); element.AddToClassList(css);
            solidPanels.Add(element); shell.Add(element); return element;
        }
        private Label Label(VisualElement parent, string name, string text, string css = null)
        { var label = new Label(text) { name = name, pickingMode = PickingMode.Ignore }; label.AddToClassList("hud-label"); if (css != null) label.AddToClassList(css); parent.Add(label); return label; }
        private Button Button(VisualElement parent, string name, string text, Action action, bool enabled = true)
        {
            var page = shell; var battle = controller; long turn = controller.Turns.TurnNumber;
            var button = UiComponents.Button(text, () =>
            {
                if (commandPending || !ReferenceEquals(shell, page) || controller != battle || owner.ActiveController != battle || battle.Turns.TurnNumber != turn) return;
                commandPending = true; action(); gate.ConsumeClosingFrame(); dirty = true; focusName = name;
                foreach (var control in page.Query<Button>().ToList()) control.SetEnabled(false);
            });
            button.name = name; button.AddToClassList("hud-button"); button.SetEnabled(enabled); parent.Add(button); return button;
        }
        private UnitMiniature Miniature(UnitRuntimeState unit, string name)
        {
            if (!sheets.TryGetValue(unit.Definition.Id, out var sheet)) { sheet = UnitSpriteSheet.Load(unit.Definition.Id); sheets.Add(unit.Definition.Id, sheet); }
            var image = new UnitMiniature(sheet) { name = name }; image.AddToClassList("hud-miniature"); miniatures.Add(image); return image;
        }
        private string Name(UnitRuntimeState unit) => unit.Team == UnitTeam.Player ? L.AdventurerName(unit.InstanceId) : L.T(unit.Definition.DisplayName);
        private void Inspect(UnitRuntimeState unit)
        {
            if (unit.Team == UnitTeam.Enemy && !controller.IsUnitVisible(unit)) return;
            inspected = unit;
            if (unit.IsAlive && unit == controller.Turns.ActiveUnit && unit.Team == UnitTeam.Player && controller.CanPlayerAct) controller.TrySelectUnit(unit.Position);
        }
        private void BuildParty()
        {
            party = Panel("hud-party", "hud-party");
            foreach (var unit in controller.Units)
            {
                var button = Button(party, "hud-hero-" + unit.InstanceId, "", () => Inspect(unit)); button.AddToClassList("hud-portrait-card");
                button.EnableInClassList("selected", unit == controller.Turns.ActiveUnit); button.EnableInClassList("fallen", !unit.IsAlive);
                var portrait = new VisualElement { pickingMode = PickingMode.Ignore }; portrait.AddToClassList("hud-portrait");
                portrait.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("UI/Art/" + unit.Definition.Id)); button.Add(portrait);
                Label(button, "portrait-hp-" + unit.InstanceId, unit.IsAlive ? unit.CurrentHealth + "/" + unit.Definition.MaxHealth : Text("Fallen", "Погиб"));
                button.tooltip = Name(unit);
            }
        }
        private void BuildQueue()
        {
            queue = Panel("hud-queue", "hud-queue");
            Label(queue, "hud-round", L.T("Round ") + controller.Turns.Round + (controller.Turns.ActiveUnit?.Team == UnitTeam.Enemy ? " · " + Text("Enemy turn", "Ход врага") : ""));
            var list = new ScrollView(ScrollViewMode.Horizontal) { name = "hud-queue-scroll", verticalScrollerVisibility = ScrollerVisibility.Hidden };
            list.style.flexGrow = 1; list.style.minHeight = 0; queue.Add(list);
            list.contentContainer.AddToClassList("hud-queue-row");
            Button activeButton = null;
            foreach (var unit in controller.Turns.Order)
            {
                if (!controller.IsUnitVisible(unit)) continue;
                var button = Button(list, "queue-" + unit.InstanceId, "", () => Inspect(unit)); button.AddToClassList("hud-queue-unit");
                button.EnableInClassList("selected", unit == controller.Turns.ActiveUnit); button.EnableInClassList("enemy", unit.Team == UnitTeam.Enemy);
                button.Add(Miniature(unit, "queue-sprite-" + unit.InstanceId)); button.tooltip = Name(unit) + " · HP " + unit.CurrentHealth;
                if (unit == controller.Turns.ActiveUnit) activeButton = button;
            }
            bool newTurn = queueTurn != controller.Turns.TurnNumber; queueTurn = controller.Turns.TurnNumber;
            list.schedule.Execute(() =>
            {
                if (list.panel == null) return;
                list.scrollOffset = queueScroll;
                if (newTurn && activeButton != null) list.ScrollTo(activeButton);
            });
        }
        private void BuildObjective()
        {
            objective = Panel("hud-objective", "hud-objective"); var run = controller.Expedition;
            Label(objective, "hud-goal", run.MissionCompleted ? L.T("Objective complete. Reach EXIT to extract.") : L.T(run.Mission.Name), "hud-title");
            Label(objective, "hud-section", owner.Journey == null ? "" : L.F("Section {0}/{1}", owner.Journey.Completed + 1, owner.Journey.Sections));
            if (controller.BossAttack?.Pending == true) Label(objective, "hud-warning", L.T("Red hexes: 10 damage next boss turn."));
        }
        private void BuildControls()
        {
            controls = Panel("hud-controls", "hud-controls");
            var languages = new VisualElement(); languages.AddToClassList("hud-row"); controls.Add(languages);
            Button(languages, "hud-ru", "RU", () => L.SetLanguage(InterfaceLanguage.Russian, !Application.isBatchMode));
            Button(languages, "hud-en", "EN", () => L.SetLanguage(InterfaceLanguage.English, !Application.isBatchMode));
            Button(controls, "hud-log-toggle", Text("Log", "Журнал"), () => logOpen = !logOpen);
            if (owner.DebugMode)
            {
                controls.style.height = 130;
                Button(controls, "hud-legacy", Text("Legacy HUD", "Старый HUD"), ShowLegacy);
            }
            var camera = Panel("hud-camera", "hud-camera");
            Button(camera, "hud-overview", Text("Map / detail", "Карта / вблизи"), () => { if (interaction.DetailView) interaction.FrameCamera(); else interaction.ShowCharacterDetail(); });
        }
        private void BuildActionBar()
        {
            actionBar = Panel("hud-actions", "hud-actions");
            var active = controller.Turns.ActiveUnit;
            var actor = new VisualElement(); actor.AddToClassList("hud-actor"); actionBar.Add(actor);
            if (controller.IsUnitVisible(active)) actor.Add(Miniature(active, "active-sprite"));
            Label(actor, "hud-active", controller.IsUnitVisible(active) ? Name(active) + " · HP " + active.CurrentHealth + "/" + active.Definition.MaxHealth : Text("Enemy turn", "Ход врага"), "hud-title");
            Label(actor, "hud-resources", controller.CanPlayerAct ? Text("Move: ", "Движение: ") + controller.Turns.RemainingMovement + Text(" · Action: ", " · Действие: ") + (controller.Turns.ActionAvailable ? "1/1" : "0/1") : Text("Please wait", "Подождите"));
            var actions = new VisualElement(); actions.AddToClassList("hud-row"); actionBar.Add(actions);
            bool canAct = controller.CanPlayerAct;
            ActionButton(actions, "move", L.T("Move / cancel"), controller.CancelTargeting, canAct,
                !controller.IsTargetingAttack && controller.SelectedAbility == null, Text("Movement points · Esc cancels targeting", "Очки движения · Esc отменяет выбор"));
            ActionButton(actions, "attack", L.T("Basic attack"), () => controller.SelectBasicAttack(), canAct && controller.Turns.ActionAvailable,
                controller.IsTargetingAttack, Text("Range ", "Дальность ") + (active?.Definition.AttackRange ?? 0));
            if (active?.Team == UnitTeam.Player)
                foreach (var ability in active.Definition.Abilities)
                { var chosen = ability; ActionButton(actions, "ability-" + ability.Id, L.T(ability.Name), () => controller.SelectAbility(chosen), canAct && controller.Turns.ActionAvailable,
                    controller.SelectedAbility == ability, L.T(ability.Description), ability.Id); }
            string potionReason = ConsumableSystem.CanHeal(controller.Turns, active) ? Text("Restore health · consumes one potion", "Восстановить здоровье · расходует одно зелье") :
                active?.HealingPotions == 0 ? Text("No potions equipped", "Нет зелий в снаряжении") : Text("Health is already full", "Здоровье уже полное");
            ActionButton(actions, "potion", L.F("Potion ({0})", active?.HealingPotions ?? 0), () => controller.TryUseHealingPotion(), canAct && ConsumableSystem.CanHeal(controller.Turns, active), false, potionReason);
            ActionButton(actions, "wait", L.T("Wait"), () => controller.TryWaitAction(), canAct && controller.Turns.ActionAvailable, false, Text("Spend the action; keep movement", "Потратить действие, сохранить движение"));
            var mission = new VisualElement(); mission.AddToClassList("hud-row"); actionBar.Add(mission);
            var run = controller.Expedition; var presentation = controller.GetComponent<Expeditions.ExpeditionUI>();
            if (run.Mission.RequiresRelic) Button(mission, "hud-relic", L.T("Take relic"), () => controller.TryOpenChest(), canAct && run.CanOpenChest(controller.SelectedUnit));
            Button(mission, "hud-extract", L.T("Extract party"), presentation.RequestExtraction, canAct && run.CanExtract(controller.SelectedUnit));
            Button(mission, "hud-retreat", L.T("Return / retreat"), presentation.RequestRetreat, canAct && run.CanRetreat(controller.SelectedUnit));
            Label(actionBar, "hud-hint", canAct ? controller.ActionHint : active?.Team == UnitTeam.Enemy ? Text("Enemy turn — please wait.", "Ход врага — подождите.") : Text("Resolving action…", "Выполнение действия…"), "hud-hint");
            var end = Panel("hud-end-panel", "hud-end-panel");
            var button = Button(end, "hud-end-turn", L.T("End turn"), () => controller.TryEndTurn(), canAct); button.AddToClassList("primary");
        }
        private void HandleEscape()
        {
            if (escapeFrame == Time.frameCount) return;
            escapeFrame = Time.frameCount;
            if (logOpen || inspected != null) { logOpen = false; inspected = null; }
            else controller.CancelTargeting();
            dirty = true; gate.ConsumeClosingFrame();
        }

        private void ActionButton(VisualElement parent, string id, string title, Action action, bool enabled, bool selected, string description, string icon = null)
        {
            var wrapper = new VisualElement(); wrapper.AddToClassList("hud-action-wrapper"); parent.Add(wrapper);
            var button = Button(wrapper, "hud-" + id, "", action, enabled);
            button.AddToClassList("hud-action-button"); button.EnableInClassList("selected", selected);
            button.Add(new ActionIcon(icon ?? id));
            Label(button, "action-title-" + id, title + (id == "move" ? "" : " · 1"));
            string blocked = !controller.CanPlayerAct ? Text("Wait for your turn / resolution", "Дождитесь своего хода / выполнения") :
                !controller.Turns.ActionAvailable && id != "move" ? Text("Action already spent", "Действие уже потрачено") : !enabled ? description : "";
            button.tooltip = (id == "move" ? "" : Text("Cost: 1 action. ", "Цена: 1 действие. ")) + description + (blocked.Length > 0 ? "\n" + blocked : "");
            wrapper.tooltip = button.tooltip;
            // Keep disabled reasons discoverable without attempting a command.
            wrapper.RegisterCallback<PointerEnterEvent>(_ => { var hint = shell?.Q<Label>("hud-hint"); if (hint != null) hint.text = blocked.Length > 0 ? blocked : button.tooltip; });
            wrapper.RegisterCallback<PointerLeaveEvent>(_ => { var hint = shell?.Q<Label>("hud-hint"); if (hint != null) hint.text = controller.ActionHint; });
        }

        private void BuildForecast()
        {
            // A non-interactive overlay follows the map target. It must never steal hover from it.
            forecastPanel = new VisualElement { name = "hud-forecast", pickingMode = PickingMode.Ignore };
            forecastPanel.AddToClassList("hud-forecast"); shell.Add(forecastPanel);
            forecastSummary = Label(forecastPanel, "hud-forecast-summary", "", "hud-title");
            forecastDetails = Label(forecastPanel, "hud-forecast-details", "");
            forecastPanel.style.display = DisplayStyle.None;
        }

        private void UpdateTargetPreview()
        {
            var view = controller.GetComponent<HexGridView>();
            var hovered = interaction.Hovered;
            bool targeting = controller.IsTargetingAttack || controller.SelectedAbility != null ||
                (hovered.HasValue && controller.Enemies.Any(e => controller.IsUnitVisible(e) && e.Position == hovered.Value));
            if (!controller.CanPlayerAct || !hovered.HasValue || gate.BlocksGameplay)
            { forecastPanel.style.display = DisplayStyle.None; view.SetPathCells(null); return; }
            string summary, details = "";
            if (targeting)
            {
                view.SetPathCells(null);
                bool valid = controller.PreviewTarget(hovered.Value, out var attacks, out var reason);
                if (!valid) summary = L.T(reason);
                else if (attacks.Count > 0)
                {
                    summary = string.Join("\n", attacks.Select(a => a.TargetPosition + " · " + a.HitPercent + "% · " +
                        a.MinimumDamage + "–" + a.MaximumDamage + Text(" damage on hit", " урона при попадании")));
                    details = string.Join("\n", attacks.Select(a => "d20 " + Signed(a.AttackBonus) + " ≥ " + a.Defense + " · d" + a.DamageDie + " " + Signed(a.DamageBonus)));
                }
                else summary = L.T(controller.SelectedAbility.Description);
                if (valid && controller.SelectedAbility?.Effect == Abilities.AbilityEffect.Push)
                {
                    var actor = controller.SelectedUnit;
                    var landing = new HexCoordinates(hovered.Value.Q * 2 - actor.Position.Q, hovered.Value.R * 2 - actor.Position.R);
                    summary = owner.Grid.GetCell(landing).Terrain == TerrainType.Pit ? Text("Push into pit · fatal", "Толчок в пропасть · гибель") : Text("Push one hex · no attack roll", "Толчок на одну клетку · без броска атаки");
                }
                forecastPanel.EnableInClassList("invalid", !valid);
                if (valid) details += Text("\nCost: 1 action", "\nЦена: 1 действие");
            }
            else
            {
                var range = controller.Turns.GetMovementRange(controller.SelectedUnit);
                var path = range?.GetPathTo(hovered.Value);
                // Do not show a path through unknown terrain, even if a future model permits it.
                bool visiblePath = path != null && path.Count > 1 && path.All(c => controller.Visibility == null || controller.Visibility.IsVisible(c));
                view.SetPathCells(visiblePath ? path : null);
                if (!visiblePath) { forecastPanel.style.display = DisplayStyle.None; return; }
                summary = Text("Move · ", "Движение · ") + range.Costs[hovered.Value] + " / " + controller.Turns.RemainingMovement;
                forecastPanel.EnableInClassList("invalid", false);
            }
            forecastPanel.style.display = DisplayStyle.Flex;
            forecastSummary.text = summary;
            bool expanded = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            forecastDetails.text = expanded ? details : targeting ? Text("Hold Alt for details · Esc to cancel", "Удерживайте Alt: детали · Esc: отмена") : Text("Click to move", "Нажмите для движения");
            var root = document.rootVisualElement.worldBound;
            var camera = interaction.GridCamera.pixelRect;
            var point = interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(hovered.Value));
            float x = (point.x - camera.x) * root.width / camera.width;
            float y = (camera.yMax - point.y) * root.height / camera.height;
            float height = Mathf.Max(85, forecastPanel.layout.height);
            forecastPanel.style.left = Mathf.Clamp(x + 18, 128, root.width - 338);
            forecastPanel.style.top = Mathf.Clamp(y - height - 18, 110, Mathf.Max(110, actionBar.layout.y - height - 8));
        }

        private static string Signed(int value) => value < 0 ? value.ToString() : "+" + value;

        private void BuildInspection()
        {
            var panel = Panel("hud-inspection", "hud-inspection");
            Label(panel, "hud-inspected-name", Name(inspected), "hud-title");
            Label(panel, "hud-inspected-stats", "HP " + inspected.CurrentHealth + "/" + inspected.Definition.MaxHealth + Text(" · ATK ", " · АТК ") + inspected.Attack + Text(" · DEF ", " · ЗАЩ ") + inspected.Defense);
            Label(panel, "hud-inspected-turn", inspected == controller.Turns.ActiveUnit ? Text("Active turn", "Активный ход") : Text("Inspection only — acts on own turn", "Осмотр — действует в свой ход"));
            Button(panel, "hud-close-inspection", Text("Close", "Закрыть"), () => inspected = null);
        }
        private void BuildLog()
        {
            var panel = Panel("hud-log", "hud-log");
            Label(panel, "hud-log-title", Text("Combat log", "Журнал боя"), "hud-title");
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("hud-log-scroll"); panel.Add(scroll);
            var feedback = controller.GetComponent<CombatText>();
            Label(scroll, "hud-log-message", feedback.History.Count > 0 ? string.Join("\n\n", feedback.History.Reverse()) : L.T("No combat result yet."));
            Button(panel, "hud-close-log", Text("Close", "Закрыть"), () => logOpen = false);
        }
        public Rect ScreenRect(VisualElement element)
        {
            var root = document.rootVisualElement.worldBound; var rect = element.worldBound; var camera = interaction.GridCamera.pixelRect;
            return new Rect(camera.x + (rect.x - root.x) * camera.width / root.width,
                camera.y + (root.yMax - rect.yMax) * camera.height / root.height, rect.width * camera.width / root.width, rect.height * camera.height / root.height);
        }
        public bool BlocksPointer(Vector2 point) => IsOpen && solidPanels.Any(panel => panel.panel != null && ScreenRect(panel).Contains(point));
        private void UpdateMapArea()
        {
            if (shell.worldBound.width <= 0 || actionBar.worldBound.height <= 0 || queue.worldBound.height <= 0) return;
            party.style.top = Mathf.Max(110, objective.layout.yMax + 8);
            var inspection = shell.Q("hud-inspection");
            if (inspection != null) inspection.style.top = Mathf.Max(116, objective.layout.yMax + 8);
            var camera = interaction.GridCamera.pixelRect;
            float margin = 8 * camera.width / document.rootVisualElement.worldBound.width;
            var area = Rect.MinMaxRect(ScreenRect(party).xMax + margin, ScreenRect(actionBar).yMax + margin,
                camera.xMax - margin, Mathf.Min(ScreenRect(queue).yMin, ScreenRect(objective).yMin, ScreenRect(controls).yMin) - margin);
            if (area.width > 100 && area.height > 100) interaction.SetMapSafeArea(area);
        }
        internal void SetValidationTarget(RenderTexture target) => settings.targetTexture = target;
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            Hide(); if (gate != null && gate.Battle == this) gate.Battle = null;
            foreach (var sheet in sheets.Values) sheet?.Dispose();
            if (document != null) Destroy(document); if (settings != null) Destroy(settings);
        }
    }
}
