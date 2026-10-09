using UnityEngine;

namespace GuildTactics.Core
{
    /// <summary>Owns the grid and wires the prototype presentation explicitly.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] 
        private Camera gridCamera;

        [SerializeField, Min(0)] 
        private float movementSecondsPerStep = 0.12f;

        [SerializeField]
        private int combatSeed = Combat.SeededDice.DefaultSeed;

        [SerializeField]
        private string dungeonSeed = "crypt-1";

        [SerializeField]
        private bool debugMode;

        [SerializeField]
        private Generation.DungeonGenerationConfig dungeonConfig = new Generation.DungeonGenerationConfig();

        [SerializeField]
        private Generation.EncounterConfig encounterConfig = new Generation.EncounterConfig();

        public HexGrid.HexGrid Grid { get; private set; }
        public Generation.DungeonMap Dungeon { get; private set; }
        private System.Collections.Generic.IReadOnlyList<Generation.EnemyPlacement> encounter;
        private GameObject presentation;
        public Meta.GuildState Guild { get; private set; }
        public Units.PlayerUnitController ActiveController { get; private set; }
        public Expeditions.ExpeditionSelection Expeditions { get; private set; }
        public bool DebugMode => debugMode;
        private Meta.GuildSaveStore saveStore;
        public string SaveMessage { get; private set; }
        public Expeditions.ExpeditionJourney Journey { get; private set; }
        public Expeditions.ExpeditionResult JourneyBoundary { get; private set; }
        private Meta.GuildSaveData departureSave;
        private Expeditions.ExpeditionRun capturedRun;
        private bool boundarySaved;
        public bool BoundarySaved => boundarySaved;
        public bool CanContinueJourney => JourneyBoundary != null && Journey.Completed < Journey.Sections && boundarySaved && !Journey.EventPending && !Journey.IsDefeated;

        private void Awake()
        {
            if (!Application.isBatchMode) Localization.LoadPreference();
            Guild = new Meta.GuildState();
            try { Expeditions = new Expeditions.ExpeditionSelection(dungeonSeed); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning("Empty dungeon seed; using crypt-1.", this);
                Expeditions = new Expeditions.ExpeditionSelection("crypt-1");
            }
            // Batch validation uses isolated stores and must never touch a player's save.
            if (!Application.isBatchMode)
            {
                saveStore = new Meta.GuildSaveStore(System.IO.Path.Combine(Application.persistentDataPath, "guild-v1.json"));
                if (saveStore.TryLoad(out var savedGuild, out var savedExpeditions, out var message))
                { Guild = savedGuild; Expeditions = savedExpeditions; }
                SaveMessage = message;
            }
            Guild.Changed += SaveProgress;
            if (saveStore?.LoadedCheckpoint != null)
                RestoreJourney(saveStore.LoadedCheckpoint);
            GenerateDungeon();
        }

        private void GenerateDungeon()
        {
            try
            {
                Dungeon = Generation.DungeonGenerator.Generate(Journey?.NextSectionSeed ?? Expeditions.NextSeed, Journey?.DungeonConfig ?? dungeonConfig);
                encounter = Generation.EncounterGenerator.Generate(Dungeon,
                    Journey?.EncounterConfig ?? (debugMode ? encounterConfig : Expeditions.Selected.CreateEncounterConfig()));
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogWarning("Invalid generation settings; using safe defaults. " + exception.Message, this);
                Dungeon = Generation.DungeonGenerator.Generate(Journey?.NextSectionSeed ?? Expeditions.NextSeed);
                encounter = Generation.EncounterGenerator.Generate(Dungeon);
            }
            Grid = Dungeon.Grid;
            Debug.Log($"Dungeon seed {Dungeon.Seed}; fallback {Dungeon.UsedFallback}; objective candidate {Dungeon.Objective}.", this);
        }

        private void Start()
        {
            if (gridCamera is null)
            {
                Debug.LogError("Assign the battlefield camera to GameBootstrap.", this);
                enabled = false;
                return;
            }

            gameObject.AddComponent<Meta.GuildUI>().Initialize(this);
            gameObject.AddComponent<Expeditions.JourneyUI>().Initialize(this);
        }

