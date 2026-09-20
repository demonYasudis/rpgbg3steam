using System;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Temporary selected-cell movement preview until unit selection exists.</summary>
    public sealed class HexMovementPreview : MonoBehaviour
    {
        public const int DefaultBudget = 4;
        private HexGrid grid;
        private HexGridView view;
        private HexGridInteraction interaction;
        public int MovementBudget { get; private set; }
        public HexMovementRange CurrentRange { get; private set; }

        public void Initialize(HexGrid model, HexGridView gridView, HexGridInteraction gridInteraction, int budget)
        {
            if (grid != null) throw new InvalidOperationException("Preview is already initialized.");
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            grid = model ?? throw new ArgumentNullException(nameof(model));
            view = gridView != null ? gridView : throw new ArgumentNullException(nameof(gridView));
            interaction = gridInteraction != null ? gridInteraction : throw new ArgumentNullException(nameof(gridInteraction));
            MovementBudget = budget;
            if (isActiveAndEnabled) interaction.SelectionChanged += Refresh;
            Refresh();
        }

        public void SetMovementBudget(int budget)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            MovementBudget = budget;
            Refresh();
        }

        // Call after external grid mutations; this component never changes terrain or occupancy.
        public void Refresh()
        {
            if (grid == null || !isActiveAndEnabled) return;
            CurrentRange = interaction.Selected.HasValue
                ? HexPathfinder.FindReachable(grid, interaction.Selected.Value, MovementBudget) : null;
            view.SetReachableCells(CurrentRange == null ? null : CurrentRange.Costs.Keys);
        }

        private void OnEnable()
        {
            if (interaction == null) return;
            interaction.SelectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (interaction != null) interaction.SelectionChanged -= Refresh;
            CurrentRange = null;
            if (view != null) view.SetReachableCells(null);
        }

        private void OnGUI()
        {
            if (grid == null) return;
            int destinations = CurrentRange == null ? 0 : Math.Max(0, CurrentRange.Costs.Count - 1);
            GUI.Label(new Rect(24, 88, 700, 24),
                $"Movement preview: {MovementBudget} points | Green: {destinations} reachable cells | Click a starting cell");
        }
    }
}
