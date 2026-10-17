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
    public static class BattleHudChecks
    {
        private const string Pending = "GuildTactics.WP39.Pending";
        private static int frames, step, clicks;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static PlayerUnitController controller;
        private static HexGridInteraction interaction;
        private static BattleHud hud;
        private static RenderTexture target;
        private static List<Action> steps;
        private static UnitRuntimeState selected, enemy;
        private static long turn;
        private static int movement;
        private static string randomState;
        static BattleHudChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }
        [MenuItem("Tools/Guild Tactics/Validate Battle HUD (Play Mode)")]
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
        { deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (!EditorApplication.isPlaying || ++frames < 25) return; frames = 0;
                if (steps == null)
                {
                    bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, null);
                    typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, new GuildState());
                    var offer = ExpeditionSelection.Offers[0];
                    int seed = Enumerable.Range(1, 200).First(value => EncounterGenerator.Generate(DungeonGenerator.Generate(value, offer.CreateDungeonConfig()), offer.CreateEncounterConfig()).All(e => e.Archetype.Unit.Initiative < 3));
                    typeof(GameBootstrap).GetProperty("Expeditions").SetValue(bootstrap, new ExpeditionSelection(seed, 0, 0));
                    Require(bootstrap.TryLaunchExpedition(), "Actual launch with uninterrupted first two player turns");
                    controller = bootstrap.ActiveController; interaction = controller.GetComponent<HexGridInteraction>(); hud = bootstrap.BattleScreen;
                    controller.SetAnimationSecondsPerStep(0); hud.Show(); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    interaction.CellClicked += _ => clicks++; randomState = JsonUtility.ToJson(UnityEngine.Random.state);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-39 ALL BATTLE HUD CHECKS PASSED: actual battle, fog-filtered queue/active/deaths, four portraits and shared sprites, read-only ally/enemy inspection, guarded move/wait/end-turn, panel click exclusion and real map click, dynamic safe framing/pan/zoom, journal, RU/EN at two resolutions, unchanged RNG, legacy camera, exit dialog, hide/return/relaunch.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-39 step " + step); Finish(error); }
        }
        private static List<Action> Scenarios() => new List<Action>
        {
            () => { VerifyLayout(); VerifyQueue(); Capture("1280-ru"); selected = controller.SelectedUnit; turn = controller.Turns.TurnNumber; movement = controller.Turns.RemainingMovement; Submit("hud-hero-warrior-1"); },
            () => { Require(hud.Inspected.InstanceId == "warrior-1" && controller.SelectedUnit == selected && controller.Turns.TurnNumber == turn && controller.Turns.RemainingMovement == movement, "Inspect another ally cannot grant control"); BlockEveryPanel(); Capture("1280-inspect-ru"); Escape(); },
            () => { Require(hud.Inspected == null, "Escape closes inspection"); VerifyCamera(); },
            () => { Submit("hud-attack"); },
            () => { Require(controller.IsTargetingAttack && controller.Turns.ActionAvailable, "Existing attack selector"); Submit("hud-move"); },
            () => { Require(!controller.IsTargetingAttack, "Existing cancellation"); var cell = controller.Turns.GetMovementRange(controller.SelectedUnit).Costs.Keys.First(c => c != controller.SelectedUnit.Position && hud.SafeMapRect.Contains(interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(c)))); interaction.ProcessPointer(interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(cell)), true); Require(controller.SelectedUnit.Position == cell && clicks == 1, "Unobscured map remains clickable"); },
            () => { Submit("hud-log-toggle"); },
            () => { Require(hud.ScreenRoot.Q("hud-log") != null, "Collapsed log opens"); BlockEveryPanel(); Escape(); },
            () => { var button = Button("hud-wait"); Submit(button); Submit(button); },
            () => { Require(!controller.Turns.ActionAvailable, "Wait consumes action once"); if (controller.Turns.State == TurnState.ResolvingAction || !Button("hud-end-turn").enabledInHierarchy) { step--; return; } turn = controller.Turns.TurnNumber; var button = Button("hud-end-turn"); Submit(button); Submit(button); },
            () => { Require(controller.Turns.TurnNumber == turn + 1 && controller.SelectedUnit != selected, "One end-turn despite duplicate submit: before=" + turn + " now=" + controller.Turns.TurnNumber + " state=" + controller.Turns.State + " selected=" + controller.SelectedUnit?.InstanceId + " can=" + controller.CanPlayerAct); RevealEnemies(); },
            () => { VerifyQueue(); Require(controller.Enemies.All(controller.IsUnitVisible), "Visible enemies enter queue"); enemy = controller.Enemies[0]; Submit("queue-" + enemy.InstanceId); },
            () => { Require(hud.Inspected == enemy && controller.SelectedUnit.Team == UnitTeam.Player, "Enemy inspection does not select for control"); Capture("1280-enemy-inspect-ru"); Escape(); HoldEnemyTurn(); },
            () => { Require(controller.Turns.ActiveUnit.Team == UnitTeam.Enemy && !Button("hud-end-turn").enabledInHierarchy && !Button("hud-attack").enabledInHierarchy, "Enemy turn never grants actions"); VerifyQueue(); Capture("1280-enemy-turn-ru"); Submit("hud-hero-mage-1"); },
            () => { Require(controller.Turns.ActiveUnit.Team == UnitTeam.Enemy && !controller.CanPlayerAct, "Portrait cannot steal enemy turn"); Escape(); ResumePlayerTurn(); controller.Units.First(h => h.InstanceId == "mage-1").ApplyDamage(999); enemy.ApplyDamage(999); },
            () => { VerifyQueue(); Require(hud.ScreenRoot.Q<Button>("hud-hero-mage-1").ClassListContains("fallen") && hud.ScreenRoot.Q<Button>("queue-mage-1") == null && hud.ScreenRoot.Q<Button>("queue-" + enemy.InstanceId) == null, "Death updates portraits and queue"); Capture("1280-casualties-ru"); Localization.SetLanguage(InterfaceLanguage.English, false); },
            () => { VerifyLayout(); Capture("1280-en"); SetTarget(1920, 1080); },
            () => { VerifyLayout(); VerifyCamera(); Capture("1920-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); },
            () => { VerifyLayout(); Capture("1920-ru"); VerifyDice(); Require(JsonUtility.ToJson(UnityEngine.Random.state) == randomState, "HUD does not consume Unity RNG"); SetTarget(1280, 720); },
            () => { hud.ShowLegacy(); },
            () => { Require(!hud.IsOpen && interaction.MapScreenRect.yMin == 32, "Legacy camera reservation restored"); hud.Show(); },
            () => { Require(hud.IsOpen, "Reopen HUD"); PrepareExit(); },
            () => Submit("hud-retreat"),
            () => { Require(!hud.IsOpen && controller.HasBlockingInterface, "Existing retreat dialog owns screen"); controller.InterfaceBlocked = false; controller.GetComponent<ExpeditionUI>().enabled = false; Require(controller.TryRetreat(true), "Actual retreat"); Require(bootstrap.TryReturnToGuild(), "Actual return"); },
            () => { Require(!hud.IsOpen && bootstrap.GuildScreen.IsOpen, "Battle HUD hides on return"); var replacement = new GuildState(); typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, replacement);
                typeof(GameBootstrap).GetField("debugMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, true);
                typeof(GameBootstrap).GetField("encounterConfig", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, new EncounterConfig { Budget = 100, MinEnemies = 8, MaxEnemies = 8, MiniBossPercent = 0 });
                Require(bootstrap.TryLaunchExpedition(), "Second launch with maximum queue"); },
            () => { controller = bootstrap.ActiveController; interaction = controller.GetComponent<HexGridInteraction>(); SetTarget(1280, 720); },
            () => { Require(hud.IsOpen && hud.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("hud-hero-")) == 4 && hud.Inspected == null, "New battle rebinds with no stale inspection"); RevealEnemies(); },
            () => { VerifyQueue(); Require(controller.Enemies.Count == 8 && hud.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("queue-")) == 12, "Maximum living visible queue"); hud.ScreenRoot.Q<ScrollView>("hud-queue-scroll").scrollOffset = new Vector2(10000, 0); },
            () => { Require(hud.ScreenRoot.Q<ScrollView>("hud-queue-scroll").scrollOffset.x > 0, "Long queue scrolls"); Capture("1280-long-queue-ru");
                var lastEnemy = controller.Turns.Order.Last(u => u.Team == UnitTeam.Enemy);
                for (int i = 0; i < controller.Turns.Order.Count && controller.Turns.ActiveUnit != lastEnemy; i++) NextTurn();
                Require(controller.Turns.TryBeginAction(lastEnemy), "Hold last enemy for scroll verification"); },
            () => { var scroll = hud.ScreenRoot.Q<ScrollView>("hud-queue-scroll"); var active = Button("queue-" + controller.Turns.ActiveUnit.InstanceId);
                Require(scroll.contentViewport.worldBound.Contains(active.worldBound.center), "New active unit is scrolled into view"); VerifyQueue(); hud.enabled = false; },
            () => Require(!hud.IsOpen && !UiInputGate.UsesBattleHud(controller), "Disable releases HUD and camera")
        };
        private static void VerifyQueue()
        {
            var expected = controller.Turns.Order.Where(controller.IsUnitVisible).Select(u => "queue-" + u.InstanceId);
            var actual = hud.ScreenRoot.Query<Button>().ToList().Where(b => b.name.StartsWith("queue-")).Select(b => b.name);
            Require(actual.SequenceEqual(expected), "Exact visible living model queue");
            var active = controller.Turns.ActiveUnit;
            if (controller.IsUnitVisible(active)) Require(hud.ScreenRoot.Q<UnitMiniature>("active-sprite").sprite.texture == Resources.Load<Texture2D>("UnitSprites/" + UnitSpriteSheet.AssetFor(active.Definition.Id)), "Active miniature uses the same source atlas as battle/guild");
            var marked = hud.ScreenRoot.Query<Button>().ToList().Where(b => b.name.StartsWith("queue-") && b.ClassListContains("selected")).ToList();
            Require(marked.Count == (controller.IsUnitVisible(active) ? 1 : 0), "Active marker follows model");
            foreach (var image in hud.ScreenRoot.Query<UnitMiniature>().ToList()) Require(image.sprite != null, "Real shared unit sprite");
        }
        private static void VerifyLayout()
        {
            Require(hud.IsOpen && hud.SafeMapRect.width > interaction.GridCamera.pixelWidth * 0.75f && hud.SafeMapRect.height > interaction.GridCamera.pixelHeight * 0.45f, "Map dominates safe area");
            var root = hud.ScreenRoot.worldBound;
            foreach (var panel in hud.ScreenRoot.Query<VisualElement>(className: "hud-panel").ToList())
                Require(root.Contains(panel.worldBound.min) && root.Contains(panel.worldBound.max - Vector2.one), "Panel fits: " + panel.name);
            foreach (var label in hud.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Unclipped label: " + label.name + " " + label.text + " size=" + size + " actual=" + label.contentRect);
            }
            BlockEveryPanel();
        }
        private static void BlockEveryPanel()
        {
            int before = clicks;
            foreach (var panel in hud.ScreenRoot.Query<VisualElement>(className: "hud-panel").ToList())
            { var point = hud.ScreenRect(panel).center; Require(!interaction.AllowsMapPointer(point), "Panel blocks camera/map: " + panel.name); interaction.ProcessPointer(point, true); Require(!interaction.Hovered.HasValue, "HUD does not hover a map hex"); }
            Require(clicks == before, "HUD clicks never reach CellClicked");
        }
        private static void VerifyCamera()
        {
            interaction.FrameCamera();
            foreach (var cell in bootstrap.Grid.Cells) Require(hud.SafeMapRect.Contains(interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(cell.Coordinates))), "Overview fits each cell in measured safe area");
            interaction.SetSelected(new HexCoordinates(5, 5)); interaction.ShowCharacterDetail();
            float before = interaction.GridCamera.orthographicSize;
            interaction.ZoomCamera(1); Require(interaction.GridCamera.orthographicSize < before, "Zoom works with HUD"); interaction.ZoomCamera(-1);
            var position = interaction.GridCamera.transform.position; interaction.PanCamera(new Vector2(30, 0));
            Require(interaction.GridCamera.transform.position != position, "Pan works with HUD");
            interaction.SetSelected(controller.SelectedUnit.Position);
        }
        private static void RevealEnemies()
        {
            foreach (var unit in controller.Enemies)
            {
                var cell = bootstrap.Grid.Cells.First(c => !c.IsOccupied && TerrainRules.CanWalk(c.Terrain) && controller.Visibility.IsVisible(c.Coordinates));
                Require(unit.TryRelocate(bootstrap.Grid, cell.Coordinates), "Place visible enemy");
                controller.GetComponentsInChildren<UnitView>(true).First(v => v.State == unit).SnapTo(new HexLayout().ToWorld(unit.Position));
            }
            controller.Visibility.Refresh();
        }
        private static void NextTurn()
        {
            if (controller.Turns.State == TurnState.ResolvingAction) controller.Turns.TryCompleteAction(controller.Turns.ActiveUnit);
            Require(controller.Turns.TryEndTurn(controller.Turns.ActiveUnit), "End arranged turn");
            typeof(PlayerUnitController).GetMethod("StartNextTurn", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        }
        private static void HoldEnemyTurn()
        { for (int i = 0; i < controller.Turns.Order.Count && controller.Turns.ActiveUnit.Team != UnitTeam.Enemy; i++) NextTurn(); Require(controller.Turns.TryBeginAction(controller.Turns.ActiveUnit), "Hold actual enemy in resolving state"); }
        private static void ResumePlayerTurn()
        { for (int i = 0; i < controller.Turns.Order.Count && controller.Turns.ActiveUnit.Team != UnitTeam.Player; i++) NextTurn(); }
        private static void PrepareExit()
        {
            var actor = controller.SelectedUnit; var exit = controller.Expedition.Extraction;
            var occupant = controller.Units.FirstOrDefault(u => u != actor && u.IsAlive && u.Position == exit);
            if (occupant != null) occupant.TryRelocate(bootstrap.Grid, bootstrap.Grid.Cells.First(c => !c.IsOccupied && TerrainRules.CanWalk(c.Terrain)).Coordinates);
            Require(actor.TryRelocate(bootstrap.Grid, exit), "Active hero reaches EXIT");
        }
        private static void VerifyDice()
        {
            var combat = typeof(PlayerUnitController).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var dice = (IDice)typeof(CombatSystem).GetField("dice", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
            var expected = new SeededDice(ExpeditionJourney.SectionSeed(bootstrap.Journey.CombatSeed, bootstrap.Journey.Completed));
            for (int i = 0; i < 32; i++) Require(dice.Roll(20) == expected.Roll(20), "HUD/inspection/camera did not consume combat RNG");
        }
        private static Button Button(string name) => hud.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing " + name);
        private static void Submit(string name) => Submit(Button(name));
        private static void Submit(Button button) { using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void Escape() { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = hud.ScreenRoot; hud.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); interaction.GridCamera.targetTexture = target; interaction.GridCamera.ResetAspect(); hud.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp39-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-39: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
