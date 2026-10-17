using UnityEngine;

namespace GuildTactics.UI
{
    /// <summary>Scene-local presentation input ownership; never changes combat or saved state.</summary>
    public sealed class UiInputGate : MonoBehaviour
    {
        public bool PreviewOpen { get; internal set; }
        public bool ModalOpen { get; internal set; }
        public bool GuildOpen { get; internal set; }
        public BattleHud Battle { get; internal set; }
        public bool JourneyOpen { get; internal set; }
        private int releasedFrame = -1;
        public bool BlocksGameplay => PreviewOpen || ModalOpen || GuildOpen || JourneyOpen || Time.frameCount == releasedFrame;
        internal void ConsumeClosingFrame() => releasedFrame = Time.frameCount;

        public static bool Blocks(Component component)
        {
            var gate = component.GetComponentInParent<UiInputGate>();
            return gate != null && gate.BlocksGameplay;
        }

        public static bool UsesBattleHud(Component component) => component.GetComponentInParent<UiInputGate>()?.Battle?.IsOpen == true;
        public static bool BlocksPointer(Component component, Vector2 screenPosition)
        {
            var gate = component.GetComponentInParent<UiInputGate>();
            return gate != null && (gate.BlocksGameplay || (gate.Battle?.IsOpen == true && gate.Battle.BlocksPointer(screenPosition)));
        }
    }
}
