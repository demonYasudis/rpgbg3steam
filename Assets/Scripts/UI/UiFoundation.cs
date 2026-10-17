using System;
using System.Collections.Generic;
using GuildTactics.Core;
using UnityEngine;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    /// <summary>WP-33 component gallery. Gameplay screens are implemented in later packages.</summary>
    public sealed class UiFoundation : MonoBehaviour
    {
        private UIDocument document;
        private PanelSettings settings;
        private UiInputGate gate;
        private VisualElement shell;
        private VisualElement modal;
        private VisualElement focusBeforeModal;
        private readonly List<Button> modalButtons = new List<Button>();
        private Button firstFocus;
        private int selectedTab;
        private int selectedCard = -1;
        private InterfaceLanguage renderedLanguage;
        public bool IsOpen => gate != null && gate.PreviewOpen;
        public bool IsModalOpen => modal != null;
        public VisualElement ScreenRoot => shell;
        public int ModalCloseCount { get; private set; }

        private void Awake()
        {
            gate = GetComponent<UiInputGate>();
            if (gate == null) gate = gameObject.AddComponent<UiInputGate>();
            var template = Resources.Load<PanelSettings>("UI/FoundationPanel");
            if (template == null) throw new InvalidOperationException("Missing UI/FoundationPanel settings.");
            settings = Instantiate(template);
            settings.name = "Guild UI Runtime Panel";
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 100;
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/FoundationTheme");
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.rootVisualElement.style.position = Position.Absolute;
            document.rootVisualElement.style.left = 0;
            document.rootVisualElement.style.top = 0;
            document.rootVisualElement.style.right = 0;
            document.rootVisualElement.style.bottom = 0;
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.style.display = DisplayStyle.None;
        }

        public void SetPreview(bool open)
        {
            if (open == IsOpen) return;
            if (!open) { CloseModal(); gate.ConsumeClosingFrame(); }
            gate.PreviewOpen = open;
            document.rootVisualElement.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) Build();
            else { document.rootVisualElement.Clear(); shell = null; }
        }

        private string Text(string en, string ru) => L.Language == InterfaceLanguage.Russian ? ru : en;

        private void Build()
        {
            CloseModal();
            renderedLanguage = L.Language;
            var root = document.rootVisualElement;
            root.Clear();
            root.styleSheets.Clear();
            var stylesheet = Resources.Load<StyleSheet>("UI/Foundation");
            if (stylesheet == null) throw new InvalidOperationException("Missing UI/Foundation stylesheet.");
            root.styleSheets.Add(stylesheet);
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            shell = new VisualElement { name = "foundation-screen" };
            shell.AddToClassList("foundation-screen");
            shell.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("UI/Art/guild-hall"));
            root.Add(shell);
            shell.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            var header = Row("header"); shell.Add(header);
            var title = new Label(Text("THE GUILD", "ГИЛЬДИЯ")); title.AddToClassList("title"); header.Add(title);
            var language = Row("language"); header.Add(language);
            firstFocus = UiComponents.Button("RU", () => ChangeLanguage(InterfaceLanguage.Russian));
            firstFocus.EnableInClassList("selected", L.Language == InterfaceLanguage.Russian);
            language.Add(firstFocus);
            var english = UiComponents.Button("EN", () => ChangeLanguage(InterfaceLanguage.English));
            english.EnableInClassList("selected", L.Language == InterfaceLanguage.English); language.Add(english);
            language.Add(UiComponents.Button(Text("Legacy UI / Esc", "Старый UI / Esc"), () => SetPreview(false)));

            var tabs = Row("tabs"); shell.Add(tabs);
            string[] names = { Text("Party", "Отряд"), Text("Storage", "Склад"), Text("Expeditions", "Походы") };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                tabs.Add(UiComponents.Tab(names[i], selectedTab == i, () => { selectedTab = index; Build(); }));
            }
            var subtitle = new Label(Text("UI workshop · independent components · no guild commands",
                "Мастерская UI · отдельные компоненты · без команд гильдии"));
            subtitle.AddToClassList("subtitle"); shell.Add(subtitle);

            var tooltip = new Label("") { name = "tooltip", pickingMode = PickingMode.Ignore };
            tooltip.AddToClassList("tooltip");
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                // Reserve the rail so keyboard focus/tooltip updates cannot resize the cards.
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible
            };
            scroll.AddToClassList("gallery-scroll"); shell.Add(scroll);
            var cards = Row("cards"); scroll.Add(cards);
            string[] ids = { "warrior", "rogue", "ranger", "mage" };
            string[] classes = { Text("Warrior", "Воин"), Text("Rogue", "Плут"), Text("Ranger", "Следопыт"), Text("Mage", "Маг") };
            for (int i = 0; i < ids.Length; i++)
            {
                int index = i;
                var card = UiComponents.Card(classes[i], Text("Class illustration", "Иллюстрация класса"),
                    Resources.Load<Texture2D>("UI/Art/" + ids[i]), () => SelectCard(index));
                card.name = "class-card-" + ids[i];
                card.EnableInClassList("selected", i == selectedCard);
                cards.Add(card);
                UiComponents.Tooltip(card, tooltip, Text("Select a card to inspect its frame and focus state.",
                    "Выберите карточку для проверки рамки и клавиатурного фокуса."));
            }
            var samples = Row("samples"); scroll.Add(samples);
            var modalButton = UiComponents.Button(Text("Open dialog", "Открыть окно"), ShowModal, "primary");
            modalButton.name = "open-modal"; samples.Add(modalButton);
            UiComponents.Tooltip(modalButton, tooltip, Text("Dialog captures input. Esc returns focus here.", "Окно перехватывает ввод. Esc возвращает фокус сюда."));
            var disabled = UiComponents.Button(Text("Unavailable", "Недоступно"), null); disabled.SetEnabled(false); samples.Add(disabled);
            samples.Add(UiComponents.IconLabel(UiIconKind.Gold, Text("Gold", "Золото")));
            samples.Add(UiComponents.IconLabel(UiIconKind.Equipment, Text("Equipment", "Экипировка")));
            samples.Add(UiComponents.IconLabel(UiIconKind.Expedition, Text("Expedition", "Поход")));
            shell.Add(tooltip);
            var footer = new Label(Text("Foundation preview · Tab / Shift+Tab: focus · Enter / Space: select · Esc: back",
                "Проверка основы · Tab / Shift+Tab: фокус · Enter / Пробел: выбор · Esc: назад"));
            footer.AddToClassList("footer"); shell.Add(footer);
            firstFocus.Focus();
            shell.schedule.Execute(() => { if (IsOpen && !IsModalOpen) firstFocus.Focus(); });
        }

        private static VisualElement Row(string className)
        {
            var row = new VisualElement(); row.AddToClassList(className); return row;
        }

        private void SelectCard(int index)
        {
            selectedCard = index;
            var cards = shell.Q(className: "cards");
            for (int i = 0; i < cards.childCount; i++) cards[i].EnableInClassList("selected", i == index);
        }

        private void ChangeLanguage(InterfaceLanguage language)
        {
            if (L.Language == language) return;
            L.SetLanguage(language, !Application.isBatchMode); Build();
        }

        public void ShowModal()
        {
            if (!IsOpen || modal != null) return;
            focusBeforeModal = document.rootVisualElement.focusController.focusedElement as VisualElement;
            var close = UiComponents.Button(Text("Close / Esc", "Закрыть / Esc"), CloseModal, "primary");
            close.name = "close-modal";
            modalButtons.Add(close);
            modal = UiComponents.Modal(Text("A moment at the guild", "Привал в гильдии"),
                Text("This is a component preview. Map clicks, camera controls and game commands are blocked while this screen owns input. No resources are spent.",
                "Это проверка компонента. Пока экран владеет вводом, клики по карте, управление камерой и игровые команды заблокированы. Ресурсы не расходуются."), close);
            gate.ModalOpen = true;
            // Disable background focus and pointer activation, including language and preview switches.
            foreach (var child in shell.Children()) child.SetEnabled(false);
            shell.Add(modal);
            close.Focus();
        }

        public void CloseModal()
        {
            if (modal == null) return;
            modal.RemoveFromHierarchy(); modal = null; modalButtons.Clear();
            gate.ModalOpen = false; ModalCloseCount++;
            foreach (var child in shell.Children()) child.SetEnabled(true);
            focusBeforeModal?.Focus(); focusBeforeModal = null;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (IsModalOpen) CloseModal(); else SetPreview(false);
                evt.StopImmediatePropagation();
            }
            else if (IsModalOpen && evt.keyCode == KeyCode.Tab)
            {
                modalButtons[0].Focus(); evt.StopImmediatePropagation();
            }
        }

        private void Update()
        {
            if (IsOpen && renderedLanguage != L.Language) Build();
        }

        private void OnGUI()
        {
            if (IsOpen || gate.GuildOpen) return;
            if (GUI.Button(new Rect(Screen.width - 292, Screen.height - 26, 172, 24),
                Text("UI workshop (WP-33)", "Мастерская UI (WP-33)"))) SetPreview(true);
        }

        private void OnDisable()
        {
            if (document == null) return;
            SetPreview(false);
        }

        private void OnDestroy()
        {
            if (document != null) Destroy(document);
            if (settings != null) Destroy(settings);
        }

        internal void SetValidationTarget(RenderTexture target) => settings.targetTexture = target;
    }
}
