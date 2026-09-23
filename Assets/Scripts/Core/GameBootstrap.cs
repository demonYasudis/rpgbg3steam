using UnityEngine;

namespace GuildTactics.Core
{
    /// <summary>Owns the grid and wires the prototype presentation explicitly.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private Camera gridCamera;
        [SerializeField, Min(0)] private float movementSecondsPerStep = 0.12f;
        [SerializeField] private int combatSeed = Combat.SeededDice.DefaultSeed;
        [SerializeField] private string dungeonSeed = "crypt-1";
        [SerializeField] private Generation.DungeonGenerationConfig dungeonConfig = new Generation.DungeonGenerationConfig();
        [SerializeField] private Generation.EncounterConfig encounterConfig = new Generation.EncounterConfig();
        public HexGrid.HexGrid Grid { get; private set; }
        public Generation.DungeonMap Dungeon { get; private set; }
        private System.Collections.Generic.IReadOnlyList<Generation.EnemyPlacement> encounter;
        private GameObject presentation;
        public Meta.GuildState Guild { get; private set; }
        public Units.PlayerUnitController ActiveController { get; private set; }

        private void Awake()
        {
            Guild = new Meta.GuildState();
            GenerateDungeon();
        }

        private void GenerateDungeon()
        {
            try
            {
                Dungeon = Generation.DungeonGenerator.Generate(dungeonSeed, dungeonConfig);
                encounter = Generation.EncounterGenerator.Generate(Dungeon, encounterConfig);
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogWarning("Invalid generation settings; using safe defaults. " + exception.Message, this);
                Dungeon = Generation.DungeonGenerator.Generate("crypt-1");
                encounter = Generation.EncounterGenerator.Generate(Dungeon);
            }
            Grid = Dungeon.Grid;
            Debug.Log($"Dungeon seed {Dungeon.Seed}; fallback {Dungeon.UsedFallback}; objective candidate {Dungeon.Objective}.", this);
        }

        private void Start()
        {
            if (gridCamera == null)
            {
                Debug.LogError("Assign the battlefield camera to GameBootstrap.", this);
                enabled = false;
                return;
            }
            gameObject.AddComponent<Meta.GuildUI>().Initialize(this);
        }

        public bool TryLaunchExpedition()
        {
            if (gridCamera == null || !Guild.CanLaunch || presentation != null) return false;
            var party = Guild.BeginExpedition();
            try
            {
                GenerateDungeon();
                CreateBattle(party);
                Guild.AttachRun(ActiveController.Expedition);
                return true;
            }
            catch
            {
                if (presentation != null) { presentation.SetActive(false); Destroy(presentation); }
                presentation = null; ActiveController = null;
                Guild.CancelLaunch();
                throw;
            }
        }

        public bool TryReturnToGuild()
        {
            if (!Guild.TryReturn()) return false;
            presentation.SetActive(false);
            Destroy(presentation);
            presentation = null; ActiveController = null;
            return true;
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
            var feedback = presentation.AddComponent<Combat.CombatText>();
            feedback.Initialize(gridCamera, combatSeed);
            var units = presentation.AddComponent<Units.PlayerUnitController>();
            units.Initialize(Grid, layout, view, interaction, movementSecondsPerStep,
                new Combat.SeededDice(combatSeed), feedback, enableFog: true,
                playerSpawns: Dungeon.PlayerSpawns, encounter: encounter, expeditionMap: Dungeon, guildParty: party);
            ActiveController = units;
            presentation.AddComponent<Combat.TurnOrderUI>().Initialize(units);
            presentation.AddComponent<Abilities.ActionBarUI>().Initialize(units);
            presentation.AddComponent<Expeditions.ExpeditionUI>().Initialize(units, layout, gridCamera, this);
            Debug.Log($"Guild Tactics: grid ready ({Grid.Width} x {Grid.Height}, {Grid.Cells.Count} cells, {units.Units.Count} heroes, {units.Enemies.Count} enemies, combat seed {combatSeed}).", this);
        }

        private void OnGUI()
        {
            if (Dungeon != null && Guild.IsAway)
                GUI.Label(new Rect(24, Screen.height - 28, Screen.width - 48, 24),
                    $"Dungeon seed: {Dungeon.Seed}" + (Dungeon.UsedFallback ? " (fallback)" : ""));
        }
    }
}
