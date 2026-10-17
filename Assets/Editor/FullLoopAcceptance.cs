using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GuildTactics.Core;
using GuildTactics.Expeditions;
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
    /// <summary>Actual combat acceptance: no injected damage, rewards, positions or turn completion.</summary>
    [InitializeOnLoad]
    public static class FullLoopAcceptance
    {
        private const string Pending = "GuildTactics.WP42.FullLoop";
        private static GameBootstrap owner;
        private static GuildSaveStore store;
        private static int phase, frames, commands, attacks, sections;
        private static double deadline;
        private static ExpeditionResult result;
        static FullLoopAcceptance() { if (SessionState.GetBool(Pending, false)) Begin(); }
        public static void RunBatch()
        {
            try { EditorSceneManager.OpenScene("Assets/Scenes/TacticalPrototype.unity"); SessionState.SetBool(Pending, true); Begin(); EditorApplication.EnterPlaymode(); }
            catch (Exception error) { Finish(error); }
        }
        private static void Begin() { deadline = EditorApplication.timeSinceStartup + 360; EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline && commands < 1500, "Loop timeout");
                if (!EditorApplication.isPlaying || ++frames < 10) return; frames = 0;
                if (owner == null)
                {
                    owner = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    var data = GuildSaveData.Capture(owner.Guild, owner.Expeditions);
                    data.items = new[] { ItemDefinitions.Weapon.Id, ItemDefinitions.Armor.Id };
                    var guild = GuildState.Restore(data);
                    // Initial progression fixture only; battle HP, RNG and rewards remain unmodified.
                    typeof(GameBootstrap).GetProperty("Guild").SetValue(owner, guild);
                    typeof(GameBootstrap).GetProperty("Expeditions").SetValue(owner, new ExpeditionSelection(1, 0, 0));
                    store = new GuildSaveStore(Path.GetFullPath("Logs/wp42-full-loop-save.json"));
                    typeof(GameBootstrap).GetField("saveStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, store);
                    guild.Changed += () => owner.RetryGuildSave();
                    owner.GuildScreen.ShowParty(); owner.BattleScreen.Show(); owner.ExpeditionScreen.Show(); return;
                }
                if (phase == 0) { Submit(owner.GuildScreen.ScreenRoot, "guild-tab-1"); phase++; return; }
                if (phase == 1) { Submit(owner.GuildScreen.ScreenRoot, "storage-item-iron-edge"); phase++; return; }
                if (phase == 2) { Submit(owner.GuildScreen.ScreenRoot, "storage-equip"); phase++; return; }
                if (phase == 3) { Require(owner.Guild.Roster.First(h => h.Id == "warrior-1").Weapon == ItemDefinitions.Weapon, "Equipment via actual storage"); Submit(owner.GuildScreen.ScreenRoot, "guild-tab-2"); phase++; return; }
                if (phase == 4) { Submit(owner.GuildScreen.ScreenRoot, "launch-expedition"); phase++; return; }
                if (phase == 6)
                {
                    Require(!owner.Guild.IsAway && owner.Guild.Gold == 100 + result.Gold && attacks > 0, "Actual combat payout");
                    Require(store.TryLoad(out var loaded, out _, out _) && loaded.Gold == owner.Guild.Gold && loaded.Inventory.Count == owner.Guild.Inventory.Count, "Paid save reload");
                    Submit(owner.GuildScreen.ScreenRoot, "guild-tab-1"); phase++; return;
                }
                if (phase == 7)
                {
                    Require(result.Items.All(i => owner.GuildScreen.ScreenRoot.Q("storage-item-" + i.Id) != null), "Earned loot in storage");
                    Debug.Log($"WP-42 FULL ACTUAL COMBAT LOOP PASSED: storage equipment -> departure -> {sections} sections, {attacks} attacks, {commands} commands -> actual loot -> guild/storage -> paid save reload. No injected combat damage, relocation, rewards or action completion.");
                    Finish(null); return;
                }
                if (owner.ExpeditionScreen.IsOpen)
                {
                    var screen = owner.ExpeditionScreen.ScreenRoot;
                    if (screen.Q<Button>("journey-confirm") != null) Submit(screen, "journey-confirm");
                    else if (owner.Journey.EventPending) Submit(screen, "journey-skip");
                    else if (owner.CanContinueJourney) { sections++; Submit(screen, "journey-continue"); }
                    else
                    {
                        result = owner.JourneyBoundary ?? owner.ActiveController.Expedition.Result;
                        Require(result.Outcome == ExpeditionOutcome.Extracted && result.Items.Count > 0, "A complete successful run, not a retreat/defeat");
                        sections++; Submit(screen, "journey-return"); phase = 6;
                    }
                    return;
                }
                var controller = owner.ActiveController;
                if (controller == null || !controller.CanPlayerAct || !owner.BattleScreen.IsOpen) return;
                controller.SetAnimationSecondsPerStep(0);
                var actor = controller.SelectedUnit; var turns = controller.Turns; var run = controller.Expedition;
                if (turns.ActionAvailable)
                {
                    var victim = controller.Enemies.FirstOrDefault(controller.CanAttack);
                    if (victim != null)
                    {
                        Require(controller.TryAttackSelected(victim.Position), "Real attack"); attacks++; commands++; return;
                    }
                    if (run.CanOpenChest(actor)) { Submit(owner.BattleScreen.ScreenRoot, "hud-relic"); return; }
                }
                if (run.CanExtract(actor)) { Submit(owner.BattleScreen.ScreenRoot, "hud-extract"); return; }
                HexCoordinates goal = run.MissionCompleted ? run.Extraction : run.Chest;
                var enemy = controller.Enemies.Where(u => u.IsAlive && controller.IsUnitVisible(u)).OrderBy(u => u.Position.DistanceTo(actor.Position)).FirstOrDefault();
                var fullRange = HexPathfinder.FindReachable(owner.Grid, actor.Position, int.MaxValue);
                if (enemy != null)
                {
                    var attackCells = owner.Grid.GetNeighbors(enemy.Position).Where(c => fullRange.Costs.ContainsKey(c.Coordinates)).OrderBy(c => fullRange.Costs[c.Coordinates]).ToArray();
                    if (attackCells.Length > 0) goal = attackCells[0].Coordinates;
                }
                var path = fullRange.GetPathTo(goal);
                var legal = turns.GetMovementRange(actor);
                var destination = actor.Position;
                foreach (var cell in path.Skip(1)) { if (!legal.Costs.ContainsKey(cell)) break; destination = cell; }
                if (destination != actor.Position && controller.TryMoveSelected(destination)) { commands++; return; }
                Submit(owner.BattleScreen.ScreenRoot, "hud-end-turn");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Submit(VisualElement root, string name)
        {
            var button = root?.Q<Button>(name); Require(button != null && button.enabledInHierarchy, "Available " + name);
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
            commands++;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-42: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
