using System;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Mouse input and temporary prototype HUD; selection is separate from grid data.</summary>
    public sealed class HexGridInteraction : MonoBehaviour
    {
        private const float CameraPadding = 1.12f;
        private const float CameraDistance = 10f;
        public const float HudHeight = 386f;
        private HexGrid grid;
        private HexLayout layout;
        private HexGridView view;
        private Camera gridCamera;
        public Camera GridCamera => gridCamera;
        private float previousAspect = -1;
        private int previousHeight = -1;
        public HexCoordinates? Hovered { get; private set; }
        public HexCoordinates? Selected { get; private set; }
        public event Action SelectionChanged;
        public event Action<HexCoordinates> CellClicked;

        public void Initialize(HexGrid model, HexLayout hexLayout, HexGridView gridView, Camera camera)
        {
            grid = model ?? throw new ArgumentNullException(nameof(model));
            layout = hexLayout ?? throw new ArgumentNullException(nameof(hexLayout));
            view = gridView != null ? gridView : throw new ArgumentNullException(nameof(gridView));
            gridCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            FrameCamera();
        }

        public void FrameCamera()
        {
            var bounds = layout.GetBounds(grid);
            gridCamera.orthographic = true;
            gridCamera.transform.rotation = Quaternion.identity;
            // Reserve screen space above the map for the prototype readout.
            float usableHeight = Mathf.Clamp01(1f - HudHeight / Mathf.Max(1, gridCamera.pixelHeight));
            // Keep framing consistent with the HUD input exclusion even in short windows.
            usableHeight = Mathf.Max(1f / Mathf.Max(1, gridCamera.pixelHeight), usableHeight);
            gridCamera.orthographicSize = Mathf.Max(bounds.extents.y / usableHeight,
                bounds.extents.x / Mathf.Max(0.01f, gridCamera.aspect)) * CameraPadding;
            gridCamera.transform.position = bounds.center + new Vector3(0,
                gridCamera.orthographicSize * (1f - usableHeight), -CameraDistance);
            previousAspect = gridCamera.aspect;
            previousHeight = gridCamera.pixelHeight;
        }

        private void Update()
        {
            if (grid == null) return;
            if (!Mathf.Approximately(previousAspect, gridCamera.aspect) || previousHeight != gridCamera.pixelHeight)
                FrameCamera();
            ProcessPointer(Input.mousePosition, Input.GetMouseButtonDown(0), Application.isFocused);
        }

        // Both runtime input and checks use the same screen -> plane -> cell path.
        public void ProcessPointer(Vector2 screenPosition, bool clicked, bool pointerAvailable = true)
        {
            Hovered = null;
            if (pointerAvailable && screenPosition.y < gridCamera.pixelRect.yMax - HudHeight &&
                gridCamera.pixelRect.Contains(screenPosition))
            {
                var ray = gridCamera.ScreenPointToRay(screenPosition);
                var plane = new Plane(Vector3.forward, Vector3.zero);
                if (plane.Raycast(ray, out float distance))
                {
                    var coordinate = layout.ToCoordinates(ray.GetPoint(distance));
                    if (grid.Contains(coordinate)) Hovered = coordinate;
                }
            }
            // A gameplay controller owns selection when subscribed. Without one, retain the
            // WP-02 cell-selection behavior for isolated use and editor checks.
            if (clicked && Hovered.HasValue)
            {
                if (CellClicked == null) SetSelected(Hovered);
                else CellClicked.Invoke(Hovered.Value);
            }
            view.SetHighlights(Hovered, Selected);
        }

        public void SetSelected(HexCoordinates? coordinate)
        {
            if (coordinate.HasValue && !grid.Contains(coordinate.Value))
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            if (Selected == coordinate) return;
            Selected = coordinate;
            if (view != null) view.SetHighlights(Hovered, Selected);
            SelectionChanged?.Invoke();
        }

        private void OnDisable()
        {
            Hovered = null;
            if (view != null) view.SetHighlights(null, Selected);
        }

        private void OnGUI()
        {
            if (grid == null) return;
            GUI.Label(new Rect(24, 16, 560, 24), "GUILD TACTICS / HEX PROTOTYPE");
            GUI.Label(new Rect(24, 40, 700, 24), "Green: move. Purple: action targets. Orange: trap. End turn: next unit.");
            GUI.Label(new Rect(24, 64, 560, 24),
                $"Hover: {Hovered?.ToString() ?? "—"} {HoveredTerrain()}    Selected: {Selected?.ToString() ?? "—"}");
            if (view.Visibility != null && GUI.Button(new Rect(24, 298, 280, 22),
                view.DebugVisibility ? "Vision debug ON: green / blue / black" : "Vision debug OFF"))
                view.ToggleVisibilityDebug();
        }

        private string HoveredTerrain()
        {
            if (!Hovered.HasValue) return "";
            if (view.Visibility == null) return grid.GetCell(Hovered.Value).Terrain.ToString();
            var state = view.Visibility.GetState(Hovered.Value);
            return view.Visibility.TryGetRememberedTerrain(Hovered.Value, out var terrain)
                ? state + " / " + terrain : state.ToString();
        }
    }
}
