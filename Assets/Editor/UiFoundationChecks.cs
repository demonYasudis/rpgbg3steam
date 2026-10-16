using System;
using System.IO;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.Editor
{
    /// <summary>Runs the real runtime panel, commands, focus/events and offscreen UI rendering.</summary>
    [InitializeOnLoad]
    public static class UiFoundationChecks
    {
        private const string Pending = "GuildTactics.WP33.Pending";
        private static double deadline;
        private static int frames;
        private static int stage;
        private static UiFoundation ui;
        private static GameBootstrap bootstrap;
        private static RenderTexture target;
        private static Rect cardBeforeSelection;

        static UiFoundationChecks()
        {
            if (SessionState.GetBool(Pending, false)) BeginPolling();
        }

        [MenuItem("Tools/Guild Tactics/Validate UI Foundation (Play Mode)")]
        public static void RunBatch()
        {
            try
            {
                Require(!EditorApplication.isPlaying, "Start outside Play Mode");
                PreparePanel();
                foreach (string id in new[] { "guild-hall", "warrior", "rogue", "ranger", "mage" })
                {
                    var texture = Resources.Load<Texture2D>("UI/Art/" + id);
                    Require(texture != null && texture.width >= 1024 && texture.mipmapCount == 1, "Imported separate artwork: " + id);
                }
                Require(Resources.Load<StyleSheet>("UI/Foundation") != null, "Imported runtime stylesheet");
                EditorSceneManager.OpenScene("Assets/Scenes/TacticalPrototype.unity");
                SessionState.SetBool(Pending, true);
                BeginPolling();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        // Create once in Edit Mode so the serialized theme is present before runtime OnEnable.
        public static void PreparePanel()
        {
            const string path = "Assets/Resources/UI/FoundationPanel.asset";
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path) != null) return;
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/FoundationTheme");
            Require(panel.themeStyleSheet != null, "Imported runtime theme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 100;
            AssetDatabase.CreateAsset(panel, path);
            AssetDatabase.SaveAssets();
        }

        private static void BeginPolling()
        {
            deadline = EditorApplication.timeSinceStartup + 150;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Play Mode timeout");
                if (!EditorApplication.isPlaying || ++frames < 25) return;
                frames = 0;
                switch (stage++)
                {
                    case 0:
                        bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                        ui = bootstrap.GetComponent<UiFoundation>();
                        Require(ui != null && !ui.IsOpen, "Legacy presentation remains default");
                        Require(bootstrap.TryLaunchExpedition(), "Launch real battle fixture");
                        Localization.SetLanguage(InterfaceLanguage.Russian, false);
                        SetTarget(1280, 720);
                        ui.SetPreview(true);
                        break;
                    case 1:
                        VerifyLayout(); Capture("1280-ru");
                        ValidateInputAndLifecycle();
                        ui.SetPreview(true);
                        break;
                    case 2:
                        cardBeforeSelection = ui.ScreenRoot.Q<Button>("class-card-warrior").worldBound;
                        Submit(ui.ScreenRoot.Q<Button>("class-card-ranger"));
                        break;
                    case 3:
                        Require(ui.ScreenRoot.Q<Button>("class-card-warrior").worldBound == cardBeforeSelection,
                            "Card selection/focus/tooltip cannot resize adjacent cards: " + cardBeforeSelection + " -> " +
                            ui.ScreenRoot.Q<Button>("class-card-warrior").worldBound);
                        Localization.SetLanguage(InterfaceLanguage.English, false);
                        break;
                    case 4:
                        VerifyLayout(); Capture("1280-en");
                        SetTarget(1920, 1080);
                        break;
                    case 5:
                        VerifyLayout(); Capture("1920-en");
                        Localization.SetLanguage(InterfaceLanguage.Russian, false);
                        break;
                    case 6:
                        VerifyLayout(); Capture("1920-ru");
                        var opener = ui.ScreenRoot.Q<Button>("open-modal");
                        opener.Focus(); Submit(opener);
                        Require(ui.IsModalOpen, "Real button opens modal");
                        using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("tab")))
                        {
                            var close = ui.ScreenRoot.Q<Button>("close-modal");
                            evt.target = close; close.SendEvent(evt);
                        }
                        break;
                    case 7:
                        Capture("1920-modal-ru");
                        Require(ui.ScreenRoot.panel.focusController.focusedElement == ui.ScreenRoot.Q<Button>("close-modal"), "Modal receives keyboard focus");
                        Escape();
                        Require(!ui.IsModalOpen && ui.IsOpen, "Esc closes modal before preview");
                        Require(ui.ScreenRoot.panel.focusController.focusedElement == ui.ScreenRoot.Q<Button>("open-modal"), "Focus restored to opener");
                        Escape();
                        Require(!ui.IsOpen && bootstrap.ActiveController.InterfaceBlocked, "Closing frame cannot click through");
                        break;
                    case 8:
                        Require(!bootstrap.ActiveController.InterfaceBlocked, "Input restored on following frame");
                        ui.enabled = false;
                        Require(!UiInputGate.Blocks(bootstrap), "Disable releases ownership");
                        Debug.Log("WP-33 ALL UI CHECKS PASSED: real runtime UI Toolkit, five art assets, RU/EN at 1280x720 and 1920x1080, modal/focus/Esc, blocked battle commands, 20 presentation switches, captures in Logs/wp33-*.png.");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void ValidateInputAndLifecycle()
        {
            var controller = bootstrap.ActiveController;
            var selected = controller.SelectedUnit;
            var position = selected.Position;
            int gold = bootstrap.Guild.Gold;
            for (int i = 0; i < 20; i++)
            {
                ui.SetPreview(false); ui.SetPreview(true);
                int previous = ui.ModalCloseCount;
                Submit(ui.ScreenRoot.Q<Button>("open-modal"));
                Require(ui.IsModalOpen && controller.InterfaceBlocked, "Modal input gate");
                Require(!controller.TryEndTurn() && !controller.TryWaitAction() && !controller.TryUseHealingPotion(), "All battle action APIs reject overlay input");
                var interaction = controller.GetComponent<HexGrid.HexGridInteraction>();
                var selection = interaction.Selected;
                interaction.ProcessPointer(new Vector2(Screen.width / 2f, Screen.height / 2f), true);
                Require(interaction.Selected == selection && !interaction.Hovered.HasValue, "Map input cannot pass through");
                Submit(ui.ScreenRoot.Q<Button>("close-modal"));
                Require(!ui.IsModalOpen && ui.ModalCloseCount == previous + 1, "One handler per button after switching");
            }
            Require(controller.SelectedUnit == selected && selected.Position == position && bootstrap.Guild.Gold == gold,
                "Preview and modal never mutate turn, position or gold");
        }

        private static void VerifyLayout()
        {
            Require(ui.ScreenRoot.panel != null, "Real runtime panel attached");
            var screen = ui.ScreenRoot.worldBound;
            Require(screen.width >= 1200 && screen.height >= 680, "Panel scaled to target: " + screen);
            foreach (var label in ui.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                Rect bounds = label.worldBound;
                Require(bounds.width > 0 && bounds.height > 0 && screen.Contains(bounds.center), "Visible label: " + label.text);
                Vector2 measured = label.MeasureTextSize(label.text, label.contentRect.width,
                    VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(measured.y <= label.contentRect.height + 2, "Unclipped text: " + label.text);
            }
            var portraits = ui.ScreenRoot.Query<VisualElement>(name: "portrait").ToList();
            Require(portraits.Count == 4 && portraits.All(p => p.worldBound.height >= 220), "Four separate illustrated cards");
        }

        private static void SetTarget(int width, int height)
        {
            var previous = target;
            target = new RenderTexture(width, height, 24); target.Create();
            ui.SetValidationTarget(target);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        }

        private static void Capture(string name)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/wp33-" + name + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void Submit(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
        }

        private static void Escape()
        {
            using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape")))
            { evt.target = ui.ScreenRoot; ui.ScreenRoot.SendEvent(evt); }
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-33: " + message); }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Poll;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
