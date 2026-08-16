namespace SlopWorld
{
    // State that spans the colonist-bar back and front passes. Keeping the scroll group and
    // resize gesture together makes it explicit that neither belongs to row layout.
    sealed class SidebarInteraction
    {
        public readonly SmoothScroll AgentScroll = new SmoothScroll();
        public bool AgentScrollOpen;
        public bool Resizing;
        public float Grab;

        public void BeginFrame()
        {
            AgentScrollOpen = false;
        }
    }
}
