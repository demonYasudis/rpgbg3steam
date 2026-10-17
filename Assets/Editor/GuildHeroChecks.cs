using System;
using System.Collections.Generic;
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
    /// <summary>Real UI commands against isolated saved guilds, including stale controls and payment boundaries.</summary>
    [InitializeOnLoad]
    public static class GuildHeroChecks
    {
        private const string Pending = "GuildTactics.WP35.Pending";
        private static int frames, step;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static GuildPartyScreen ui;
        private static List<Action> steps;
        private static RenderTexture target;
        private static string path;
        static GuildHeroChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }

        [MenuItem("Tools/Guild Tactics/Validate Hero Panels (Play Mode)")]
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
            path = Path.Combine(Path.GetTempPath(), "guild-hero-" + Guid.NewGuid() + ".json");
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (!EditorApplication.isPlaying || ++frames < 25) return;
                frames = 0;
                if (steps == null)
                {
                    bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>(); ui = bootstrap.GuildScreen;
                    ui.ShowParty(); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-35 ALL HERO UI CHECKS PASSED: heal 4/5 gold, resurrect 29/30, hire 19/20, upgrades, duplicate submits, stale command rejection, body/lost states, no living heroes, recruitment exhaustion, focus/Esc, saved hero, RU/EN at two resolutions and new guild confirmation.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-35 step " + step); Finish(error); }
        }
        private static GuildAdventurer Hero => bootstrap.Guild.Roster.First(h => h.Id == "warrior-1");
        private static List<Action> Scenarios() => new List<Action>
        {
            () => Fixture(4),
            () => Submit("open-hero"),
            () => { Require(!Button("hero-heal").enabledInHierarchy && Label("hero-heal-reason").Contains("5"), "4 gold cannot heal, price explained"); Capture("1280-poor-ru"); },
            () => Escape(),
            () => Fixture(5, experience: 100),
            () => Submit("open-hero"),
            () => { VerifyModal(); Capture("1280-hero-ru"); SubmitTwice("hero-heal"); },
            () => { Require(Hero.Health == 20 && bootstrap.Guild.Gold == 0 && !Button("hero-heal").enabledInHierarchy, "Heal pays exactly 5 once"); SubmitTwice("hero-upgrade-attack"); },
            () => { Require(Hero.TrainingAttack == 1 && Hero.AvailableUpgrades == 0 && !Button("hero-upgrade-defense").enabledInHierarchy, "One upgrade spent once"); Roundtrip(); Escape(); },
            () => { Require(ui.SelectedId == "warrior-1" && ui.ScreenRoot.panel.focusController.focusedElement == Button("open-hero"), "Close restores hero and opener focus"); Fixture(29, status: 1); },
            () => { Submit("open-reserve"); },
            () => Submit("reserve-details-warrior-1"),
            () => { Require(ui.IsHeroDetailsOpen && !Button("hero-resurrect").enabledInHierarchy && Label("hero-resurrect-reason").Contains("30"), "29 gold cannot resurrect recovered body"); Capture("1280-body-ru"); Escape(); },
            () => { Require(ui.IsReserveOpen && ui.SelectedId == "warrior-1" && ui.ScreenRoot.panel.focusController.focusedElement == Button("reserve-details-warrior-1"), "Details return to reserve and focus"); Fixture(30, status: 1); },
            () => Submit("open-hero"),
            () => SubmitTwice("hero-resurrect"),
            () => { Require(Hero.Status == AdventurerStatus.Alive && Hero.Health == 20 && bootstrap.Guild.Gold == 0 && bootstrap.Guild.SelectedIds.Count == 3, "Resurrection pays once, does not silently add to party"); Roundtrip(); Fixture(100, status: 2); },
            () => Submit("open-hero"),
            () => { Require(!Button("hero-heal").enabledInHierarchy && !Button("hero-resurrect").enabledInHierarchy && !Button("hero-upgrade-attack").enabledInHierarchy, "Lost hero cannot heal, resurrect or train"); Capture("1280-lost-ru"); Fixture(19); },
            () => Submit("open-recruitment"),
            () => { Require(!Button("hire-warrior-3").enabledInHierarchy && Label("hire-warrior-3-reason").Contains("20"), "19 gold cannot hire"); Fixture(20); },
            () => Submit("open-recruitment"),
            () => { VerifyModal(); Capture("1280-recruit-ru"); SubmitTwice("hire-warrior-3"); },
            () => { Require(bootstrap.Guild.Gold == 0 && bootstrap.Guild.Roster.Count == 9 && bootstrap.Guild.Candidates.Count == 3 && ui.SelectedId == "warrior-3", "Hire pays once and selects stable new identity"); Roundtrip(); Fixture(100, experience: 250); },
            () => Submit("open-hero"),
            () => { SubmitTwice("hero-upgrade-defense"); },
            () => { Require(Hero.TrainingDefense == 1 && Hero.AvailableUpgrades == 1, "Duplicate submit cannot spend two available upgrades"); Submit("hero-upgrade-attack"); },
            () => { Require(Hero.TrainingAttack == 1 && Hero.AvailableUpgrades == 0, "Separate later command spends next upgrade"); Fixture(5); },
            () => Submit("open-hero"),
            () => { Require(bootstrap.Guild.TryHeal("warrior-1"), "Simulate model change before stale UI command"); Submit("hero-heal"); },
            () => { Require(Hero.Health == 20 && bootstrap.Guild.Gold == 0 && Label("command-feedback").Contains("отклонена"), "Rejected stale command refreshes values and explains refusal"); Fixture(100, noLiving: true); },
            () => Submit("open-recruitment"),
            () => { Require(!bootstrap.Guild.CanLaunch && Button("hire-warrior-3").enabledInHierarchy, "No living heroes can recover via recruitment"); Submit("hire-warrior-3"); },
            () => Submit("hire-rogue-3"),
            () => Submit("hire-ranger-3"),
            () => Submit("hire-mage-3"),
            () => { Require(bootstrap.Guild.Roster.Count(h => h.Status == AdventurerStatus.Alive) == 4 && ui.ScreenRoot.Q<Label>("recruitment-empty") != null, "All candidates hired, empty offers explained"); Localization.SetLanguage(InterfaceLanguage.English, false); SetTarget(1920, 1080); },
            () => { VerifyModal(); Capture("1920-recruit-empty-en"); Escape(); },
            () => Fixture(100, experience: 250),
            () => Submit("open-hero"),
            () => { VerifyModal(); Capture("1920-hero-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); },
            () => { VerifyModal(); Capture("1920-hero-ru"); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.English, false); },
            () => { VerifyModal(); Capture("1280-hero-en"); Fixture(0, noLiving: true); },
            () => Submit("open-recruitment"),
            () => { Require(ui.ScreenRoot.Q<Label>("recovery-reason") != null, "Unrecoverable guild has explanation"); Submit("new-guild"); },
            () => { Require(bootstrap.Guild.Gold == 0 && Hero.Status == AdventurerStatus.Lost, "New guild requires in-game confirmation"); Capture("1280-new-guild-en"); Escape(); },
            () => { Require(Button("new-guild").enabledInHierarchy, "Cancel keeps old guild"); Submit("new-guild"); },
            () => SubmitTwice("confirm-new-guild"),
            () => { Require(bootstrap.Guild.Gold == 100 && bootstrap.Guild.SelectedIds.Count == 4 && ui.SelectedId == "warrior-1" && !ui.IsHeroDetailsOpen, "Confirmed new guild rebinds UI"); }
        };

        private static void Fixture(int gold, int status = 0, int experience = 0, bool noLiving = false)
        {
            var data = GuildSaveData.Capture(new GuildState(gold), bootstrap.Expeditions);
            var hero = data.roster.First(h => h.id == "warrior-1"); hero.status = status; hero.health = status == 0 ? 8 : 0; hero.experience = experience;
            if (status != 0) data.selected = data.selected.Where(id => id != hero.id).ToArray();
            if (noLiving) { foreach (var entry in data.roster) { entry.status = 2; entry.health = 0; } data.selected = new string[0]; }
            File.WriteAllText(path, JsonUtility.ToJson(data));
            Require(new GuildSaveStore(path).TryLoad(out var loaded, out _, out _), "Fixture passes real save validation");
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, loaded);
        }
        private static void Roundtrip()
        {
            var store = new GuildSaveStore(path);
            string before = JsonUtility.ToJson(GuildSaveData.Capture(bootstrap.Guild, bootstrap.Expeditions));
            Require(store.TrySave(bootstrap.Guild, bootstrap.Expeditions, out _), "Save changed hero");
            Require(store.TryLoad(out var loaded, out var offers, out _), "Load changed hero");
            Require(before == JsonUtility.ToJson(GuildSaveData.Capture(loaded, offers)), "Gold, HP, state, training, roster and party survive load exactly");
        }
        private static void VerifyModal()
        {
            var dialog = ui.ScreenRoot.Q("dialog"); var screen = ui.ScreenRoot.worldBound;
            Require(dialog != null && UiInputGate.Blocks(bootstrap) && screen.Contains(dialog.worldBound.min) && screen.Contains(dialog.worldBound.max - Vector2.one), "Modal fits screen and owns input");
            var scroll = ui.ScreenRoot.Q<ScrollView>("hero-panel-scroll");
            Require(scroll.worldBound.height > 100 && Button("close-hero-panel").enabledInHierarchy, "Scrollable content and accessible close");
            foreach (var label in dialog.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Unclipped text: " + label.text);
            }
        }
        private static Button Button(string name) => ui.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing " + name);
        private static string Label(string name) => ui.ScreenRoot.Q<Label>(name)?.text ?? "";
        private static void Submit(string name) { var button = Button(name); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void SubmitTwice(string name) { Submit(name); Submit(name); }
        private static void Escape() { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = ui.ScreenRoot; ui.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); ui.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp35-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-35: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (path != null) foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
