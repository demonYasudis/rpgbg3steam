using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Meta;
using GuildTactics.UI;
using GuildTactics.Units;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.Editor
{
    [InitializeOnLoad]
    public static class GuildMotionChecks
    {
        private const string Pending = "GuildTactics.WP37.Pending";
        private static int frames, step, samples, lastProfileFrame;
        private static float profileStarted;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static GuildPartyScreen ui;
        private static RenderTexture target;
        private static List<Action> steps;
        private static string savedGuild, randomState;
        private static Rect[] bounds;
        private static UnitMiniature[] heldImages;
        private static Sprite[] heldFrames;
        private static float heldTime;
        private static ProfilerRecorder motionCpu, frameGc;
        private static long cpuSum, gcSum;
        private static string profile;
        private static int[] frameChanges;
        private static int previewFrame, previewLength;
        private static string preview;
        private static float nextPreviewTime;
        static GuildMotionChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }

        [MenuItem("Tools/Guild Tactics/Validate Guild Motion (Play Mode)")]
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
            deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
                if (!EditorApplication.isPlaying) return;
                if (preview != null)
                {
                    if (Time.unscaledTime < nextPreviewTime) return;
                    Capture("preview-" + preview + "-" + previewFrame.ToString("D2"));
                    nextPreviewTime = Time.unscaledTime + 0.1f;
                    if (++previewFrame >= previewLength) preview = null;
                    return;
                }
                if (profile != null)
                {
                    if (Time.frameCount == lastProfileFrame) return;
                    lastProfileFrame = Time.frameCount; samples++;
                    cpuSum += motionCpu.LastValue; gcSum += frameGc.LastValue;
                    if (samples >= 600 && Time.unscaledTime - profileStarted >= 2)
                    {
                        Require(motionCpu.Valid && motionCpu.Count > 0, "Actual Unity profiler marker recorded");
                        Debug.Log("WP-37 PROFILE " + profile + ": " + samples + " PlayerLoop frames, motion CPU mean " + cpuSum / samples + " ns, whole-frame GC mean " + gcSum / samples + " bytes; same scene, 1280x720, four heroes.");
                        motionCpu.Dispose(); frameGc.Dispose();
                        profile = null;
                    }
                    return;
                }
                if (++frames < 25) return;
                frames = 0;
                if (steps == null)
                {
                    bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>(); ui = bootstrap.GuildScreen;
                    // All previews and the defeat scenario use an isolated guild, never a player's save.
                    typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, null);
                    var fixture = GuildSaveData.Capture(new GuildState(), bootstrap.Expeditions);
                    fixture.items = ItemDefinitions.All.Select(item => item.Id).ToArray();
                    typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, GuildState.Restore(fixture));
                    ui.ShowParty(); SetTarget(1280, 720); Localization.SetLanguage(InterfaceLanguage.Russian, false);
                    savedGuild = SaveSnapshot(); randomState = JsonUtility.ToJson(UnityEngine.Random.state);
                    steps = Scenarios(); return;
                }
                if (step < steps.Count) { steps[step++](); return; }
                Debug.Log("WP-37 ALL GUILD MOTION CHECKS PASSED: four shared atlases, idle/reaction/timing, full cells and stable layout, reduced motion, hidden/modal/disabled pause, 60 switches, sustained opening, static/animated Unity profiling, unchanged guild/Unity RNG and exact combat dice stream, RU/EN in two resolutions.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-37 step " + step); Finish(error); }
        }
        private static List<Action> Scenarios() => new List<Action>
        {
            () => { VerifyLayout(); VerifyFrames(); HoldImages(); bounds = StableBounds(); Capture("1280-party-ru"); },
            () => { ui.SetReducedMotion(true, false); },
            () => { Require(Images().All(i => i.sprite.name.EndsWith("_Idle_0")), "Reduced mode freezes idle zero"); heldTime = ui.MotionTime; HoldImages(); bounds = StableBounds(); },
            () => { Require(ui.MotionTime == heldTime && Images().Select(i => i.sprite).SequenceEqual(heldFrames), "Reduced clock and sprites stay frozen"); VerifyStable(); Require(ui.ScreenRoot.Q("guild-candlelight").resolvedStyle.opacity == 0, "Decorative light disabled"); Submit("party-card-rogue-1"); },
            () => { Require(Images().All(i => i.sprite.name.EndsWith("_Idle_0")), "Reduced selection has no reaction"); StartProfile("static baseline / reduced motion"); },
            () => { ui.ScreenRoot.Q<Toggle>("reduce-motion").value = false; },
            () => { Require(!ui.ReducedMotion, "Real setting resumes motion"); HoldImages(); frameChanges = heldImages.Select(i => i.FrameChanges).ToArray(); bounds = StableBounds(); StartProfile("animated"); },
            () => { Require(Images().Select((image, i) => image.FrameChanges > frameChanges[i] + 1).All(v => v), "All four heroes advance during sustained opening"); VerifyStable(); VerifyLayout(); Submit("party-card-warrior-1"); },
            () => { Require(ui.SelectedId == "warrior-1", "Selection preserves hero identity"); var image = ui.ScreenRoot.Q<UnitMiniature>("miniature-warrior-1"); Require(image.sprite.name.Contains("_Defend_") || image.FrameChanges > 1, "Short selection response uses atlas"); Capture("1280-selection-ru"); StartPreview("party", 20); },
            () => { VerifyFrames(); Submit("open-reserve"); },
            () => { heldTime = ui.MotionTime; HoldImages(); },
            () => { Require(ui.MotionTime == heldTime && heldImages.Select(i => i.sprite).SequenceEqual(heldFrames), "Covered party pauses"); Submit("close-reserve"); },
            () => { HoldImages(); ui.ShowLegacy(); heldTime = ui.MotionTime; },
            () => { Require(!ui.IsOpen && ui.MotionTime == heldTime && ui.MotionElementCount == 0 && heldImages.Select(i => i.sprite).SequenceEqual(heldFrames), "Hidden screen releases animation elements and stops ticking"); ui.ShowParty(); },
            () => { Require(ui.MotionElementCount == 9 && Images().Length == 4, "Exactly four sprites, four portraits and one light"); for (int i = 0; i < 60; i++) { ui.ShowLegacy(); ui.ShowParty(); } },
            () => { Require(ui.MotionElementCount == 9 && Images().Length == 4, "Repeated opens do not accumulate animation work"); Submit("guild-tab-1"); },
            () => { Require(ui.MotionElementCount == 2 && Images().Length == 0, "Storage keeps only its portrait and light"); VerifyLayout(); Capture("1280-storage-ru"); StartPreview("storage", 12); },
            () => Submit("guild-tab-0"),
            () => { Require(ui.MotionElementCount == 9, "Party restores four miniatures"); Localization.SetLanguage(InterfaceLanguage.English, false); },
            () => { VerifyLayout(); Capture("1280-party-en"); SetTarget(1920, 1080); },
            () => { VerifyLayout(); Capture("1920-party-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); },
            () => { VerifyLayout(); Capture("1920-party-ru"); ui.SetReducedMotion(true, false); },
            () => { Require(Images().All(i => i.sprite.name.EndsWith("_Idle_0")), "Setting applies after resize/language"); Require(SaveSnapshot() == savedGuild && JsonUtility.ToJson(UnityEngine.Random.state) == randomState, "Menu never changes guild or Unity RNG"); ui.SetReducedMotion(false, false); ui.enabled = false; heldTime = ui.MotionTime; },
            () => { Require(!ui.IsOpen && ui.MotionTime == heldTime && ui.MotionElementCount == 0, "Disabled screen stops and clears motion"); ui.enabled = true; ui.ShowParty(); },
            () => { Require(bootstrap.TryLaunchExpedition(), "Launch after long menu session"); },
            () => { Require(!ui.IsOpen && ui.MotionElementCount == 0, "Battle owns presentation after launch"); VerifyCombatDice(); heldTime = ui.MotionTime; foreach (var unit in bootstrap.ActiveController.Expedition.Party) unit.ApplyDamage(unit.CurrentHealth); bootstrap.ActiveController.Expedition.RefreshOutcome(); },
            () => { Require(ui.MotionTime == heldTime, "Guild clock stays stopped during battle"); Require(bootstrap.TryReturnToGuild(), "Return to guild"); },
            () => { Require(ui.IsOpen && Images().Length == 0 && ui.MotionElementCount == 1, "Empty party after casualties has no stale animations"); }
        };
        private static UnitMiniature[] Images() => ui.ScreenRoot.Query<UnitMiniature>().ToList().ToArray();
        private static void HoldImages() { heldImages = Images(); heldFrames = heldImages.Select(i => i.sprite).ToArray(); }
        private static string SaveSnapshot() => JsonUtility.ToJson(GuildSaveData.Capture(bootstrap.Guild, bootstrap.Expeditions));
        private static void StartPreview(string name, int length)
        { preview = name; previewFrame = 0; previewLength = length; nextPreviewTime = Time.unscaledTime + 0.1f; }
        private static void StartProfile(string name)
        {
            profile = name; cpuSum = gcSum = 0; samples = 0; profileStarted = Time.unscaledTime; lastProfileFrame = Time.frameCount;
            motionCpu = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "GuildTactics.GuildMotion", 1);
            frameGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        }
        private static void VerifyFrames()
        {
            foreach (var definition in HeroDefinitions.Defaults)
            using (var sheet = UnitSpriteSheet.Load(definition.Id))
            {
                var image = new UnitMiniature(sheet);
                var first = sheet.Frame(UnitSpriteSheet.Pose.Idle, 0);
                for (int i = 0; i < 4; i++)
                {
                    float elapsed = sheet.Duration(UnitSpriteSheet.Pose.Idle) * (i + 0.1f) / 4;
                    image.Render(elapsed, false);
                    Require(image.sprite == sheet.Frame(UnitSpriteSheet.Pose.Idle, elapsed), "Shared atlas and timing: " + definition.Id);
                    Require(image.sprite.rect.size == first.rect.size && image.sprite.pivot == first.pivot && image.scaleMode == ScaleMode.ScaleToFit, "Uncropped cell, fixed pivot: " + definition.Id);
                }
                image.Render(0, false, 0.21f);
                Require(image.sprite == sheet.Frame(UnitSpriteSheet.Pose.Defend, 0.21f, false), "Shared reaction frames");
                image.Render(0.3f, false, sheet.Duration(UnitSpriteSheet.Pose.Defend));
                Require(image.sprite == sheet.Frame(UnitSpriteSheet.Pose.Idle, 0.3f), "Reaction returns to idle at actual duration");
                image.Render(3600.5f, false);
                Require(image.sprite == sheet.Frame(UnitSpriteSheet.Pose.Idle, 3600.5f), "Long-running clock wraps safely");
            }
        }
        private static Rect[] StableBounds() => ui.ScreenRoot.Query<Label>().ToList().Select(l => l.worldBound)
            .Concat(ui.ScreenRoot.Query<Button>().ToList().Select(b => b.worldBound)).ToArray();
        private static void VerifyStable()
        {
            var current = StableBounds(); Require(current.Length == bounds.Length, "Same controls");
            for (int i = 0; i < current.Length; i++) Require(Vector2.Distance(current[i].position, bounds[i].position) < 0.1f && Vector2.Distance(current[i].size, bounds[i].size) < 0.1f, "Text and hit areas stay still");
        }
        private static void VerifyLayout()
        {
            var screen = ui.ScreenRoot.worldBound;
            var toggle = ui.ScreenRoot.Q<Toggle>("reduce-motion");
            Require(screen.Contains(toggle.worldBound.min) && screen.Contains(toggle.worldBound.max - Vector2.one), "Setting fits screen");
            foreach (var image in Images()) Require(image.worldBound.height >= 64 && screen.Contains(image.worldBound.center), "Visible miniature");
            foreach (var label in ui.ScreenRoot.Query<Label>().ToList())
            {
                if (string.IsNullOrEmpty(label.text) || label.ClassListContains("card-title")) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Readable label: " + label.text);
            }
        }
        private static void VerifyCombatDice()
        {
            var field = typeof(PlayerUnitController).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic);
            var combat = field.GetValue(bootstrap.ActiveController);
            var dice = (IDice)typeof(CombatSystem).GetField("dice", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
            var expected = new SeededDice(ExpeditionJourney.SectionSeed(bootstrap.Journey.CombatSeed, bootstrap.Journey.Completed));
            for (int i = 0; i < 32; i++) Require(dice.Roll(20) == expected.Roll(20), "Menu did not consume combat RNG");
        }
        private static void Submit(string name)
        { var button = ui.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing " + name); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); ui.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp37-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-37: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (motionCpu.Valid) motionCpu.Dispose(); if (frameGc.Valid) frameGc.Dispose();
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
