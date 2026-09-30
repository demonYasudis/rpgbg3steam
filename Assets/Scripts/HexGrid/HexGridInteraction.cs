using System;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Mouse input and temporary prototype HUD; selection is separate from grid data.</summary>
    public sealed class HexGridInteraction : MonoBehaviour
    {
        private const float CameraPadding = 1.12f;
        private const float CameraDistance = 10f;
        public const float HudHeight = 230f;
        private const float FooterHeight = 32f;
        public bool DebugMode { get; set; }
        private HexGrid grid;
        private HexLayout layout;
        private HexGridView view;
        private Camera gridCamera;
        public Camera GridCamera => gridCamera;
        private float previousAspect = -1;
        private int previousHeight = -1;
        // Most generated standing silhouettes are about 0.8 world units tall.
        public const float DetailPixelsPerUnit = 80f;
        private float pixelsPerUnit = DetailPixelsPerUnit;
        private bool detailView;
        private Vector3 focusPoint;
        private Vector2 previousPointer;
        private bool dragging;
        public bool DetailView => detailView;
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
            ShowCharacterDetail();
        }

        public void FrameCamera()
        {
            detailView = false;
            var bounds = layout.GetBounds(grid);
            gridCamera.orthographic = true;
            gridCamera.backgroundColor = new Color(0.035f, 0.04f, 0.065f);
            gridCamera.allowMSAA = false;
            gridCamera.transform.rotation = Quaternion.identity;
            // Reserve screen space above the map for the prototype readout.
            float usableHeight = Mathf.Clamp01(1f - (HudHeight + FooterHeight) / Mathf.Max(1, gridCamera.pixelHeight));
            // Keep framing consistent with the HUD input exclusion even in short windows.
            usableHeight = Mathf.Max(1f / Mathf.Max(1, gridCamera.pixelHeight), usableHeight);
            gridCamera.orthographicSize = Mathf.Max(bounds.extents.y / usableHeight,
                bounds.extents.x / Mathf.Max(0.01f, gridCamera.aspect)) * CameraPadding;
            // Whole screen pixels per 16px unit texel where the full board fits.
            // Short windows retain fit-to-board with nearest filtering instead of cropping cells.
            float texelScale = gridCamera.pixelHeight / (2f * gridCamera.orthographicSize * 16f);
            if (texelScale >= 1f)
                gridCamera.orthographicSize = gridCamera.pixelHeight / (32f * Mathf.Floor(texelScale));
            gridCamera.transform.position = bounds.center + new Vector3(0,
                gridCamera.orthographicSize * (HudHeight - FooterHeight) / Mathf.Max(1, gridCamera.pixelHeight), -CameraDistance);
            previousAspect = gridCamera.aspect;
            previousHeight = gridCamera.pixelHeight;
        }

        public void ShowCharacterDetail()
        {
            detailView = true;
            pixelsPerUnit = DetailPixelsPerUnit;
            focusPoint = Selected.HasValue ? layout.ToWorld(Selected.Value) : layout.GetBounds(grid).center;
            ApplyDetailCamera();
        }

        private void ApplyDetailCamera()
        {
            var bounds = layout.GetBounds(grid);
            focusPoint.x = Mathf.Clamp(focusPoint.x, bounds.min.x, bounds.max.x);
            focusPoint.y = Mathf.Clamp(focusPoint.y, bounds.min.y, bounds.max.y);
            gridCamera.orthographicSize = Mathf.Max(1, gridCamera.pixelHeight) / (2f * pixelsPerUnit);
            gridCamera.transform.position = new Vector3(focusPoint.x,
                focusPoint.y + (HudHeight - FooterHeight) / (2f * pixelsPerUnit), -CameraDistance);
            previousAspect = gridCamera.aspect;
            previousHeight = gridCamera.pixelHeight;
        }

        public void PanCamera(Vector2 screenDelta)
        {
            if (!detailView) return;
            focusPoint -= new Vector3(screenDelta.x, screenDelta.y) / pixelsPerUnit;
            ApplyDetailCamera();
        }

        public void ZoomCamera(float scroll)
        {
            if (Mathf.Approximately(scroll, 0)) return;
            if (!detailView)
            {
                focusPoint = gridCamera.transform.position - new Vector3(0,
                    (HudHeight - FooterHeight) * gridCamera.orthographicSize / gridCamera.pixelHeight, -CameraDistance);
                pixelsPerUnit = gridCamera.pixelHeight / (2f * gridCamera.orthographicSize);
                detailView = true;
            }
            pixelsPerUnit = Mathf.Clamp(pixelsPerUnit * Mathf.Pow(1.2f, scroll), 12f, 160f);
            ApplyDetailCamera();
        }

        private void Update()
        {
            if (grid == null) return;
            var controller = GetComponent<Units.PlayerUnitController>();
            if (controller != null && (controller.InterfaceBlocked || controller.Expedition?.Result != null))
            { dragging = false; return; }
            if (!Mathf.Approximately(previousAspect, gridCamera.aspect) || previousHeight != gridCamera.pixelHeight)
            {
                if (detailView) ApplyDetailCamera(); else FrameCamera();
            }
            Vector2 pointer = Input.mousePosition;
            bool onMap = gridCamera.pixelRect.Contains(pointer) && pointer.y > 32 &&
                pointer.y < gridCamera.pixelRect.yMax - HudHeight;
            if (Application.isFocused && onMap) ZoomCamera(Input.mouseScrollDelta.y);
            if (!Application.isFocused || !Input.GetMouseButton(1)) dragging = false;
            if (Application.isFocused && onMap && Input.GetMouseButtonDown(1))
            { dragging = true; previousPointer = pointer; }
            if (dragging) PanCamera(pointer - previousPointer);
            previousPointer = pointer;
            ProcessPointer(pointer, Input.GetMouseButtonDown(0) && !dragging, Application.isFocused);
        }

        // Both runtime input and checks use the same screen -> plane -> cell path.
        public void ProcessPointer(Vector2 screenPosition, bool clicked, bool pointerAvailable = true)
        {
            Hovered = null;
            bool overLanguageButtons = screenPosition.x >= Screen.width - 112 && screenPosition.y <= 28;
            if (pointerAvailable && !overLanguageButtons && screenPosition.y > 32 && screenPosition.y < gridCamera.pixelRect.yMax - HudHeight &&
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
            if (detailView && coordinate.HasValue)
            {
                focusPoint = layout.ToWorld(coordinate.Value);
                ApplyDetailCamera();
            }
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
            var controller = GetComponent<Units.PlayerUnitController>();
            if (controller != null && (controller.InterfaceBlocked || controller.Expedition?.Result != null)) return;
            if (GUI.Button(new Rect(16, Screen.height - 30, 110, 24),
                Core.Localization.T(detailView ? "Map overview" : "Character detail")))
            {
                if (detailView) FrameCamera(); else ShowCharacterDetail();
            }
            GUI.Label(new Rect(134, Screen.height - 28, Mathf.Max(0, Screen.width - 260), 22),
                Core.Localization.T("Wheel: zoom | Right drag: pan"));
            if (!DebugMode) return;
            GUI.Label(new Rect(16, Screen.height - 52, Screen.width - 200, 22),
                Core.Localization.F("Hover: {0} {1}", Hovered?.ToString() ?? "—", HoveredTerrain()));
            if (view.Visibility != null && GUI.Button(new Rect(Screen.width - 170, Screen.height - 52, 154, 22),
                Core.Localization.T(view.DebugVisibility ? "Vision debug ON" : "Vision debug OFF")))
                view.ToggleVisibilityDebug();
        }

        private string HoveredTerrain()
        {
            if (!Hovered.HasValue) return "";
            if (view.Visibility == null) return Core.Localization.T(grid.GetCell(Hovered.Value).Terrain.ToString());
            var state = view.Visibility.GetState(Hovered.Value);
            return view.Visibility.TryGetRememberedTerrain(Hovered.Value, out var terrain)
                ? Core.Localization.T(state.ToString()) + " / " + Core.Localization.T(terrain.ToString()) : Core.Localization.T(state.ToString());
        }
    }
}