        public bool TryLaunchExpedition()
        {
            if (gridCamera == null || !Guild.CanLaunch || presentation != null) 
                return false;

            SaveProgress();
            departureSave = Meta.GuildSaveData.Capture(Guild, Expeditions);
            try
            {
                Journey = new Expeditions.ExpeditionJourney(Expeditions.NextSeed, Expeditions.SelectedIndex, combatSeed,
                    dungeonConfig, debugMode ? encounterConfig : Expeditions.Selected.CreateEncounterConfig());
            }
            catch (System.ArgumentException)
            {
                Journey = new Expeditions.ExpeditionJourney(Expeditions.NextSeed, Expeditions.SelectedIndex, combatSeed,
                    new Generation.DungeonGenerationConfig(), Expeditions.Selected.CreateEncounterConfig());
            }
            var party = Guild.BeginExpedition();
            try
            {
                GenerateDungeon();
                CreateBattle(party);
                Guild.AttachRun(ActiveController.Expedition);
                Expeditions.RecordLaunch();
                return true;
            }
            catch
            {
                if (presentation != null) { presentation.SetActive(false); Destroy(presentation); }
                presentation = null; ActiveController = null;
                Guild.CancelLaunch();
                Journey = null; departureSave = null;
                throw;
            }
        }

        public bool TryReturnToGuild()
        {
            CaptureJourneyBoundary();
            if (JourneyBoundary != null && Journey.EventPending) return false;
            var result = JourneyBoundary ?? ActiveController?.Expedition.Result;
            if (!Guild.TryReturn(result))
                return false;
            if (presentation != null) { presentation.SetActive(false); Destroy(presentation); }
            presentation = null; ActiveController = null;
            Journey = null; JourneyBoundary = null; departureSave = null; capturedRun = null;
            return true;
        }

        private void Update() => CaptureJourneyBoundary();

        internal void CaptureJourneyBoundary()
        {
            var run = ActiveController?.Expedition;
            if (Journey == null || JourneyBoundary != null || run == null || run == capturedRun ||
                run.Result?.Outcome != GuildTactics.Expeditions.ExpeditionOutcome.Extracted) return;
            Journey.Complete(run.Result);
            capturedRun = run;
            JourneyBoundary = run.Result;
            ActiveController.BossAttack?.Cancel();
            RetryJourneySave();
        }

        public bool TryResolveExplorationEvent(int choice, string heroId = null)
        {
            if (JourneyBoundary == null || !boundarySaved || !Journey.TryResolveEvent(Guild, choice, heroId)) return false;
            JourneyBoundary = Journey.BoundaryResult(Guild);
            RetryJourneySave();
            return true;
        }

        public bool RetryJourneySave()
        {
            if (JourneyBoundary == null) return false;
            string message = null;
            boundarySaved = saveStore == null || saveStore.TrySaveCheckpoint(departureSave, Expeditions, Journey.Capture(), out message);
            SaveMessage = message;
            return boundarySaved;
        }

        internal void RestoreJourney(Expeditions.JourneyCheckpoint checkpoint)
        {
            Journey = GuildTactics.Expeditions.ExpeditionJourney.Restore(checkpoint, Guild);
            departureSave = Meta.GuildSaveData.Capture(Guild, Expeditions);
            JourneyBoundary = Journey.BoundaryResult(Guild);
            Guild.BeginExpedition();
            boundarySaved = true;
        }

        public bool TryContinueJourney()
        {
            CaptureJourneyBoundary();
            if (!CanContinueJourney || gridCamera == null) return false;
            var previousPresentation = presentation;
            var previousController = ActiveController;
            var previousDungeon = Dungeon;
            var previousEncounter = encounter;
            try
            {
                GenerateDungeon();
                CreateBattle(Journey.ContinuingParty(Guild));
                Guild.AttachContinuingRun(ActiveController.Expedition);
            }
            catch (System.Exception error)
            {
                if (presentation != null && presentation != previousPresentation)
                { presentation.SetActive(false); Destroy(presentation); }
                presentation = previousPresentation; ActiveController = previousController;
                Dungeon = previousDungeon; Grid = Dungeon.Grid; encounter = previousEncounter;
                Debug.LogException(error, this);
                SaveMessage = "Could not continue expedition. Retry or return to the guild.";
                return false;
            }
            // Disable synchronously: Destroy alone would leave the old input alive for this frame.
            if (previousPresentation != null) { previousPresentation.SetActive(false); Destroy(previousPresentation); }
            JourneyBoundary = null;
            SaveMessage = null;
            return true;
        }

