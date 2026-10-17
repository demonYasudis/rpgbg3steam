using System.Collections.Generic;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEngine;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    /// <summary>Guild presentation only. Party changes and departure use the existing model APIs.</summary>
    public sealed partial class GuildPartyScreen : MonoBehaviour
    {
        private GameBootstrap bootstrap;
        private GuildState guild;
        private UiInputGate gate;
        private UIDocument document;
        private PanelSettings settings;
        private readonly Dictionary<string, UnitSpriteSheet> sheets = new Dictionary<string, UnitSpriteSheet>();
        private VisualElement shell;
        private VisualElement content;
        private VisualElement modal;
        private bool legacy;
        private bool dirty;
        private enum GuildDialog { None, Reserve, Hero, Recruitment, NewGuild }
        private GuildDialog dialog;
        private int tab;
        private Vector2 reserveScroll;
        private string selectedId;
        private string restoreFocus;
        private InterfaceLanguage language;
        public bool IsOpen => shell != null;
        public bool IsReserveOpen => dialog == GuildDialog.Reserve && IsOpen;
        public string SelectedId => selectedId;
        public VisualElement ScreenRoot => shell;

        public void Initialize(GameBootstrap owner)
        {
            bootstrap = owner;
            reducedMotion = !Application.isBatchMode && PlayerPrefs.GetInt(ReducedMotionPreferenceKey, 0) != 0;
            legacy = Application.isBatchMode; // Existing batch fixtures exercise the legacy presentation.
            gate = owner.GetComponent<UiInputGate>();
            settings = Instantiate(Resources.Load<PanelSettings>("UI/FoundationPanel"));
            settings.sortingOrder = 90;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            var root = document.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;
            root.style.display = DisplayStyle.None;
            BindGuild();
            RefreshVisibility();
        }

        private string Text(string en, string ru) => L.Language == InterfaceLanguage.Russian ? ru : en;
        private void BindGuild()
        {
            if (guild == bootstrap.Guild) return;
            if (guild != null) guild.Changed -= OnChanged;
            guild = bootstrap.Guild;
            guild.Changed += OnChanged;
            dialog = GuildDialog.None;
            ClearStorageFeedback();
            travelFeedbackEn = travelFeedbackRu = null;
            storageScroll = equipmentScroll = Vector2.zero;
            dirty = true;
        }
        private void OnChanged() => dirty = true;

        public void ShowParty()
        {
            legacy = false; tab = 0;
            RefreshVisibility();
            dirty = true;
        }

        public void ShowLegacy()
        {
            legacy = true;
            Hide();
        }

        private void Update()
        {
            if (bootstrap == null) return;
            BindGuild();
            RefreshVisibility();
            if (IsOpen && (dirty || language != L.Language)) Build();
            UpdateMenuMotion();
        }

        private void RefreshVisibility()
        {
            if (guild.IsAway) legacy = false; // Return to the new party screen after the expedition.
            bool visible = !legacy && !guild.IsAway && !gate.PreviewOpen;
            if (visible && !IsOpen) Build();
            else if (!visible && IsOpen) Hide();
        }

        private void Hide()
        {
            if (!IsOpen) return;
            RememberStorageScroll();
            ClearMotionElements(); reactionHeroId = null;
            gate.GuildOpen = false;
            gate.ConsumeClosingFrame();
            document.rootVisualElement.Clear();
            document.rootVisualElement.style.display = DisplayStyle.None;
            shell = null; modal = null;
            dialog = GuildDialog.None;
        }

        private void Build()
        {
            var oldScroll = shell?.Q<ScrollView>("reserve-scroll");
            if (oldScroll != null) reserveScroll = oldScroll.scrollOffset;
            RememberStorageScroll();
            ClearMotionElements();
            string focusName = restoreFocus ?? (document.rootVisualElement.focusController?.focusedElement as VisualElement)?.name;
            restoreFocus = null;
            dirty = false; language = L.Language;
            commandPending = false;
            if (guild.Roster.All(h => h.Id != selectedId)) selectedId = guild.SelectedIds.FirstOrDefault() ?? guild.Roster.FirstOrDefault()?.Id;
            var root = document.rootVisualElement;
            root.Clear(); root.styleSheets.Clear();
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Foundation"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/GuildParty"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/GuildStorage"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/GuildExpeditions"));
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root.style.display = DisplayStyle.Flex;
            gate.GuildOpen = true;
            shell = new VisualElement { name = "guild-party-screen" };
            shell.AddToClassList("foundation-screen"); shell.AddToClassList("guild-screen");
            shell.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("UI/Art/guild-hall"));
            root.Add(shell);
            BuildCandlelight();
            shell.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            content = new VisualElement(); content.style.flexGrow = 1; shell.Add(content);
            var header = Row("header"); content.Add(header);
            var title = new Label(Text("THE GUILD", "ГИЛЬДИЯ")); title.AddToClassList("title"); header.Add(title);
            header.Add(UiComponents.IconLabel(UiIconKind.Gold, Text("Gold: ", "Золото: ") + guild.Gold));
            var languages = Row("language"); header.Add(languages);
            foreach (var value in new[] { InterfaceLanguage.Russian, InterfaceLanguage.English })
            {
                var chosen = value;
                var button = UiComponents.Button(value == InterfaceLanguage.Russian ? "RU" : "EN", () =>
                { L.SetLanguage(chosen, !Application.isBatchMode); dirty = true; });
                button.EnableInClassList("selected", value == L.Language); languages.Add(button);
            }
            AddButton(languages, Text("Legacy UI", "Старый UI"), ShowLegacy, "legacy-ui");
            var tabs = Row("tabs"); content.Add(tabs);
            string[] titles = { Text("Party", "Отряд"), Text("Storage", "Склад"), Text("Expeditions", "Походы") };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                var button = UiComponents.Tab(titles[i], i == tab, () => { tab = index; dirty = true; });
                button.name = "guild-tab-" + i; tabs.Add(button);
            }
            BuildMotionOptions(tabs);
            if (tab == 1) BuildStorage(content);
            else if (tab == 2) BuildExpeditions(content);
            else
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("gallery-scroll"); content.Add(scroll);
                if (tab == 0) BuildParty(scroll);
            }
            string status = tab == 1 ? Text("Equipment changes are saved automatically.", "Изменения снаряжения сохраняются автоматически.") :
                tab == 2 ? Text("Expedition selection is saved automatically.", "Выбор похода сохраняется автоматически.") :
                guild.CanLaunch ? Text("Party changes are saved automatically.", "Изменения состава сохраняются автоматически.") : LaunchReason();
            string saveMessage = L.SaveMessage(bootstrap.SaveMessage);
            var save = new Label(saveMessage == null ? status : status + "\n" + saveMessage) { name = "launch-reason" };
            save.AddToClassList("footer"); content.Add(save);
            if (!string.IsNullOrEmpty(bootstrap.SaveMessage))
                AddButton(content, Text("Retry save", "Повторить сохранение"), () => { bootstrap.RetryGuildSave(); dirty = true; }, "guild-retry-save");
            modal = null;
            if (dialog == GuildDialog.Reserve) BuildReserve();
            else if (dialog == GuildDialog.Hero) BuildHeroDetails();
            else if (dialog == GuildDialog.Recruitment) BuildRecruitment();
            else if (dialog == GuildDialog.NewGuild) BuildNewGuildConfirmation();
            transitionStarted = motionTime;
            ApplyMenuMotion();
            var focusScope = modal ?? shell;
            var target = focusScope.Q(focusName ?? "");
            if (target == null || !target.focusable || !target.enabledInHierarchy) target = focusScope.Query<Button>().ToList().FirstOrDefault(b => b.enabledInHierarchy);
            shell.schedule.Execute(() => { if (target?.panel != null) target.Focus(); });
        }

        private void BuildParty(VisualElement parent)
        {
            var heading = new Label(Text("YOUR PARTY", "ВАШ ОТРЯД") + " · " + guild.SelectedIds.Count + "/4");
            heading.AddToClassList("subtitle"); parent.Add(heading);
            var cards = Row("party-cards"); parent.Add(cards);
            for (int i = 0; i < GuildState.PartySize; i++)
            {
                var holder = new VisualElement(); holder.AddToClassList("party-slot"); cards.Add(holder);
                var hero = i < guild.SelectedIds.Count ? guild.Roster.First(h => h.Id == guild.SelectedIds[i]) : null;
                if (hero == null)
                {
                    var empty = AddButton(holder, Text("Empty slot\nChoose from reserve", "Пустой слот\nВыбрать из резерва"), OpenReserve, "empty-slot-" + i);
                    empty.AddToClassList("empty-slot"); continue;
                }
                var card = UiComponents.Card(L.AdventurerName(hero.Id), L.T(hero.Definition.DisplayName),
                    Resources.Load<Texture2D>("UI/Art/" + hero.Definition.Id), () => SelectHero(hero.Id));
                card.name = "party-card-" + hero.Id;
                card.EnableInClassList("selected", hero.Id == selectedId);
                card.tooltip = L.AdventurerName(hero.Id) + " · " + hero.Id;
                holder.Add(card);
                MakeMovingPortrait(card.Q("portrait"));
                card.Add(Miniature(hero));
                var health = new Label("HP " + hero.Health + "/" + hero.Definition.MaxHealth + " · " + Status(hero));
                health.AddToClassList("party-health"); card.Add(health);
                AddButton(holder, Text("To reserve", "В резерв"), () => Toggle(hero.Id), "remove-" + hero.Id);
            }
            var selected = guild.Roster.FirstOrDefault(h => h.Id == selectedId);
            var selection = new Label(selected == null ? "" : Text("Selected: ", "Выбран: ") + L.AdventurerName(selected.Id) + " · " + Status(selected));
            selection.name = "selected-hero"; selection.AddToClassList("subtitle"); parent.Add(selection);
            var actions = Row("party-actions"); parent.Add(actions);
            AddButton(actions, Text("Reserve", "Резерв"), OpenReserve, "open-reserve");
            var details = AddButton(actions, Text("Hero details", "Герой"), () => OpenHeroDetails(selectedId, false), "open-hero");
            details.SetEnabled(selected != null);
            AddButton(actions, Text("Recruit", "Найм"), OpenRecruitment, "open-recruitment");
            var depart = AddButton(actions, Text("Choose expedition", "Выбрать поход"), () => { if (guild.CanLaunch) { tab = 2; dirty = true; } }, "choose-expedition");
            depart.AddToClassList("primary"); depart.SetEnabled(guild.CanLaunch);
        }

        private string LaunchReason() => guild.CanLaunch ? Text("Four adventurers ready to depart.", "Четыре бойца готовы к отправлению.") :
            Text("Select four living adventurers to depart.", "Для похода выберите четырёх живых бойцов.");

        private string Status(GuildAdventurer hero)
        {
            if (hero.Status == AdventurerStatus.BodyRecovered) return Text("Body recovered", "Тело возвращено");
            if (hero.Status == AdventurerStatus.Lost) return Text("Permanently lost", "Потерян навсегда");
            return hero.Health < hero.Definition.MaxHealth ? Text("Wounded", "Ранен") : Text("Ready", "Готов");
        }

        public void OpenReserve()
        {
            dialog = GuildDialog.Reserve; restoreFocus = "close-reserve"; dirty = true;
        }
        private void CloseReserve()
        {
            dialog = GuildDialog.None; restoreFocus = "open-reserve"; dirty = true;
        }
        private void SelectHero(string id)
        {
            selectedId = id;
            reactionHeroId = reducedMotion ? null : id; reactionStarted = motionTime;
            dirty = true;
        }
        private void Toggle(string id)
        {
            selectedId = id;
            guild.TryToggleSelection(id);
            dirty = true; // Rejected commands also refresh availability.
        }

        private void BuildReserve()
        {
            var close = UiComponents.Button(Text("Back / Esc", "Назад / Esc"), CloseReserve);
            close.name = "close-reserve";
            modal = UiComponents.Modal(Text("Party and reserve", "Отряд и резерв"),
                Text("Select a hero, then add or return them. Remove someone first if all four slots are occupied.",
                    "Выберите героя и добавьте его в отряд или верните в резерв. Если все места заняты, сначала освободите одно."), close);
            modal.Q("dialog").AddToClassList("reserve-dialog");
            var list = new ScrollView(ScrollViewMode.Vertical) { name = "reserve-scroll" };
            list.AddToClassList("gallery-scroll");
            list.AddToClassList("reserve-list"); modal.Q("dialog").Insert(2, list);
            foreach (var hero in guild.Roster)
            {
                bool inParty = guild.SelectedIds.Contains(hero.Id);
                var row = Row("reserve-row"); list.Add(row);
                var select = AddButton(row, L.AdventurerName(hero.Id) + " · HP " + hero.Health + "/" + hero.Definition.MaxHealth + " · " + Status(hero),
                    () => SelectHero(hero.Id), "reserve-select-" + hero.Id);
                select.AddToClassList("reserve-name"); select.EnableInClassList("selected", selectedId == hero.Id);
                AddButton(row, Text("Details", "Подробнее"), () => OpenHeroDetails(hero.Id, true), "reserve-details-" + hero.Id);
                var toggle = AddButton(row, inParty ? Text("To reserve", "В резерв") : Text("Add to party", "В отряд"),
                    () => Toggle(hero.Id), "reserve-toggle-" + hero.Id);
                bool allowed = hero.Status == AdventurerStatus.Alive && (inParty || guild.SelectedIds.Count < GuildState.PartySize);
                toggle.SetEnabled(allowed);
                toggle.tooltip = allowed ? "" : hero.Status != AdventurerStatus.Alive ? Status(hero) : Text("Party is full", "Отряд заполнен");
            }
            list.schedule.Execute(() => list.scrollOffset = reserveScroll);
            content.SetEnabled(false); shell.Add(modal);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (dialog == GuildDialog.Reserve) CloseReserve();
                else if (dialog != GuildDialog.None) CloseHeroPanel();
                else if (tab == 2) ReturnFromExpeditions();
                else if (tab != 0) { tab = 0; dirty = true; }
                else ShowLegacy();
                evt.StopImmediatePropagation();
            }
            else if (modal != null && evt.keyCode == KeyCode.Tab)
            {
                var buttons = modal.Query<Button>().ToList().Where(b => b.enabledInHierarchy).ToList();
                int index = buttons.IndexOf(shell.panel.focusController.focusedElement as Button);
                buttons[(index + (evt.shiftKey ? buttons.Count - 1 : 1)) % buttons.Count].Focus();
                evt.StopImmediatePropagation();
            }
        }

        private static VisualElement Row(string css) { var row = new VisualElement(); row.AddToClassList(css); return row; }
        private static Button AddButton(VisualElement parent, string text, System.Action action, string name)
        { var button = UiComponents.Button(text, action); button.name = name; parent.Add(button); return button; }

        private void OnGUI()
        {
            if (bootstrap == null || guild.IsAway || IsOpen || gate.PreviewOpen) return;
            if (GUI.Button(new Rect(Screen.width - 465, Screen.height - 26, 168, 24), Text("Party UI (WP-34)", "Отряд (WP-34)"))) ShowParty();
        }
        private void OnDisable() { if (document != null) Hide(); }
        private void OnDestroy()
        {
            if (guild != null) guild.Changed -= OnChanged;
            foreach (var sheet in sheets.Values) sheet?.Dispose();
            if (document != null) Destroy(document);
            if (settings != null) Destroy(settings);
        }
        internal void SetValidationTarget(RenderTexture target) => settings.targetTexture = target;
    }
}
