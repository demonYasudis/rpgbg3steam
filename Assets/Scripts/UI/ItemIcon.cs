using GuildTactics.Expeditions;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    /// <summary>Original code-native item illustrations; no font glyphs or generated textures.</summary>
    public sealed class ItemIcon : VisualElement
    {
        private readonly ItemCategory category;
        public ItemIcon(ItemCategory category)
        {
            this.category = category; AddToClassList("item-icon");
            pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float scale = Mathf.Min(contentRect.width, contentRect.height) / 64f;
            Vector2 origin = contentRect.center - Vector2.one * 32 * scale;
            Vector2 V(float x, float y) => origin + new Vector2(x, y) * scale;
            void Shape(Color fill, params Vector2[] points)
            {
                p.fillColor = fill; p.strokeColor = new Color(.92f, .73f, .43f); p.lineWidth = 1.3f * scale;
                p.BeginPath(); p.MoveTo(points[0]);
                for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
                p.ClosePath(); p.Fill(); p.Stroke();
            }
            var metal = new Color(.49f, .59f, .65f);
            var gold = new Color(.65f, .41f, .18f);
            if (category == ItemCategory.Weapon)
            {
                Shape(metal, V(46, 5), V(56, 6), V(54, 17), V(28, 43), V(21, 36));
                Shape(new Color(.77f, .85f, .86f), V(46, 5), V(50, 10), V(24, 40), V(21, 36));
                Shape(gold, V(16, 29), V(35, 48), V(31, 52), V(12, 33));
                Shape(new Color(.28f, .15f, .10f), V(17, 40), V(23, 46), V(13, 57), V(7, 51));
                Shape(gold, V(5, 50), V(14, 59), V(9, 62), V(2, 55));
            }
            else if (category == ItemCategory.Armor)
            {
                Shape(metal, V(20, 8), V(26, 14), V(38, 14), V(44, 8), V(58, 20), V(50, 31), V(44, 27), V(47, 55), V(17, 55), V(20, 27), V(14, 31), V(6, 20));
                Shape(new Color(.29f, .37f, .42f), V(22, 22), V(42, 22), V(41, 48), V(32, 54), V(23, 48));
                Shape(gold, V(19, 39), V(45, 39), V(45, 45), V(19, 45));
                Shape(new Color(.91f, .74f, .41f), V(29, 38), V(35, 38), V(35, 46), V(29, 46));
            }
            else
            {
                Shape(new Color(.23f, .33f, .33f), V(24, 13), V(40, 13), V(40, 26), V(50, 37), V(50, 50), V(42, 59), V(22, 59), V(14, 50), V(14, 37), V(24, 26));
                Shape(new Color(.65f, .10f, .12f), V(18, 36), V(46, 36), V(46, 49), V(39, 55), V(25, 55), V(18, 49));
                Shape(gold, V(22, 5), V(42, 5), V(42, 14), V(22, 14));
                Shape(new Color(1f, .78f, .62f), V(23, 30), V(26, 29), V(26, 46), V(23, 44));
            }
        }
    }
}
