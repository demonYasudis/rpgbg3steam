using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    public enum UiIconKind { Gold, Equipment, Expedition }

    /// <summary>Small original vector ornaments, independent of font glyph coverage.</summary>
    public sealed class UiIcon : VisualElement
    {
        private readonly UiIconKind kind;
        public UiIcon(UiIconKind kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            style.width = 24; style.height = 24;
            style.flexShrink = 0; style.marginRight = 7;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = new Color(0.95f, 0.75f, 0.4f);
            painter.fillColor = new Color(0.35f, 0.23f, 0.12f);
            painter.lineWidth = 1.5f;
            if (kind == UiIconKind.Gold)
            {
                painter.BeginPath();
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * Mathf.PI / 6;
                    var point = new Vector2(12 + Mathf.Cos(angle) * 10, 12 + Mathf.Sin(angle) * 10);
                    if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
                }
                painter.ClosePath(); painter.Fill(); painter.Stroke();
                Polygon(painter, new Vector2(12, 5), new Vector2(17, 12), new Vector2(12, 19), new Vector2(7, 12));
            }
            else if (kind == UiIconKind.Equipment)
                Polygon(painter, new Vector2(3, 3), new Vector2(21, 3), new Vector2(19, 15), new Vector2(12, 22), new Vector2(5, 15));
            else
            {
                Polygon(painter, new Vector2(12, 1), new Vector2(17, 9), new Vector2(23, 12), new Vector2(15, 16),
                    new Vector2(12, 23), new Vector2(8, 15), new Vector2(1, 12), new Vector2(9, 8));
                painter.BeginPath(); painter.MoveTo(new Vector2(12, 4)); painter.LineTo(new Vector2(12, 20));
                painter.MoveTo(new Vector2(4, 12)); painter.LineTo(new Vector2(20, 12)); painter.Stroke();
            }
        }

        private static void Polygon(Painter2D painter, params Vector2[] points)
        {
            painter.BeginPath(); painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath(); painter.Fill(); painter.Stroke();
        }
    }
}