        public bool TrySelectExpedition(int index)
        {
            if (Guild.IsAway || index == Expeditions.SelectedIndex || !Expeditions.TrySelect(index)) 
                return false;
            
            SaveProgress(); 
            
            return true;
        }

        public bool TryStartNewGuild()
        {
            if (Guild.IsAway) 
                return false;

            Guild.Changed -= SaveProgress;
            Guild = new Meta.GuildState();

            Expeditions = new Expeditions.ExpeditionSelection(Expeditions.InitialSeed, 1, 0);

            Guild.Changed += SaveProgress;

            SaveProgress();
            return true;
        }

        private void SaveProgress()
        {
            if (saveStore == null || Guild.IsAway) return;
            saveStore.TrySave(Guild, Expeditions, out var message);
            SaveMessage = message;
        }

        private void OnDestroy() 
        { 
            if (Guild != null) Guild.Changed -= SaveProgress; 
        }

        private void CreateBattle(System.Collections.Generic.IReadOnlyList<Meta.GuildAdventurer> party)
        {
            var layout = new HexGrid.HexLayout();
            presentation = new GameObject("Hex Grid");
            presentation.transform.SetParent(transform, false);
            var view = presentation.AddComponent<HexGrid.HexGridView>();
            view.Initialize(Grid, layout);
            var interaction = presentation.AddComponent<HexGrid.HexGridInteraction>();
            interaction.Initialize(Grid, layout, view, gridCamera);
            interaction.DebugMode = debugMode;
            var feedback = presentation.AddComponent<Combat.CombatText>();
            int sectionCombatSeed = Journey == null ? combatSeed :
                GuildTactics.Expeditions.ExpeditionJourney.SectionSeed(Journey.CombatSeed, Journey.Completed);
            feedback.Initialize(gridCamera, sectionCombatSeed);
            var units = presentation.AddComponent<Units.PlayerUnitController>();
            units.Initialize(Grid, layout, view, interaction, movementSecondsPerStep,
                new Combat.SeededDice(sectionCombatSeed), feedback, enableFog: true,
                playerSpawns: Dungeon.PlayerSpawns, encounter: encounter, expeditionMap: Dungeon, guildParty: party, mission: Expeditions.Selected.Mission,
                composeResult: Journey?.ResultComposer(Guild));
            ActiveController = units;
            presentation.AddComponent<Combat.TurnOrderUI>().Initialize(units);
            presentation.AddComponent<Abilities.ActionBarUI>().Initialize(units);
            presentation.AddComponent<Expeditions.ExpeditionUI>().Initialize(units, layout, gridCamera, this);
            Debug.Log($"Guild Tactics: grid ready ({Grid.Width} x {Grid.Height}, {Grid.Cells.Count} cells, {units.Units.Count} heroes, {units.Enemies.Count} enemies, combat seed {combatSeed}).", this);
        }

        private void OnGUI()
        {
            // HexGridInteraction excludes these buttons from battlefield clicks.
            int previousDepth = GUI.depth;
            GUI.depth = -100;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = true;
            float x = Screen.width - 112;
            float y = Screen.height - 25;
            if (GUI.Toggle(new Rect(x, y, 50, 22), Localization.Language == InterfaceLanguage.Russian, "RU", GUI.skin.button))
                if (Localization.Language != InterfaceLanguage.Russian) Localization.SetLanguage(InterfaceLanguage.Russian);
            if (GUI.Toggle(new Rect(x + 54, y, 50, 22), Localization.Language == InterfaceLanguage.English, "EN", GUI.skin.button))
                if (Localization.Language != InterfaceLanguage.English) Localization.SetLanguage(InterfaceLanguage.English);
            GUI.enabled = previousEnabled;
            GUI.depth = previousDepth;
            if (debugMode && Dungeon != null && Guild.IsAway)
                GUI.Label(new Rect(24, Screen.height - 28, Screen.width - 48, 24),
                    Localization.F("Dungeon seed: {0} | Combat seed: {1}", Dungeon.Seed, combatSeed) + (Dungeon.UsedFallback ? Localization.T(" (fallback)") : ""));
        }
    }
}
