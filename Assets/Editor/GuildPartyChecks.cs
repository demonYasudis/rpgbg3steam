using System;
using System.IO;
using System.Linq;
using GuildTactics.Core;
using GuildTactics.Meta;
using GuildTactics.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.Editor
{
    /// <summary>Real retained UI events, isolated saves and Play Mode layout checks for WP-34.</summary>
    [InitializeOnLoad]
    public static class GuildPartyChecks
    {
        private const string Pending = "GuildTactics.WP34.Pending";
        private static int frames, stage;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static GuildPartyScreen ui;
        private static RenderTexture target;
        private static readonly string fixturePath = Path.Combine(Path.GetTempPath(), "guild-party-" + Guid.NewGuid() + ".json");

        static GuildPartyChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }

        [MenuItem("Tools/Guild Tactics/Validate Guild Party (Play Mode)")]
        public static void RunBatch()
        {
            try
            {
                Require(!EditorApplication.isPlaying, "Start outside Play Mode");
                EditorSceneManager.OpenScene("Assets/Scenes/TacticalPrototype.unity");
                SessionState.SetBool(Pending, true); Begin(); EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Begin()
        {
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (!EditorApplication.isPlaying || ++frames < 25) return;
                frames = 0;
                switch (stage++)
                {
                    case 0:
                        bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                        ui = bootstrap.GuildScreen;
                        Require(ui != null, "Bootstrap wires party screen");
                        Localization.SetLanguage(InterfaceLanguage.Russian, false);
                        SetTarget(1280, 720); ui.ShowParty(); break;
                    case 1:
                        Capture("1280-ru"); VerifyLayout();
                        foreach (string id in bootstrap.Guild.SelectedIds.ToArray()) Submit("remove-" + id);
                        break;
                    case 2:
                        Require(bootstrap.Guild.SelectedIds.Count == 0 && !bootstrap.Guild.CanLaunch, "Zero party");
                        Require(ui.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("empty-slot-")) == 4, "Four empty slots");
                        Require(!Button("choose-expedition").enabledInHierarchy && !bootstrap.TryLaunchExpedition(), "Empty party cannot depart via UI or API");
                        Capture("1280-empty-ru"); Submit("guild-tab-2"); break;
                    case 3:
                        Require(!Button("launch-expedition").enabledInHierarchy, "Expedition tab cannot bypass party rule");
                        Submit("guild-tab-0"); ui.OpenReserve(); break;
                    case 4:
                        Require(ui.IsReserveOpen && UiInputGate.Blocks(bootstrap), "Reserve owns input");
                        Submit("reserve-toggle-warrior-1"); break;
                    case 5:
                        Require(bootstrap.Guild.SelectedIds.Count == 1 && !bootstrap.Guild.CanLaunch, "One hero cannot depart");
                        Submit("reserve-select-warrior-2"); break;
                    case 6:
                        Require(ui.SelectedId == "warrior-2", "Inspect reserve hero by stable identity");
                        Capture("1280-reserve-ru"); Escape(); break;
                    case 7:
                        Require(!ui.IsReserveOpen && ui.SelectedId == "warrior-2", "Return preserves inspected reserve hero");
                        Require(ui.ScreenRoot.panel.focusController.focusedElement == Button("open-reserve"), "Reserve returns focus");
                        ui.OpenReserve(); break;
                    case 8:
                        Submit("reserve-toggle-warrior-2"); Submit("reserve-toggle-rogue-1"); Submit("reserve-toggle-ranger-1"); break;
                    case 9:
                        Require(bootstrap.Guild.SelectedIds.Count == 4 && bootstrap.Guild.CanLaunch, "Four heroes, including two of one class");
                        Require(!Button("reserve-toggle-mage-1").enabledInHierarchy, "Full party disables extra additions");
                        Submit("close-reserve"); break;
                    case 10:
                        VerifyLayout();
                        var images = ui.ScreenRoot.Query<UnitMiniature>().ToList();
                        Require(images.Count == 4 && images.All(i => i.sprite != null &&
                            (i.sprite.name.Contains("_Idle_") || i.sprite.name.Contains("_Defend_"))), "Four actual UnitSpriteSheet idle/selection frames");
                        Require(Button("party-card-warrior-1").Q<Label>().text != Button("party-card-warrior-2").Q<Label>().text, "Same class identities distinguishable");
                        Capture("1280-two-warriors-ru"); Localization.SetLanguage(InterfaceLanguage.English, false); break;
                    case 11:
                        VerifyLayout(); Capture("1280-en"); SetTarget(1920, 1080); break;
                    case 12:
                        VerifyLayout(); Capture("1920-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); break;
                    case 13:
                        VerifyLayout(); Capture("1920-ru"); Submit("guild-tab-1"); break;
                    case 14:
                        Require(bootstrap.Guild.SelectedIds.Count == 4 && ui.SelectedId == "ranger-1", "Tabs preserve selection and party");
                        Submit("guild-tab-0"); break;
                    case 15:
                        for (int i = 0; i < 20; i++) { ui.ShowLegacy(); ui.ShowParty(); }
                        Submit("remove-warrior-2"); break;
                    case 16:
                        Require(bootstrap.Guild.SelectedIds.Count == 3, "One command after twenty presentation switches");
                        var store = new GuildSaveStore(fixturePath);
                        Require(store.TrySave(bootstrap.Guild, bootstrap.Expeditions, out _), "Save changed party");
                        Require(store.TryLoad(out var restored, out _, out _) && restored.SelectedIds.SequenceEqual(bootstrap.Guild.SelectedIds), "Party roundtrip");
                        bootstrap.Guild.TryToggleSelection("warrior-2");
                        LoadLargeRoster(); break;
                    case 17:
                        Require(bootstrap.Guild.Roster.Count == 88, "Loaded large reserve fixture");
                        Require(Button("party-card-warrior-1").Q<Label>(className: "party-health").text.Contains("8/20"), "Loaded wounds reflected");
                        ui.OpenReserve(); break;
                    case 18:
                        Require(ui.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("reserve-select-")) == 88, "Entire large reserve accessible");
                        Require(!Button("reserve-toggle-rogue-2").enabledInHierarchy && !Button("reserve-toggle-mage-2").enabledInHierarchy, "Dead/lost cannot join");
                        var list = ui.ScreenRoot.Q<ScrollView>("reserve-scroll"); list.scrollOffset = new Vector2(0, 100000);
                        break;
                    case 19:
                        Require(ui.ScreenRoot.Q<ScrollView>("reserve-scroll").scrollOffset.y > 500, "Large reserve scrolls");
                        Capture("1920-large-reserve-ru"); Submit("reserve-select-warrior-82"); break;
                    case 20:
                        Require(ui.SelectedId == "warrior-82" && ui.ScreenRoot.Q<ScrollView>("reserve-scroll").scrollOffset.y > 500, "Selection retains large-reserve scroll");
                        Escape(); break;
                    case 21:
                        // Names are IDs today; check future long display names in the real label layout too.
                        var name = Button("party-card-warrior-1").Q<Label>(className: "card-title");
                        name.text = "Очень длинное имя героя для проверки карточки отряда";
                        break;
                    case 22:
                        VerifyLayout(); Capture("1920-long-name-ru");
                        ui.ShowLegacy(); bootstrap.GetComponent<UiFoundation>().SetPreview(true); break;
                    case 23:
                        Require(!ui.IsOpen && bootstrap.GetComponent<UiFoundation>().IsOpen, "Workshop and guild never overlap");
                        bootstrap.GetComponent<UiFoundation>().SetPreview(false); ui.ShowParty(); break;
                    case 24:
                        Submit("choose-expedition"); break;
                    case 25:
                        Submit("launch-expedition"); break;
                    case 26:
                        Require(bootstrap.Guild.IsAway && !ui.IsOpen && bootstrap.ActiveController != null, "Depart through real UI");
                        Require(!UiInputGate.Blocks(bootstrap), "Guild releases battle input");
                        foreach (var hero in bootstrap.ActiveController.Expedition.Party) hero.ApplyDamage(hero.CurrentHealth);
                        bootstrap.ActiveController.Expedition.RefreshOutcome(); break;
                    case 27:
                        if (bootstrap.ActiveController.Expedition.Result == null) { stage--; return; }
                        Require(bootstrap.TryReturnToGuild(), "Return through bootstrap after defeat"); break;
                    case 28:
                        Require(ui.IsOpen && !bootstrap.Guild.IsAway && bootstrap.Guild.SelectedIds.Count == 0, "Party screen returns and reflects casualties");
                        Require(bootstrap.TryStartNewGuild(), "New guild replaces model"); break;
                    case 29:
                        Require(ui.IsOpen && bootstrap.Guild.SelectedIds.Count == 4 && ui.SelectedId == "warrior-1", "Replacement model rebinds selection and UI");
                        ui.enabled = false;
                        break;
                    case 30:
                        Require(!UiInputGate.Blocks(bootstrap) && !ui.IsOpen, "Disable releases UI ownership");
                        Debug.Log("WP-34 ALL PARTY UI CHECKS PASSED: 0/1/4 heroes, same classes, shared sprites, reserve/focus/Esc, 88 heroes, dead/lost, scroll retention, saved party, RU/EN at two resolutions, long name, 20 switches, real departure/return and new guild rebind.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void LoadLargeRoster()
        {
            var data = GuildSaveData.Capture(bootstrap.Guild, bootstrap.Expeditions);
            var roster = data.roster.ToList();
            for (int i = 3; i <= 82; i++) roster.Add(new SavedAdventurer { id = "warrior-" + i, definition = "warrior", health = 20 });
            data.roster = roster.ToArray(); data.nextRecruitId = 84; data.candidates = new string[0];
            data.roster.First(h => h.id == "warrior-1").health = 8;
            foreach (string id in new[] { "rogue-2", "mage-2" })
            { var hero = data.roster.First(h => h.id == id); hero.health = 0; hero.status = id == "rogue-2" ? 1 : 2; }
            File.WriteAllText(fixturePath, JsonUtility.ToJson(data));
            Require(new GuildSaveStore(fixturePath).TryLoad(out var loaded, out _, out _), "Large fixture uses real save validation");
            // Test fixture only: swap the bootstrapped guild, as startup loading/new-game does.
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, loaded);
        }

        private static void VerifyLayout()
        {
            Require(ui.IsOpen && ui.ScreenRoot.panel != null, "Attached runtime panel");
            var bounds = ui.ScreenRoot.worldBound;
            var cards = ui.ScreenRoot.Query<Button>().ToList().Where(b => b.name.StartsWith("party-card-")).ToArray();
            Require(cards.Length == 4 && cards.All(c => c.worldBound.width > 180 && bounds.Contains(c.worldBound.center)),
                "Four readable cards: " + bounds + " / " + string.Join("; ", cards.Select(c => c.worldBound.ToString())));
            Require(cards.Zip(cards.Skip(1), (a, b) => a.worldBound.xMax <= b.worldBound.xMin).All(v => v), "Cards do not overlap");
            foreach (var label in ui.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                Require(label.worldBound.width > 0 && label.worldBound.height > 0 && bounds.Contains(label.worldBound.center), "Visible text: " + label.text);
                if (label.ClassListContains("card-title")) continue; // Deliberate single-line ellipsis; full name is in tooltip/reserve.
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(size.y <= label.contentRect.height + 2, "Unclipped text: " + label.text);
            }
        }
        private static Button Button(string name) => ui.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing button " + name);
        private static void Submit(string name)
        { var button = Button(name); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void Escape()
        { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = ui.ScreenRoot; ui.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        {
            var old = target; target = new RenderTexture(width, height, 24); target.Create(); ui.SetValidationTarget(target);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
        }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp34-" + name + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-34: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(fixturePath + suffix)) File.Delete(fixturePath + suffix);
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
