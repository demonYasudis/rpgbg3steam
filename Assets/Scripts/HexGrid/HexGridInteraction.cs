using System;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Mouse input and temporary prototype HUD; selection is separate from grid data.</summary>
    public sealed class HexGridInteraction : MonoBehaviour
    {
        private const float CameraPadding = 1.12f;
        private const float CameraDistance = 10f;
        public const float HudHeight = 124f;
        private HexGrid grid;
        private HexLayout layout;
        private HexGridView view;
        private Camera gridCamera;
        private float previousAspect = -1;
        private int previousHeight = -1;
        public HexCoordinates? Hovered { get; private set; }
        public HexCoordinates? Selected { get; private set; }
        public event Action SelectionChanged;

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
            usableHeight = Mathf.Max(0.25f, usableHeight);
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
            if (pointerAvailable && gridCamera.pixelRect.Contains(screenPosition))
            {
                var ray = gridCamera.ScreenPointToRay(screenPosition);
                var plane = new Plane(Vector3.forward, Vector3.zero);
                if (plane.Raycast(ray, out float distance))
                {
                    var coordinate = layout.ToCoordinates(ray.GetPoint(distance));
                    if (grid.Contains(coordinate)) Hovered = coordinate;
                }
            }
            // Clicking outside preserves selection. The small visual gutters belong to their hex.
            bool selectionChanged = clicked && Hovered.HasValue && Selected != Hovered;
            if (selectionChanged) Selected = Hovered;
            view.SetHighlights(Hovered, Selected);
            if (selectionChanged) SelectionChanged?.Invoke();
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
            GUI.Label(new Rect(24, 40, 560, 24), "Hover: cyan   |   Left click: select (gold)");
            GUI.Label(new Rect(24, 64, 560, 24),
                $"Hover: {Hovered?.ToString() ?? "—"}    Selected: {Selected?.ToString() ?? "—"}");
        }
    }
}
