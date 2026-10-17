using GuildTactics.Units;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    /// <summary>A passive UI view of a borrowed atlas. The visible screen owns its clock and lifetime.</summary>
    public sealed class UnitMiniature : Image
    {
        private readonly UnitSpriteSheet sheet;
        public int FrameChanges { get; private set; }

        public UnitMiniature(UnitSpriteSheet sheet)
        {
            this.sheet = sheet;
            scaleMode = ScaleMode.ScaleToFit;
            pickingMode = PickingMode.Ignore;
            Render(0, true);
        }

        // Full atlas cells preserve their shared foot baseline and all weapon margins.
        // No per-frame cropping, duplicated frame tables, scheduling, gameplay state or RNG.
        public void Render(float idleElapsed, bool reducedMotion, float selectionElapsed = float.PositiveInfinity)
        {
            if (sheet == null) return;
            bool reaction = !reducedMotion && selectionElapsed >= 0 && selectionElapsed < sheet.Duration(UnitSpriteSheet.Pose.Defend);
            var frame = reaction ? sheet.Frame(UnitSpriteSheet.Pose.Defend, selectionElapsed, false) :
                sheet.Frame(UnitSpriteSheet.Pose.Idle, reducedMotion ? 0 : idleElapsed);
            if (sprite == frame) return;
            sprite = frame; FrameChanges++;
        }
    }
}
