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
    [DefaultExecutionOrder(20000)]
    public sealed class ActionHoverDriver : MonoBehaviour
    {
        internal BattleHud Hud;
        internal HexGridInteraction Interaction;
        internal HexCoordinates? Cell;
        private void LateUpdate()
        {
            if (Hud == null || !Hud.IsOpen) return;
            Interaction.ProcessPointer(Cell.HasValue ? Interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(Cell.Value)) : new Vector3(-100, -100), false);
            typeof(BattleHud).GetMethod("UpdateTargetPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Hud, null);
        }
    }
    [InitializeOnLoad]
    public static class BattleActionChecks
    {
        private const string Pending = "GuildTactics.WP40.Pending";
        private static int frames, step;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static PlayerUnitController controller;
        private static BattleHud hud;
        private static HexGridInteraction interaction;
        private static ActionHoverDriver hover;
        private static RenderTexture target;
        private static List<Action> steps;
        private static UnitRuntimeState enemy;
        private static string rng;
        private static int hp;
        static BattleActionChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }
        [MenuItem("Tools/Guild Tactics/Validate Battle Actions (Play Mode)")]
        public static void RunBatch()
        {
            try
            {
                ActionForecastChecks.Run();
                EditorSceneManager.OpenScene("Assets/Scenes/TacticalPrototype.unity");
                SessionState.SetBool(Pending, true); Begin(); EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Begin() { deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (controller != null && controller.Turns.State == TurnState.ResolvingAction) return;
                if (!EditorApplication.isPlaying || ++frames < 30) return; frames = 0;
                if (steps == null)
                {
                    bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, null);
                    typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, new GuildState());
                    var offer = ExpeditionSelection.Offers[0];
                    int seed = Enumerable.Range(1, 200).First(value => EncounterGenerator.Generate(DungeonGenerator.Generate(value, offer.CreateDungeonConfig()), offer.CreateEncounterConfig()).All(e => e.Archetype.Unit.Initiative < 3));
                    typeof(GameBootstrap).GetProperty("Expeditions").SetValue(bootstrap, new ExpeditionSelection(seed, 0, 0));
                    Require(bootstrap.TryLaunchExpedition(), "Actual launch");
                    controller = bootstrap.ActiveController; interaction = controller.GetComponent<HexGridInteraction>(); hud = bootstrap.BattleScreen;
                    controller.SetAnimationSecondsPerStep(0); hud.Show(); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    hover = controller.gameObject.AddComponent<ActionHoverDriver>(); hover.Hud = hud; hover.Interaction = interaction;
                    rng = JsonUtility.ToJson(UnityEngine.Random.state);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-40 ALL BATTLE ACTION CHECKS PASSED: icons/cost/selection, actual path hover, localized forecast at two resolutions, hidden targets, exact roll agreement, duplicate clicks, spent reason, Escape, journal, unchanged Unity and combat RNG.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-40 step " + step); Finish(error); }
        }
        private static List<Action> Scenarios() => new List<Action>
        {
            () => {
                Require(controller.SelectedUnit.Definition.Id == "rogue", "Rogue starts");
                Require(hud.ScreenRoot.Query<ActionIcon>().ToList().Count == 6, "Six original action symbols");
                hover.Cell = controller.Turns.GetMovementRange(controller.SelectedUnit).Costs.Keys.First(c => c != controller.SelectedUnit.Position && controller.Visibility.IsVisible(c) && hud.SafeMapRect.Contains(interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(c))));
            },
            () => { Require(PreviewVisible() && hud.ScreenRoot.Q<Label>("hud-forecast-summary").text.Contains("Движение"), "Actual movement forecast"); Capture("path-1280-ru"); Submit("hud-attack"); },
            () => {
                Require(controller.IsTargetingAttack && hud.ScreenRoot.Q<Button>("hud-attack").ClassListContains("selected"), "Selected action highlighted");
                var hidden = controller.Enemies.First(e => !controller.Visibility.IsVisible(e.Position));
                Require(!controller.PreviewTarget(hidden.Position, out var attacks, out var reason) && attacks.Count == 0 && reason == "Target is outside current vision.", "Hidden target never reveals stats");
                enemy = controller.Enemies[0];
                var cell = bootstrap.Grid.GetNeighbors(controller.SelectedUnit.Position).First(c => !c.IsOccupied && TerrainRules.CanWalk(c.Terrain));
                Require(enemy.TryRelocate(bootstrap.Grid, cell.Coordinates), "Arrange in-range visible target");
                controller.GetComponentsInChildren<UnitView>(true).First(v => v.State == enemy).SnapTo(new HexLayout().ToWorld(enemy.Position));
                controller.Visibility.Refresh(); controller.SelectBasicAttack(); interaction.SetSelected(controller.SelectedUnit.Position); interaction.ShowCharacterDetail(); hover.Cell = enemy.Position;
            },
            () => { Require(PreviewVisible() && hud.ScreenRoot.Q<Label>("hud-forecast-summary").text.Contains("%"), "Live target probability"); CheckLayout(); Capture("target-1280-ru");
                Require(controller.PreviewTarget(enemy.Position, out var attacks, out _), "Valid basic forecast");
                for (int i = 0; i < 100; i++) Require(controller.PreviewTarget(enemy.Position, out _, out _), "Repeated hover valid");
                VerifyDice(); SetTarget(1920, 1080); Localization.SetLanguage(InterfaceLanguage.English, false); },
            () => Submit("hud-log-toggle"),
            () => { Escape(); Escape(); Require(controller.IsTargetingAttack, "Duplicate Escape in one frame only closes the log"); },
            () => { CheckLayout(); Require(hud.ScreenRoot.Q<Label>("hud-forecast-summary").text.Contains("damage on hit"), "English forecast"); Capture("target-1920-en");
                Escape(); Require(!controller.IsTargetingAttack && controller.Turns.ActionAvailable, "Escape cancels without spending"); hover.Cell = null; },
            () => { Submit("hud-ability-evade"); },
            () => { Require(controller.SelectedAbility?.Id == "evade", "Signature selected"); hover.Cell = controller.SelectedUnit.Position; },
            () => { Require(PreviewVisible() && !hud.ScreenRoot.Q<Label>("hud-forecast-summary").text.Contains("%"), "Nonattack effect has no invented hit probability"); Escape(); hover.Cell = null; },
            () => { Submit("hud-attack"); },
            () => {
                Require(controller.PreviewTarget(enemy.Position, out var forecast, out _), "Forecast before commit");
                hp = enemy.CurrentHealth;
                Require(controller.TryAttackSelected(enemy.Position), "Actual controller attack");
                var result = controller.LastAttack;
                Require(forecast[0].Hits(result.AttackRoll) == result.Hit && (!result.Hit || forecast[0].DamageForRoll(result.DamageRoll) == result.Damage), "Actual roll matches preview");
                Require(!controller.TryAttackSelected(enemy.Position) && enemy.CurrentHealth == hp - result.AppliedDamage, "Double click never reapplies damage");
                hover.Cell = null;
            },
            () => {
                Require(!controller.Turns.ActionAvailable && !hud.ScreenRoot.Q<Button>("hud-attack").enabledInHierarchy && hud.ScreenRoot.Q<Button>("hud-attack").tooltip.Contains("spent"), "Spent-action reason");
                var disabledWrapper = hud.ScreenRoot.Q<Button>("hud-attack").parent;
                using (var evt = PointerEnterEvent.GetPooled()) { evt.target = disabledWrapper; disabledWrapper.SendEvent(evt); }
                Require(hud.ScreenRoot.Q<Label>("hud-hint").text.Contains("spent"), "Disabled action explains its reason on the enabled hover surface");
                var feedback = controller.GetComponent<CombatText>();
                feedback.ShowMessage("TRAP -8 HP", Vector3.zero);
                Require(feedback.History.Count == 2 && feedback.History[0].Contains("d20("), "Journal retains detailed roll and next event");
                Submit("hud-log-toggle");
            },
            () => { Require(hud.ScreenRoot.Q<Label>("hud-log-message").text.Contains("d20(") && hud.ScreenRoot.Q<Label>("hud-log-message").text.Contains("TRAP"), "Open history contains multiple events"); Capture("journal-1920-en"); Require(rng == JsonUtility.ToJson(UnityEngine.Random.state), "UI does not use Unity RNG"); }
        };
        private static bool PreviewVisible() => hud.ScreenRoot.Q("hud-forecast").resolvedStyle.display == DisplayStyle.Flex;
        private static void CheckLayout()
        {
            var root = hud.ScreenRoot.worldBound;
            var preview = hud.ScreenRoot.Q("hud-forecast");
            Require(root.Contains(preview.worldBound.min) && root.Contains(preview.worldBound.max - Vector2.one), "Forecast inside screen");
            foreach (var label in hud.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text) || label.resolvedStyle.display == DisplayStyle.None) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(size.y <= label.contentRect.height + 2, "Unclipped label " + label.name);
            }
        }
        private static void VerifyDice()
        {
            var combat = typeof(PlayerUnitController).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var dice = (IDice)typeof(CombatSystem).GetField("dice", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
            var expected = new SeededDice(ExpeditionJourney.SectionSeed(bootstrap.Journey.CombatSeed, bootstrap.Journey.Completed));
            for (int i = 0; i < 32; i++) Require(dice.Roll(20) == expected.Roll(20), "Preview keeps combat RNG untouched");
        }
        private static void Submit(string name)
        { var button = hud.ScreenRoot.Q<Button>(name); Require(button != null && button.enabledInHierarchy, "Button " + name); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void Escape() { using (var evt = KeyDownEvent.GetPooled(Event.KeyboardEvent("escape"))) { evt.target = hud.ScreenRoot; hud.ScreenRoot.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); interaction.GridCamera.targetTexture = target; interaction.GridCamera.ResetAspect(); hud.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes("Logs/wp40-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-40: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
