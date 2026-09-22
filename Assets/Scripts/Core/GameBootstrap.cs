using UnityEngine;

namespace GuildTactics.Core
{
    /// <summary>Owns the grid and wires the prototype presentation explicitly.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private Camera gridCamera;
        [SerializeField, Min(0)] private float movementSecondsPerStep = 0.12f;
        [SerializeField] private int combatSeed = Combat.SeededDice.DefaultSeed;
        public HexGrid.HexGrid Grid { get; private set; }

        private void Awake()
        {
            Grid = new HexGrid.HexGrid();
        }

        private void Start()
        {
            if (gridCamera == null)
            {
                Debug.LogError("Assign the battlefield camera to GameBootstrap.", this);
                enabled = false;
                return;
            }
            var layout = new HexGrid.HexLayout();
            var presentation = new GameObject("Hex Grid");
            presentation.transform.SetParent(transform, false);
            var view = presentation.AddComponent<HexGrid.HexGridView>();
            view.Initialize(Grid, layout);
            var interaction = presentation.AddComponent<HexGrid.HexGridInteraction>();
            interaction.Initialize(Grid, layout, view, gridCamera);
            var feedback = presentation.AddComponent<Combat.CombatText>();
            feedback.Initialize(gridCamera, combatSeed);
            var units = presentation.AddComponent<Units.PlayerUnitController>();
            units.Initialize(Grid, layout, view, interaction, movementSecondsPerStep,
                new Combat.SeededDice(combatSeed), feedback);
            presentation.AddComponent<Combat.TurnOrderUI>().Initialize(units);
            Debug.Log($"Guild Tactics: grid ready ({Grid.Width} x {Grid.Height}, {Grid.Cells.Count} cells, {units.Units.Count} heroes, {units.Enemies.Count} enemies, combat seed {combatSeed}).", this);
        }
    }
}
