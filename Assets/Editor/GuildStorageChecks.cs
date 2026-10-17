using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public static class GuildStorageChecks
    {
        private const string Pending = "GuildTactics.WP36.Pending";
        private static int frames, step;
        private static double deadline;
        private static GameBootstrap bootstrap;
        private static GuildPartyScreen ui;
        private static List<Action> steps;
        private static RenderTexture target;
        private static string path;
        static GuildStorageChecks() { if (SessionState.GetBool(Pending, false)) Begin(); }

        [MenuItem("Tools/Guild Tactics/Validate Storage UI (Play Mode)")]
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
            path = Path.Combine(Path.GetTempPath(), "guild-storage-" + Guid.NewGuid() + ".json");
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
                Debug.Log("WP-36 ALL STORAGE UI CHECKS PASSED: empty/stocked/100000 items, filters, actual equipment/stats, owner transfer, stale/duplicate submits, potion capacity, dead/lost refusals, save/load, tab/hero identity, scrolling, RU/EN at 1280x720 and 1920x1080.");
                Finish(null);
            }
            catch (Exception error) { Debug.LogError("WP-36 step " + step); Finish(error); }
        }
        private static GuildAdventurer Hero => bootstrap.Guild.Roster.First(h => h.Id == ui.SelectedId);
        private static List<Action> Scenarios() => new List<Action>
        {
            () => Fixture(0),
            () => Submit("guild-tab-1"),
            () => { Require(ui.ScreenRoot.Q<Label>("storage-empty") != null, "Honest empty state"); VerifyLayout(); Capture("1280-empty-ru"); Fixture(3); },
            () => { Require(ui.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("storage-item-")) == 3, "Three actual item types"); VerifyLayout(); Capture("1280-stocked-ru"); Submit("storage-item-iron-edge"); },
            () => { Require(Label("storage-comparison").Contains("→"), "Actual stat comparison"); SubmitTwice("storage-equip"); },
            () => { Require(Hero.Weapon == ItemDefinitions.Weapon && Hero.Attack == Hero.Definition.Attack + 1 && Count("iron-edge") == 2, "Single equip, bonus and count"); Require(!Button("storage-equip").enabledInHierarchy && Label("storage-owners").Contains(Hero.Id), "Equipped state explained"); Submit("storage-remove-Weapon"); },
            () => { Require(Hero.Weapon == null && Count("iron-edge") == 3, "Remove returns one copy"); Submit("storage-item-crypt-mail"); },
            () => Submit("storage-equip"),
            () => { Require(Hero.Armor == ItemDefinitions.Armor && Hero.Defense == Hero.Definition.Defense + 2, "Armor stat effect"); Submit("storage-item-healing-draught"); },
            () => SubmitTwice("storage-equip"),
            () => { Require(Hero.HealingPotions == 1 && Count("healing-draught") == 2, "Duplicate events transfer only one potion"); Submit("storage-equip"); },
            () => { Require(Hero.HealingPotions == 2 && !Button("storage-equip").enabledInHierarchy, "Two potion capacity"); Submit("storage-return-potion"); },
            () => { Require(Hero.HealingPotions == 1 && Count("healing-draught") == 2, "Return one potion"); Roundtrip(); Submit("storage-filter-Weapon"); },
            () => { Require(ui.ScreenRoot.Q<Button>("storage-item-iron-edge") != null && ui.ScreenRoot.Q<Button>("storage-item-crypt-mail") == null, "Weapon filter"); Submit("storage-equip"); },
            () => { Require(Hero.Weapon != null, "Equip filtered weapon"); SelectHero("warrior-2"); },
            () => { Require(Hero.Id == "warrior-2" && Hero.Weapon == null, "Stable distinct hero identity"); Submit("storage-equip"); },
            () => { Require(Hero.Weapon != null && Count("iron-edge") == 1, "Independent ownership"); Submit("guild-tab-0"); },
            () => { Require(ui.SelectedId == "warrior-2", "Tabs retain hero"); Submit("guild-tab-1"); },
            () => { Require(Hero.Weapon != null && Label("storage-owners").Contains("warrior-1") && Label("storage-owners").Contains("warrior-2"), "Owner labels"); Roundtrip(); Fixture(1); },
            () => Submit("storage-equip"),
            () => SelectHero("warrior-2"),
            () => { Require(!Button("storage-equip").enabledInHierarchy && Label("storage-equip-reason").Contains("Нет свободных"), "Cannot take another owner's item"); SelectHero("warrior-1"); },
            () => Submit("storage-remove-Weapon"),
            () => SelectHero("warrior-2"),
            () => Submit("storage-equip"),
            () => { Require(Hero.Weapon != null && Count("iron-edge") == 0 && bootstrap.Guild.Roster.First(h => h.Id == "warrior-1").Weapon == null, "Transfer via explicit removal without duplication"); Fixture(1); },
            () => { Require(bootstrap.Guild.TryEquip("warrior-1", "iron-edge"), "Simulated external state update"); Submit("storage-equip"); },
            () => { Require(Label("storage-feedback").Contains("отклонена") && Count("iron-edge") == 0, "Stale command safely declined"); Fixture(1, dead: true); },
            () => { Require(!Button("storage-equip").enabledInHierarchy && Label("storage-equip-reason").Contains("живые"), "Recovered body cannot equip"); SelectHero("rogue-1"); },
            () => { Require(!Button("storage-equip").enabledInHierarchy, "Lost hero cannot equip"); Fixture(0); },
            () => { Submit("storage-filter-Armor"); },
            () => { Require(ui.ScreenRoot.Q<Label>("storage-empty") != null, "Empty filtered storage"); Fixture(100000); },
            () => { Submit("storage-filter-all"); },
            () => { Require(Label("item-count-iron-edge").Contains("99994"), "Large stock aggregates accurately"); Require(ui.ScreenRoot.Query<Button>().ToList().Count(b => b.name.StartsWith("storage-item-")) == 3, "Large stock does not create 100000 retained controls"); VerifyLayout(); Capture("1280-large-ru"); Localization.SetLanguage(InterfaceLanguage.English, false); },
            () => { VerifyLayout(); Capture("1280-en"); SetTarget(1920, 1080); },
            () => { VerifyLayout(); Capture("1920-en"); Localization.SetLanguage(InterfaceLanguage.Russian, false); },
            () => { VerifyLayout(); Capture("1920-ru"); SetTarget(1280, 720); },
            () => { var scroll = ui.ScreenRoot.Q<ScrollView>("equipment-scroll"); scroll.scrollOffset = new Vector2(0, 10000); },
            () => { Require(ui.ScreenRoot.Q<ScrollView>("equipment-scroll").scrollOffset.y > 0, "Equipment scrolls at small size"); Submit("storage-item-crypt-mail"); },
            () => { Require(ui.ScreenRoot.Q<ScrollView>("equipment-scroll").scrollOffset.y > 0, "Item selection retains equipment scroll"); Roundtrip(); },
        };
        private static int Count(string id) => bootstrap.Guild.Inventory.Count(i => i.Id == id);
        private static void SelectHero(string id)
        {
            var field = ui.ScreenRoot.Q<DropdownField>("storage-hero"); field.value = field.choices.First(c => c.EndsWith(" · " + id));
        }
        private static void Fixture(int copies, bool dead = false)
        {
            var data = GuildSaveData.Capture(new GuildState(), bootstrap.Expeditions);
            data.items = Enumerable.Repeat("iron-edge", copies).Concat(Enumerable.Repeat("crypt-mail", Math.Min(copies, 3))).Concat(Enumerable.Repeat("healing-draught", Math.Min(copies, 3))).ToArray();
            // The real save inventory limit is 100000 entries total.
            if (copies == 100000) data.items = Enumerable.Repeat("iron-edge", 99994).Concat(Enumerable.Repeat("crypt-mail", 3)).Concat(Enumerable.Repeat("healing-draught", 3)).ToArray();
            if (dead)
            {
                foreach (var hero in data.roster.Where(h => h.id == "warrior-1" || h.id == "rogue-1")) { hero.health = 0; hero.status = hero.id == "warrior-1" ? 1 : 2; }
                data.selected = data.selected.Where(id => id != "warrior-1" && id != "rogue-1").ToArray();
            }
            File.WriteAllText(path, JsonUtility.ToJson(data));
            Require(new GuildSaveStore(path).TryLoad(out var loaded, out _, out _), "Valid fixture");
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, loaded);
            // Reset selection through the party API to exercise the same stable ID as the UI.
            typeof(GuildPartyScreen).GetField("selectedId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(ui, "warrior-1");
        }
        private static void Roundtrip()
        {
            var store = new GuildSaveStore(path);
            string before = JsonUtility.ToJson(GuildSaveData.Capture(bootstrap.Guild, bootstrap.Expeditions));
            Require(store.TrySave(bootstrap.Guild, bootstrap.Expeditions, out _), "Save equipment");
            Require(store.TryLoad(out var loaded, out var offers, out _), "Load equipment");
            Require(before == JsonUtility.ToJson(GuildSaveData.Capture(loaded, offers)), "Inventory, slots, bonuses and quantities survive load exactly");
            typeof(GameBootstrap).GetProperty("Guild").SetValue(bootstrap, loaded);
        }
        private static void VerifyLayout()
        {
            var root = ui.ScreenRoot.worldBound;
            foreach (string name in new[] { "guild-storage", "equipment-scroll", "storage-scroll", "storage-details" })
            {
                var bounds = ui.ScreenRoot.Q(name).worldBound;
                Require(bounds.width > 100 && bounds.height > 90 && root.Contains(bounds.min) && root.Contains(bounds.max - Vector2.one), "Visible layout: " + name);
            }
            foreach (var icon in ui.ScreenRoot.Query<ItemIcon>().ToList()) Require(icon.worldBound.width >= 39.9f && icon.worldBound.height >= 39.9f, "Readable icon size: " + icon.worldBound);
            foreach (var label in ui.ScreenRoot.Query<Label>(className: "hero-panel-label").ToList())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                Require(label.contentRect.width > 0 && size.y <= label.contentRect.height + 2, "Unclipped label: " + label.name + " / " + label.text + " measured=" + size + " actual=" + label.contentRect);
            }
            Require(UiInputGate.Blocks(bootstrap), "Storage owns game input");
        }
        private static Button Button(string name) => ui.ScreenRoot.Q<Button>(name) ?? throw new InvalidOperationException("Missing " + name);
        private static string Label(string name) => ui.ScreenRoot.Q<Label>(name)?.text ?? "";
        private static void Submit(string name) { var button = Button(name); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
        private static void SubmitTwice(string name) { Submit(name); Submit(name); }
        private static void SetTarget(int width, int height)
        { var old = target; target = new RenderTexture(width, height, 24); target.Create(); ui.SetValidationTarget(target); if (old != null) UnityEngine.Object.DestroyImmediate(old); }
        private static void Capture(string name)
        {
            var old = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/wp36-" + name + ".png", texture.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-36: " + message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
            if (path != null) foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}

