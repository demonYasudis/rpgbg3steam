using System.Collections.Generic;
using GuildTactics.Meta;
using GuildTactics.Units;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    public sealed partial class GuildPartyScreen
    {
        public const string ReducedMotionPreferenceKey = "GuildTactics.ReducedMotion";
        private static readonly ProfilerMarker MotionMarker = new ProfilerMarker("GuildTactics.GuildMotion");
        private readonly List<UnitMiniature> miniatures = new List<UnitMiniature>();
        private readonly List<VisualElement> movingPortraits = new List<VisualElement>();
        private Candlelight candlelight;
        private bool reducedMotion;
        private float motionTime, transitionStarted;
        private string reactionHeroId;
        private float reactionStarted;
        public bool ReducedMotion => reducedMotion;
        internal float MotionTime => motionTime;
        internal int MotionElementCount => miniatures.Count + movingPortraits.Count + (candlelight == null ? 0 : 1);

        public void SetReducedMotion(bool value, bool persist = true)
        {
            reducedMotion = value;
            shell?.Q<Toggle>("reduce-motion")?.SetValueWithoutNotify(value);
            if (value) reactionHeroId = null;
            if (persist && !Application.isBatchMode)
            { PlayerPrefs.SetInt(ReducedMotionPreferenceKey, value ? 1 : 0); PlayerPrefs.Save(); }
            ApplyMenuMotion();
        }

        private void BuildMotionOptions(VisualElement parent)
        {
            var toggle = new Toggle(Text("Reduced motion", "Меньше движения")) { name = "reduce-motion", value = reducedMotion };
            toggle.AddToClassList("motion-option");
            toggle.RegisterValueChangedCallback(evt => SetReducedMotion(evt.newValue)); parent.Add(toggle);
        }

        private void ClearMotionElements()
        {
            miniatures.Clear(); movingPortraits.Clear(); candlelight = null;
        }

        private void BuildCandlelight()
        {
            candlelight = new Candlelight(); shell.Add(candlelight);
        }

        private void MakeMovingPortrait(VisualElement viewport)
        {
            viewport.AddToClassList("motion-portrait-window");
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("motion-portrait-art"); art.style.backgroundImage = viewport.style.backgroundImage;
            viewport.style.backgroundImage = StyleKeyword.None;
            viewport.Add(art); movingPortraits.Add(art);
        }

        private UnitMiniature Miniature(GuildAdventurer hero)
        {
            if (!sheets.TryGetValue(hero.Definition.Id, out var sheet))
            { sheet = UnitSpriteSheet.Load(hero.Definition.Id); sheets.Add(hero.Definition.Id, sheet); }
            var image = new UnitMiniature(sheet) { name = "miniature-" + hero.Id, userData = hero.Id };
            image.AddToClassList("party-miniature"); miniatures.Add(image);
            return image;
        }

        private void UpdateMenuMotion()
        {
            // No scheduler or event subscription survives a rebuild/hide. Covered dialogs pause too.
            if (!IsOpen || dialog != GuildDialog.None) return;
            using (MotionMarker.Auto())
            {
                if (reducedMotion) return;
                motionTime += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                ApplyMenuMotion();
            }
        }

        private void ApplyMenuMotion()
        {
            foreach (var image in miniatures)
                image.Render(motionTime, reducedMotion, (string)image.userData == reactionHeroId ? motionTime - reactionStarted : float.PositiveInfinity);
            for (int i = 0; i < movingPortraits.Count; i++)
            {
                var art = movingPortraits[i];
                float breath = reducedMotion ? 0 : (1 + Mathf.Sin(motionTime * 0.75f + i * 0.8f)) * 0.003f;
                art.style.scale = new Scale(Vector3.one * (1 + breath));
                art.style.translate = new Translate(0, reducedMotion ? 0 : Mathf.Sin(motionTime * 0.6f + i) * 0.7f, 0);
            }
            if (candlelight != null)
                candlelight.style.opacity = reducedMotion ? 0 : 0.65f + 0.08f * Mathf.Sin(motionTime * 2.1f) + 0.04f * Mathf.Sin(motionTime * 5.3f);
            // Fade only; text, hit areas and focus positions never move.
            if (content != null) content.style.opacity = reducedMotion ? 1 : Mathf.Lerp(0.94f, 1, Mathf.Clamp01((motionTime - transitionStarted) / 0.18f));
        }

        private sealed class Candlelight : VisualElement
        {
            public Candlelight()
            {
                name = "guild-candlelight"; pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = style.top = style.right = style.bottom = 0;
                generateVisualContent += Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                for (int light = 0; light < 4; light++)
                {
                    var center = new Vector2(contentRect.width * (light == 0 ? 0.12f : light == 1 ? 0.3f : light == 2 ? 0.74f : 0.91f), contentRect.height * 0.19f);
                    for (int ring = 4; ring >= 1; ring--)
                    {
                        painter.fillColor = new Color(1, 0.55f, 0.16f, 0.012f);
                        painter.BeginPath(); painter.Arc(center, contentRect.height * 0.018f * ring, 0, 360); painter.Fill();
                    }
                }
            }
        }
    }
}
