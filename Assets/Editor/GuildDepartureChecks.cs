using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Meta;
using GuildTactics.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.Editor
{
    [InitializeOnLoad]
    public static class GuildDepartureChecks
    {
        private const string Pending = "GuildTactics.WP38.Pending";
        private static int frames, step, launches, seed;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static GuildPartyScreen ui;
        private static List<Action> steps;
        private static RenderTexture target;
        private static Button retainedLaunch;
        private static GuildState departingGuild;
        private static string path;
        static GuildDepartureChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }

        [MenuItem("Tools/Guild Tactics/Validate Expedition Departure UI (Play Mode)")]
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
            path = Path.Combine(Path.GetTempPath(), "guild-departure-" + Guid.NewGuid() + ".json");
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
                    typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, null);
                    Fixture(4); ui.ShowParty(); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-38 ALL DEPARTURE UI CHECKS PASSED: four visual offers and truthful briefing, real party/HP, tab/back/Esc selection, incomplete 0/3 party, save/load, stale party/choice/retained controls, duplicate submits, all four actual missions/biomes and repeated departure after return, RU/EN at 1280x720 and 1920x1080.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-38 step " + step); Finish(error); }
        }
        private static List<Action> Scenarios()
        {
            var list = new List<Action>
            {
                () => Submit("choose-expedition"),
                () => { VerifyLayout(); VerifyBriefing(); Require(ui.ScreenRoot.Query<ExpeditionIllustration>().ToList().Count == ExpeditionSelection.Offers.Count, "One independent illustration per offer"); Capture("1280-ru"); Submit("expedition-3"); },
                () => { VerifyBriefing(); Require(bootstrap.Expeditions.SelectedIndex == 3, "Cellar selection"); Roundtrip(); Submit("guild-tab-1"); },
                () => Submit("guild-tab-2"),
                () => { Require(bootstrap.Expeditions.SelectedIndex == 3 && ui.SelectedId == "warrior-1", "Tabs retain expedition and hero"); Escape(); },
                () => { Require(ui.ScreenRoot.panel.focusController.focusedElement == Button("choose-expedition"), "Esc returns focus to party opener"); Submit("choose-expedition"); },
                () => Submit("travel-back"),
                () => { Require(bootstrap.Expeditions.SelectedIndex == 3 && !bootstrap.Guild.IsAway, "Back preserves choice, no launch"); Fixture(3); },
                () => Submit("guild-tab-2"),
                () => { Require(!Button("launch-expedition").enabledInHierarchy && !bootstrap.TryLaunchExpedition() && ui.ScreenRoot.Q<Label>("travel-empty-3") != null, "Three heroes cannot depart"); Require(Label("travel-launch-reason").Contains("четырёх"), "Visible blocking reason"); Capture("1280-incomplete-ru"); Fixture(0); },
                () => { Require(!Button("launch-expedition").enabledInHierarchy && !bootstrap.TryLaunchExpedition(), "Zero party cannot bypass model"); Fixture(4); },
                () => { Require(Label("travel-health-warrior-1").Contains("8/20"), "Actual saved wounds in party preview"); int other = (bootstrap.Expeditions.SelectedIndex + 1) % ExpeditionSelection.Offers.Count; Submit("expedition-" + other); Submit("launch-expedition"); },
                () => { Require(!bootstrap.Guild.IsAway && Label("travel-feedback").Contains("Выбор изменился"), "Same-frame changed choice cannot launch stale briefing"); Require(bootstrap.Guild.TryToggleSelection("mage-1"), "External party change"); Submit("launch-expedition"); },
                () => { Require(!bootstrap.Guild.IsAway && !Button("launch-expedition").enabledInHierarchy && Label("travel-feedback").Contains("отклонена"), "Stale party safely declined"); Fixture(4); },
                () => { Localization.SetLanguage(InterfaceLanguage.English, false); },
                () => { VerifyLayout(); Capture("1280-en"); SetTarget(1920, 1080); },
                () => { VerifyLayout(); Capture("1920-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); },
                () => { VerifyLayout(); Capture("1920-ru"); SetTarget(1280, 720); }
            };
            for (int i = 0; i < ExpeditionSelection.Offers.Count; i++)
            {
                int index = i;
                list.Add(() => Submit("guild-tab-2"));
                list.Add(() => Submit("expedition-" + index));
                list.Add(() =>
                {
                    VerifyBriefing(); VerifyLayout(); Capture("offer-" + index + "-ru");
                    launches = bootstrap.Expeditions.LaunchedCount; seed = bootstrap.Expeditions.NextSeed;
                    departingGuild = bootstrap.Guild;
                    retainedLaunch = Button("launch-expedition"); Submit(retainedLaunch); Submit(retainedLaunch);
                });
                list.Add(() =>
                {
                    var offer = ExpeditionSelection.Offers[index];
                    Require(bootstrap.Guild.IsAway && !ui.IsOpen && !UiInputGate.Blocks(bootstrap), "Actual departure releases input");
                    Require(bootstrap.Expeditions.SelectedIndex == index && bootstrap.Expeditions.LaunchedCount == launches + 1 && bootstrap.Expeditions.NextSeed != seed, "Exactly one selected launch and one seed advance");
                    Require(bootstrap.Dungeon.Biome == offer.Biome && bootstrap.Journey.Sections == offer.Sections && bootstrap.ActiveController.Expedition.Mission == offer.Mission, "Actual selected mission, biome and section count");
                    Require(bootstrap.ActiveController.Expedition.Party.Select(h => h.InstanceId).OrderBy(id => id).SequenceEqual(bootstrap.Guild.SelectedIds.OrderBy(id => id)) && bootstrap.ActiveController.Expedition.Party.First(h => h.InstanceId == "warrior-1").CurrentHealth == 8, "Preview party and wounds are sent (combat sorts by initiative)");
                    // Arrange only position through the domain; retreat/return use real controller/bootstrap commands.
                    var controller = bootstrap.ActiveController;
                    var exit = controller.Expedition.Extraction;
                    var actor = controller.Turns.ActiveUnit;
                    var occupant = controller.Expedition.Party.FirstOrDefault(h => h != actor && h.Position == exit);
                    if (occupant != null) Require(occupant.TryRelocate(bootstrap.Grid, bootstrap.Grid.Cells.First(c => HexGrid.TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates), "Free the exit");
                    Require(actor.TryRelocate(bootstrap.Grid, exit) && controller.TryRetreat(true), "Retreat from exit");
                });
                list.Add(() => Require(bootstrap.TryReturnToGuild(), "Return after retreat"));
                list.Add(() =>
                {
                    Require(ui.IsOpen && !bootstrap.Guild.IsAway && bootstrap.Guild.SelectedIds.Count == 4 && ReferenceEquals(bootstrap.Guild, departingGuild), "Same guild and survivors can depart again");
                    Submit(retainedLaunch);
                    Require(!bootstrap.Guild.IsAway && bootstrap.Expeditions.LaunchedCount == launches + 1, "Detached old launch button cannot trigger another expedition");
                });
            }
            return list;
        }
        private static void Fixture(int partySize)
        {
            var data = GuildSaveData.Capture(new GuildState(), bootstrap.Expeditions);
            data.selected = data.selected.Take(partySize).ToArray(); data.roster.First(h => h.id == "warrior-1").health = 8;
            var guild = GuildState.Restore(data);
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, guild);
        }
        private static void Roundtrip()
        {
            var store = new GuildSaveStore(path);
            Require(store.TrySave(bootstrap.Guild, bootstrap.Expeditions, out _), "Save expedition selection");
            Require(store.TryLoad(out var guild, out var selection, out _) && selection.SelectedIndex == bootstrap.Expeditions.SelectedIndex && selection.NextSeed == bootstrap.Expeditions.NextSeed, "Load exact selection/seed");
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, guild);
            typeof(GameBootstrap).GetProperty("Expeditions").SetValue(bootstrap, selection);
        }
        private static void VerifyBriefing()
        {
            var offer = bootstrap.Expeditions.Selected;
            Require(Label("travel-selected-name") == Localization.T(offer.Name), "Selected offer title");
            Require(Label("travel-reward").Contains(offer.Mission.MinimumGold + "–" + offer.Mission.MaximumGold) && Label("travel-risk").Contains(offer.Sections.ToString()), "Reward range and section count from model");
            Require(Label("offer-mission-" + bootstrap.Expeditions.SelectedIndex) == Localization.T(offer.Mission.Name), "Actual objective name");
            Require(ui.ScreenRoot.Query<Button>().ToList().Count(b => b.name == "launch-expedition") == 1, "One departure action");
        }
        private static void VerifyLayout()
        {
            var screen = ui.ScreenRoot.worldBound;
            foreach (string name in new[] { "guild-expeditions", "travel-offers", "travel-details", "launch-expedition", "travel-back" })
            {
                var bounds = ui.ScreenRoot.Q(name).worldBound;
                Require(bounds.width > 100 && bounds.height > 20 && screen.Contains(bounds.min) && screen.Contains(bounds.max - Vector2.one), "Visible control: " + name + " " + bounds);
            }
            var cards = ui.ScreenRoot.Query<Button>().ToList().Where(b => b.name.StartsWith("expedition-")).ToArray();
            Require(cards.Length == 4 && cards.All(c => c.worldBound.width > 250), "Four readable offer cards");
            foreach (var label in ui.ScreenRoot.Query<Label>(className: "travel-label").ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Unclipped label: " + label.name + " " + label.text);
            }
        }
        private static Button Button(string name) => ui.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing " + name);
        private static string Label(string name) => ui.ScreenRoot.Q<Label>(name)?.text ?? "";
        private static void Submit(string name) => Submit(Button(name));
        private static void Submit(Button button) { using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void Escape() { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = ui.ScreenRoot; ui.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); ui.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp38-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-38: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (path != null) foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
