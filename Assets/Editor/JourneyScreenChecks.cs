using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Meta;
using GuildTactics.UI;
using GuildTactics.Units;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.Editor
{
    [InitializeOnLoad]
    public static class JourneyScreenChecks
    {
        private const string Pending = "GuildTactics.WP41.Pending";
        private static int frames, step, gold, itemCount;
        private static double deadline;
        private static GameBootstrap owner;
        private static JourneyScreen ui;
        private static RenderTexture target;
        private static List<Action> steps;
        private static string path;
        private static GuildSaveStore store;
        private static Button retained;
        private static ExpeditionResult final;
        static JourneyScreenChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }
        [MenuItem("Tools/Guild Tactics/Validate Expedition Events and Results (Play Mode)")]
        public static void RunBatch()
        {
            try { EditorSceneManager.OpenScene("Assets/Scenes/TacticalPrototype.unity"); SessionState.SetBool(Pending, true); Begin(); EditorApplication.EnterPlaymode(); }
            catch (Exception error) { Finish(error); }
        }
        private static void Begin()
        {
            deadline = EditorApplication.timeSinceStartup + 200;
            path = Path.Combine(Path.GetTempPath(), "wp41-" + Guid.NewGuid().ToString("N") + ".json");
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (!EditorApplication.isPlaying || ++frames < 30) return; frames = 0;
                if (steps == null)
                {
                    owner = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>(); ui = owner.ExpeditionScreen;
                    store = new GuildSaveStore(path); SetStore(store);
                    var offer = ExpeditionSelection.Offers[0];
                    int seed = Enumerable.Range(1, 200).First(value => EncounterGenerator.Generate(DungeonGenerator.Generate(value, offer.CreateDungeonConfig()), offer.CreateEncounterConfig()).All(e => e.Archetype.Unit.Initiative < 3));
                    typeof(GameBootstrap).GetProperty("Expeditions").SetValue(owner, new ExpeditionSelection(seed, 0, 0));
                    ui.Show(); owner.GuildScreen.ShowParty(); owner.BattleScreen.Show();
                    SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-41 ALL JOURNEY UI CHECKS PASSED: modal input/cancel/restore, retreat and duplicate confirms, complete two-section expedition, body recovery/loss, pending/resolved events, checkpoint failure/retry/reload, result reopen/duplicate payout, storage, XP, final save failure/retry, defeat, RU/EN and both resolutions.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-41 step " + step); Finish(error); }
        }
        private static List<Action> Scenarios() => new List<Action>
        {
            () => Require(owner.TryLaunchExpedition(), "First real launch"),
            () => { PlayerTurn(); ReachExit(); Controller.GetComponent<ExpeditionUI>().RequestRetreat(); },
            () => { Require(ui.IsOpen && UiInputGate.Blocks(Controller) && !owner.BattleScreen.IsOpen, "Modal owns input"); BlockMap(); CheckLayout(); Capture("retreat-1280-ru"); Escape(); },
            () => { Require(!ui.IsOpen && Controller.CanPlayerAct && owner.BattleScreen.IsOpen, "Cancel restores idle battle"); Controller.GetComponent<ExpeditionUI>().RequestRetreat(); },
            () => { retained = Find("journey-confirm"); Submit(retained); Submit(retained); Require(Controller.Expedition.Result.Outcome == ExpeditionOutcome.Retreated, "Exactly one retreat"); },
            () => { Require(ui.IsOpen && ui.ScreenRoot.Query<VisualElement>(className: "journey-hero").ToList().Count == 4, "Retreat portraits"); CheckLayout(); Capture("retreated-1280-ru"); retained = Find("journey-return"); Submit(retained); Submit(retained); },
            () => { Require(!owner.Guild.IsAway && owner.Guild.Gold == 100 && owner.Guild.Roster.Where(h => owner.Guild.SelectedIds.Contains(h.Id)).All(h => h.Experience == 25), "Retreat XP once"); Require(store.TryLoad(out var loaded, out _, out _) && loaded.Gold == 100, "Actual return saved"); Require(owner.TryLaunchExpedition(), "Repeated expedition"); },
            () => { PlayerTurn(); var mage = Controller.Units.First(u => u.Definition.Id == "mage"); mage.ApplyDamage(mage.CurrentHealth); SetStore(new GuildSaveStore(Path.Combine(path, "blocked.json"))); CompleteSection(); },
            () => { Require(owner.JourneyBoundary != null && !owner.BoundarySaved && ui.ScreenRoot.Q("journey-save-error") != null && !Find("journey-skip").enabledInHierarchy && !Find("journey-return").enabledInHierarchy && !Find("journey-continue").enabledInHierarchy, "Checkpoint error visible and event cannot bypass save"); BlockMap(); CheckLayout(); Capture("save-error-1280-ru"); SetStore(store); Submit("journey-retry"); },
            () => { Require(owner.BoundarySaved && owner.Journey.EventPending, "Checkpoint retried"); CheckLayout(); Capture("event-1280-ru"); retained = Find("journey-skip"); Submit(retained); Submit(retained); },
            () => { Require(!owner.Journey.EventPending && owner.Journey.LastEvent.choice == 0 && owner.Guild.Gold == 100, "Event resolves once with unpaid rewards");
                Require(store.TryLoad(out var loaded, out _, out _) && store.LoadedCheckpoint != null && loaded.Gold == 100, "Checkpoint reload retains unpaid guild");
                var resumed = ExpeditionJourney.Restore(store.LoadedCheckpoint, loaded); Require(!resumed.EventPending && resumed.LastEvent.choice == 0 && !resumed.TryResolveEvent(loaded, 0), "Reload cannot apply event twice");
                Require(ui.ScreenRoot.Q<Label>("result-status-mage-1").text.Contains("тело"), "Recovered body status shown"); ui.ShowLegacy(); ui.Show(); },
            () => { Require(ui.IsOpen && owner.Guild.Gold == 100, "Reopening result never pays rewards"); RestoreBoundary(); Localization.SetLanguage(InterfaceLanguage.English, false); SetTarget(1920, 1080); },
            () => { CheckLayout(); Capture("boundary-1920-en"); Submit("journey-continue"); },
            () => { Require(!ui.IsOpen && Controller.Units.Count == 3 && Controller.CanPlayerAct, "Continuation restores input and keeps corpse dead"); PlayerTurn(); CompleteSection(); },
            () => { Require(owner.Journey.EventPending, "Final section event"); var hero = owner.JourneyBoundary.Adventurers.Where(h => h.Survived).OrderByDescending(h => h.Health).First(); retained = Find("journey-investigate-" + hero.InstanceId); Submit(retained); Submit(retained); },
            () => { Require(owner.Journey.Completed == owner.Journey.Sections && ui.ScreenRoot.Q("journey-continue") == null && owner.Journey.LastEvent.choice == 1 && ui.ScreenRoot.Q<Label>("journey-event-outcome").text.Contains("d20("), "Full expedition complete with one investigation roll"); final = owner.JourneyBoundary; gold = owner.Guild.Gold; itemCount = owner.Guild.Inventory.Count; CheckLayout(); Capture("complete-1920-en"); SetStore(new GuildSaveStore(Path.Combine(path, "blocked.json"))); retained = Find("journey-return"); Submit(retained); Submit(retained); },
            () => { Require(!owner.Guild.IsAway && owner.Guild.Gold == gold + final.Gold && owner.Guild.Inventory.Count == itemCount + final.Items.Count && owner.Guild.Roster.Single(h => h.Id == "mage-1").Status == AdventurerStatus.BodyRecovered, "Rewards and corpse returned once");
                Require(owner.Guild.Roster.Single(h => h.Id == "rogue-1").Experience == 125 && !owner.TryReturnToGuild(), "Extraction XP once");
                Require(owner.GuildScreen.ScreenRoot.Q<Label>("launch-reason").text.Contains("save") && owner.GuildScreen.ScreenRoot.Q<Button>("guild-retry-save") != null, "Failed final write visible in guild");
                SetStore(store); SubmitGuild("guild-retry-save"); },
            () => { Require(string.IsNullOrEmpty(owner.SaveMessage) && store.TryLoad(out var loaded, out _, out _) && loaded.Gold == owner.Guild.Gold && store.LoadedCheckpoint == null && loaded.Inventory.Count == owner.Guild.Inventory.Count, "Retry saves already-paid state without duplication"); SubmitGuild("guild-tab-1"); },
            () => { Require(final.Items.All(i => owner.GuildScreen.ScreenRoot.Q("storage-item-" + i.Id) != null), "Earned loot available in visual storage");
                Require(owner.Guild.TryResurrect("mage-1") && owner.Guild.TryToggleSelection("mage-1"), "Prepare next full party"); Require(owner.TryLaunchExpedition(), "Third expedition"); },
            () => { PlayerTurn(); var mage = Controller.Units.First(h => h.Definition.Id == "mage"); mage.ApplyDamage(mage.CurrentHealth); owner.Grid.GetCell(mage.Position).Terrain = TerrainType.Pit; PrepareExtraction(); Controller.GetComponent<ExpeditionUI>().RequestExtraction(); },
            () => { Require(ui.IsOpen && ui.ScreenRoot.Q("journey-confirm-warning") != null && Controller.Expedition.Result == null, "Unrecoverable body needs confirmation"); Capture("abandonment-1920-en"); Escape(); },
            () => { Require(!ui.IsOpen && Controller.Expedition.Result == null && Controller.CanPlayerAct, "Abandonment cancellation keeps expedition"); Controller.GetComponent<ExpeditionUI>().RequestExtraction(); },
            () => { retained = Find("journey-confirm"); Submit(retained); Submit(retained); },
            () => { Require(!owner.JourneyBoundary.Adventurers.Single(h => h.InstanceId == "mage-1").BodyRecovered, "Confirmed abandoned body stays lost"); Submit("journey-skip"); },
            () => Submit("journey-return"),
            () => { Require(owner.Guild.Roster.Single(h => h.Id == "mage-1").Status == AdventurerStatus.Lost, "Lost body never returns"); var reserve = owner.Guild.Roster.First(h => h.Status == AdventurerStatus.Alive && !owner.Guild.SelectedIds.Contains(h.Id)); Require(owner.Guild.TryToggleSelection(reserve.Id) && owner.TryLaunchExpedition(), "Fourth expedition with reserve" ); },
            () => { PlayerTurn(); foreach (var hero in Controller.Units) hero.ApplyDamage(hero.CurrentHealth); Controller.Expedition.RefreshOutcome(); },
            () => { Require(ui.IsOpen && Controller.Expedition.Result.Outcome == ExpeditionOutcome.Defeated && Controller.Expedition.Result.Items.Count == 0, "Defeat result"); CheckLayout(); Capture("defeat-1920-en"); Submit("journey-return"); },
            () => { Require(!owner.Guild.IsAway && owner.Guild.Roster.Where(h => final.Adventurers.Any(a => a.InstanceId == h.Id)).All(h => h.Status == AdventurerStatus.Lost), "Defeated party never silently reappears"); Require(!ui.IsOpen && owner.GuildScreen.IsOpen, "Defeat has a path to guild"); }
        };
        private static PlayerUnitController Controller => owner.ActiveController;
        private static void RestoreBoundary()
        {
            Require(store.TryLoad(out var guild, out var expeditions, out _) && store.LoadedCheckpoint != null, "Load actual boundary");
            var handler = (Action)Delegate.CreateDelegate(typeof(Action), owner, typeof(GameBootstrap).GetMethod("SaveProgress", BindingFlags.Instance | BindingFlags.NonPublic));
            owner.Guild.Changed -= handler;
            var old = Controller.gameObject; old.SetActive(false); UnityEngine.Object.Destroy(old);
            typeof(GameBootstrap).GetField("presentation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, null);
            typeof(GameBootstrap).GetProperty("ActiveController").SetValue(owner, null);
            typeof(GameBootstrap).GetProperty("Guild").SetValue(owner, guild);
            typeof(GameBootstrap).GetProperty("Expeditions").SetValue(owner, expeditions);
            guild.Changed += handler;
            owner.RestoreJourney(store.LoadedCheckpoint);
            Require(owner.ActiveController == null && owner.JourneyBoundary != null && owner.Guild.IsAway, "Restore has no live battle objects");
        }
        private static void PlayerTurn()
        {
            var controller = Controller;
            for (int i = 0; i < controller.Turns.Order.Count && controller.SelectedUnit.Team != UnitTeam.Player; i++)
            {
                if (controller.Turns.State == TurnState.ResolvingAction) controller.Turns.TryCompleteAction(controller.SelectedUnit);
                Require(controller.Turns.TryEndTurn(controller.SelectedUnit), "Pass fixture enemy turn");
                typeof(PlayerUnitController).GetMethod("StartNextTurn", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            }
        }
        private static void ReachExit()
        {
            var exit = Controller.Expedition.Extraction; var actor = Controller.SelectedUnit;
            var occupant = Controller.Units.FirstOrDefault(h => h.IsAlive && h != actor && h.Position == exit);
            if (occupant != null) Require(occupant.TryRelocate(owner.Grid, owner.Grid.Cells.First(c => !c.IsOccupied && TerrainRules.CanWalk(c.Terrain)).Coordinates), "Free EXIT");
            if (actor.Position != exit) Require(actor.TryRelocate(owner.Grid, exit), "Reach EXIT");
        }
        private static void CompleteSection()
        {
            PrepareExtraction(); Require(Controller.TryExtract(true), "Real extraction command");
        }
        private static void PrepareExtraction()
        {
            foreach (var enemy in Controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            if (Controller.Expedition.Mission.RequiresRelic)
            {
                Require(Controller.SelectedUnit.TryRelocate(owner.Grid, Controller.Expedition.Chest), "Reach RELIC");
                Controller.Visibility.Refresh(); Require(Controller.TryOpenChest(), "Real relic command");
            }
            ReachExit();
        }
        private static void BlockMap()
        {
            var input = Controller.GetComponent<HexGridInteraction>(); var position = (Controller.SelectedUnit ?? Controller.Units[0]).Position;
            input.ProcessPointer(input.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(position)), true);
            Require(!input.Hovered.HasValue && !Controller.TryWaitAction() && !Controller.TryEndTurn(), "Overlay blocks map/actions");
        }
        private static void CheckLayout()
        {
            var root = ui.ScreenRoot.worldBound;
            foreach (var button in ui.ScreenRoot.Query<Button>().ToList().Where(b => b.name.StartsWith("journey-")))
                if (button.parent.name != "" || button.name == "journey-return") Require(button.worldBound.width > 0, "Visible button " + button.name);
            foreach (var label in ui.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Unclipped " + label.name + " required=" + size + " actual=" + label.contentRect);
            }
            Require(root.Contains(Find("journey-return")?.worldBound.center ?? Find("journey-cancel").worldBound.center), "Footer stays on screen");
        }
        private static void SetStore(GuildSaveStore value) => typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static Button Find(string name) => ui.ScreenRoot?.Q<Button>(name);
        private static void Submit(string name) { var button = Find(name); Require(button != null && button.enabledInHierarchy, "Available " + name); Submit(button); }
        private static void Submit(Button button) { using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void SubmitGuild(string name) { var button = owner.GuildScreen.ScreenRoot.Q<Button>(name); Require(button != null && button.enabledInHierarchy, "Guild " + name); Submit(button); }
        private static void Escape() { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = ui.ScreenRoot; ui.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        {
            var old = target; target = new RenderTexture(width, height, 24); target.Create();
            var camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.name != "Preview Camera");
            camera.targetTexture = target; camera.ResetAspect(); ui.SetValidationTarget(target); owner.GuildScreen.SetValidationTarget(target); owner.BattleScreen.SetValidationTarget(target);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
        }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes("Logs/wp41-" + name + ".png", image.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-41: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
