using GuildTactics.Expeditions;
using GuildTactics.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    /// <summary>Original code-native destination vignettes, not previews of a generated map.</summary>
    public sealed class ExpeditionIllustration : VisualElement
    {
        private readonly DungeonBiome biome;
        private readonly MissionType mission;
        public ExpeditionIllustration(DungeonBiome biome, MissionType mission)
        {
            this.biome = biome; this.mission = mission;
            AddToClassList("travel-illustration"); pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float scale = Mathf.Min(contentRect.width / 160, contentRect.height / 60);
            Vector2 origin = contentRect.center - new Vector2(80, 30) * scale;
            Vector2 V(float x, float y) => origin + new Vector2(x, y) * scale;
            void Shape(Color fill, params Vector2[] points)
            {
                p.fillColor = fill; p.strokeColor = new Color(.68f, .51f, .29f); p.lineWidth = 1.4f * scale;
                p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
                p.ClosePath(); p.Fill(); p.Stroke();
            }
            var stone = new Color(.28f, .30f, .32f);
            var dark = new Color(.06f, .07f, .08f);
            Shape(stone, V(42, 56), V(42, 18), V(58, 3), V(102, 3), V(118, 18), V(118, 56));
            Shape(dark, V(62, 56), V(62, 25), V(72, 14), V(88, 14), V(98, 25), V(98, 56));
            Shape(stone, V(29, 56), V(29, 25), V(37, 25), V(37, 56));
            Shape(stone, V(123, 56), V(123, 25), V(131, 25), V(131, 56));
            if (biome == DungeonBiome.FloodedCellar)
            {
                Shape(new Color(.10f, .28f, .34f), V(8, 46), V(152, 46), V(158, 58), V(2, 58));
                for (int i = 0; i < 4; i++) Shape(new Color(.34f, .23f, .13f), V(70, 36 + i * 6), V(90, 36 + i * 6), V(93, 40 + i * 6), V(67, 40 + i * 6));
            }
            else if (mission == MissionType.RecoverRelic)
                Shape(new Color(.87f, .61f, .20f), V(80, 25), V(87, 35), V(80, 47), V(73, 35));
            else if (mission == MissionType.EliminateTarget)
            {
                p.strokeColor = new Color(.85f, .35f, .25f); p.lineWidth = 2 * scale;
                p.BeginPath(); p.Arc(V(80, 35), 10 * scale, 0, 360); p.Stroke();
                p.BeginPath(); p.MoveTo(V(80, 20)); p.LineTo(V(80, 50)); p.MoveTo(V(65, 35)); p.LineTo(V(95, 35)); p.Stroke();
            }
            else
                Shape(new Color(.62f, .29f, .20f), V(69, 27), V(91, 27), V(89, 42), V(80, 50), V(71, 42));
        }
    }
}
