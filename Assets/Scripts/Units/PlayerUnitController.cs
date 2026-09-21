using System;
using System.Collections;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Units
{
    /// <summary>Coordinates player unit selection, legal movement and its placeholder animation.</summary>
    public sealed class PlayerUnitController : MonoBehaviour
    {
        private static readonly HexCoordinates[] SpawnCoordinates =
        {
            new HexCoordinates(1, 1), new HexCoordinates(1, 2),
            new HexCoordinates(2, 1), new HexCoordinates(2, 2)
        };
        private static readonly Color[] HeroColors =
        {
            new Color(0.72f, 0.25f, 0.22f), new Color(0.55f, 0.38f, 0.72f),
            new Color(0.23f, 0.58f, 0.34f), new Color(0.22f, 0.45f, 0.76f)
        };

        private readonly List<UnitRuntimeState> units = new List<UnitRuntimeState>();
        private readonly Dictionary<string, UnitView> views = new Dictionary<string, UnitView>(StringComparer.Ordinal);
        private GridModel grid;
        private HexLayout layout;
        private HexGridView gridView;
        private HexGridInteraction interaction;
        private float secondsPerStep;
        private Coroutine movementRoutine;
        public IReadOnlyList<UnitRuntimeState> Units { get; private set; }
        public UnitRuntimeState SelectedUnit { get; private set; }
        public HexMovementRange CurrentRange { get; private set; }
        public bool IsMoving => movementRoutine != null;

        public void Initialize(GridModel model, HexLayout hexLayout, HexGridView view,
            HexGridInteraction gridInteraction, float moveSecondsPerStep)
        {
            if (grid != null) throw new InvalidOperationException("Unit controller is already initialized.");
            if (float.IsNaN(moveSecondsPerStep) || float.IsInfinity(moveSecondsPerStep) || moveSecondsPerStep < 0)
                throw new ArgumentOutOfRangeException(nameof(moveSecondsPerStep));
            grid = model ?? throw new ArgumentNullException(nameof(model));
            layout = hexLayout ?? throw new ArgumentNullException(nameof(hexLayout));
            gridView = view != null ? view : throw new ArgumentNullException(nameof(view));
            interaction = gridInteraction != null ? gridInteraction : throw new ArgumentNullException(nameof(gridInteraction));
            secondsPerStep = moveSecondsPerStep;

            var definitions = HeroDefinitions.Defaults;
            if (definitions.Count != SpawnCoordinates.Length)
                throw new InvalidOperationException("The prototype requires exactly four hero definitions.");
            try
            {
                for (int index = 0; index < definitions.Count; index++)
                    Spawn("hero-" + definitions[index].Id, definitions[index], SpawnCoordinates[index], HeroColors[index]);
            }
            catch
            {
                foreach (var unit in units) grid.TryVacate(unit.Position, unit.InstanceId);
                foreach (var unitView in views.Values) Destroy(unitView.gameObject);
                units.Clear();
                views.Clear();
                throw;
            }

            Units = units.AsReadOnly();
            interaction.CellClicked += HandleCellClicked;
        }

        public bool TrySelectUnit(HexCoordinates coordinate)
        {
            if (grid == null || IsMoving) return false;
            UnitRuntimeState found = null;
            foreach (var unit in units)
                if (unit.Position == coordinate) { found = unit; break; }
            if (found == null) return false;
            SelectedUnit = found;
            RefreshSelection();
            return true;
        }

        public void SetAnimationSecondsPerStep(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (IsMoving) throw new InvalidOperationException("Cannot change animation speed during movement.");
            secondsPerStep = value;
        }

        public bool TryMoveSelected(HexCoordinates destination)
        {
            if (grid == null || SelectedUnit == null || IsMoving || destination == SelectedUnit.Position)
                return false;

            // Recalculate immediately before commit so stale highlights never grant permission.
            var legalRange = HexPathfinder.FindReachable(grid, SelectedUnit.Position, SelectedUnit.Definition.Movement);
            var path = legalRange.GetPathTo(destination);
            if (path.Count < 2 || !SelectedUnit.TryMoveAlong(grid, path))
            {
                RefreshSelection();
                return false;
            }

            CurrentRange = null;
            gridView.SetReachableCells(null);
            interaction.SetSelected(SelectedUnit.Position);
            if (secondsPerStep <= 0)
            {
                views[SelectedUnit.InstanceId].SnapTo(layout.ToWorld(SelectedUnit.Position));
                RefreshSelection();
            }
            else movementRoutine = StartCoroutine(AnimateMovement(SelectedUnit, path));
            return true;
        }

        private void Spawn(string instanceId, UnitDefinition definition, HexCoordinates coordinate, Color color)
        {
            if (!UnitRuntimeState.TrySpawn(grid, instanceId, definition, coordinate, out var unit))
                throw new InvalidOperationException("Cannot spawn " + instanceId + " at " + coordinate + ".");
            GameObject unitObject = null;
            try
            {
                unitObject = new GameObject(definition.DisplayName + " (" + instanceId + ")");
                unitObject.transform.SetParent(transform, false);
                var unitView = unitObject.AddComponent<UnitView>();
                unitView.Initialize(unit, layout, color);
                units.Add(unit);
                views.Add(instanceId, unitView);
            }
            catch
            {
                grid.TryVacate(coordinate, instanceId);
                if (unitObject != null) Destroy(unitObject);
                throw;
            }
        }

        private void HandleCellClicked(HexCoordinates coordinate)
        {
            if (IsMoving) return;
            if (TrySelectUnit(coordinate)) return;
            if (!TryMoveSelected(coordinate) && SelectedUnit != null)
                interaction.SetSelected(SelectedUnit.Position);
        }

        private void RefreshSelection()
        {
            foreach (var pair in views) pair.Value.SetSelected(SelectedUnit != null && pair.Key == SelectedUnit.InstanceId);
            interaction.SetSelected(SelectedUnit?.Position);
            CurrentRange = SelectedUnit == null ? null :
                HexPathfinder.FindReachable(grid, SelectedUnit.Position, SelectedUnit.Definition.Movement);
            gridView.SetReachableCells(CurrentRange == null ? null : CurrentRange.Costs.Keys);
        }

        private IEnumerator AnimateMovement(UnitRuntimeState movingUnit, IReadOnlyList<HexCoordinates> path)
        {
            var unitView = views[movingUnit.InstanceId];
            for (int index = 1; index < path.Count; index++)
            {
                Vector3 from = layout.ToWorld(path[index - 1]);
                Vector3 to = layout.ToWorld(path[index]);
                float elapsed = 0;
                while (elapsed < secondsPerStep)
                {
                    elapsed += Time.deltaTime;
                    unitView.SetWorldPosition(Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / secondsPerStep)));
                    yield return null;
                }
                unitView.SnapTo(to);
            }
            movementRoutine = null;
            RefreshSelection();
        }

        private void OnDisable()
        {
            if (interaction != null) interaction.CellClicked -= HandleCellClicked;
            if (movementRoutine != null)
            {
                StopCoroutine(movementRoutine);
                movementRoutine = null;
                if (SelectedUnit != null && views.TryGetValue(SelectedUnit.InstanceId, out var view))
                    view.SnapTo(layout.ToWorld(SelectedUnit.Position));
            }
            CurrentRange = null;
            if (gridView != null) gridView.SetReachableCells(null);
        }

        private void OnEnable()
        {
            if (interaction != null) interaction.CellClicked += HandleCellClicked;
        }

        private void OnGUI()
        {
            if (grid == null) return;
            string status = IsMoving ? "Moving..." : SelectedUnit == null
                ? "Select one of four heroes"
                : $"{SelectedUnit.Definition.DisplayName} | Move {SelectedUnit.Definition.Movement} | Green: {Math.Max(0, CurrentRange?.Costs.Count - 1 ?? 0)} destinations";
            GUI.Label(new Rect(24, 88, 760, 24), status);
        }
    }
}
