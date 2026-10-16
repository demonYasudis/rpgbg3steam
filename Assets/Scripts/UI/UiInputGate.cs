using UnityEngine;

namespace GuildTactics.UI
{
    /// <summary>Scene-local presentation input ownership; never changes combat or saved state.</summary>
    public sealed class UiInputGate : MonoBehaviour
    {
        public bool PreviewOpen { get; internal set; }
        public bool ModalOpen { get; internal set; }
        private int releasedFrame = -1;
        public bool BlocksGameplay => PreviewOpen || ModalOpen || Time.frameCount == releasedFrame;
        internal void ConsumeClosingFrame() => releasedFrame = Time.frameCount;

        public static bool Blocks(Component component)
        {
            var gate = component.GetComponentInParent<UiInputGate>();
            return gate != null && gate.BlocksGameplay;
        }
    }
}
