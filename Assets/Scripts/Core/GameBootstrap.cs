using UnityEngine;

namespace GuildTactics.Core
{
    /// <summary>Owns the grid and wires the prototype presentation explicitly.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private Camera gridCamera;
        [SerializeField, Min(0)] private int previewMovementBudget = HexGrid.HexMovementPreview.DefaultBudget;
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
            var movementPreview = presentation.AddComponent<HexGrid.HexMovementPreview>();
            movementPreview.Initialize(Grid, view, interaction, previewMovementBudget);
            Debug.Log($"Guild Tactics: grid ready ({Grid.Width} x {Grid.Height}, {Grid.Cells.Count} cells).", this);
        }
    }
}
