using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    /// <summary>Small code-native symbols for the existing actions; independent of font glyphs.</summary>
    public sealed class ActionIcon : VisualElement
    {
        private readonly string id;
        public ActionIcon(string actionId)
        {
            id = actionId; pickingMode = PickingMode.Ignore; AddToClassList("action-icon");
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float scale = Mathf.Min(contentRect.width, contentRect.height) / 24f;
            Vector2 V(float x, float y) => contentRect.center + new Vector2(x - 12, y - 12) * scale;
            p.strokeColor = new Color(.96f, .79f, .49f); p.lineWidth = 1.8f * scale;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.Stroke(); }
            void Circle(float radius) { p.BeginPath(); p.Arc(V(12, 12), radius * scale, 0, 360); p.Stroke(); }
            switch (id)
            {
                case "move": Line(V(3, 18), V(9, 18), V(9, 10), V(20, 10)); Line(V(15, 5), V(20, 10), V(15, 15)); break;
                case "push": Line(V(3, 8), V(12, 8), V(12, 4), V(21, 12), V(12, 20), V(12, 16), V(3, 16)); break;
                case "evade": Line(V(5, 4), V(19, 4), V(19, 13), V(12, 21), V(5, 13), V(5, 4)); break;
                case "aimed-shot": Circle(8); Line(V(12, 1), V(12, 8)); Line(V(12, 16), V(12, 23)); Line(V(1, 12), V(8, 12)); Line(V(16, 12), V(23, 12)); break;
                case "trap": Line(V(2, 18), V(22, 18)); Line(V(4, 18), V(7, 8), V(11, 18), V(15, 8), V(20, 18)); break;
                case "fire-burst": Line(V(12, 2), V(18, 10), V(20, 7), V(21, 16), V(16, 22), V(7, 22), V(3, 16), V(8, 8), V(9, 13), V(12, 2)); break;
                case "blink": Circle(7); Line(V(2, 18), V(22, 6)); Line(V(17, 5), V(22, 6), V(21, 11)); break;
                case "potion": Line(V(9, 2), V(15, 2), V(15, 8), V(20, 14), V(20, 21), V(4, 21), V(4, 14), V(9, 8), V(9, 2)); Line(V(7, 15), V(17, 15)); break;
                case "wait": Circle(9); Line(V(12, 5), V(12, 12), V(17, 15)); break;
                default:
                    Line(V(4, 21), V(19, 4), V(21, 3), V(21, 7), V(7, 22)); Line(V(3, 13), V(12, 21));
                    if (id == "heavy-strike") Line(V(2, 3), V(6, 7), V(2, 9));
                    if (id == "backstab") Line(V(3, 4), V(3, 10), V(9, 10));
                    break;
            }
        }
    }
}
