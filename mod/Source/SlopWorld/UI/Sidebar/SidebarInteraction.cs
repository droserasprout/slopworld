namespace SlopWorld
{
    // State that spans the colonist-bar back and front passes. Keeping the scroll group and
    // resize gesture together makes it explicit that neither belongs to row layout.
    sealed class SidebarInteraction
    {
        public readonly SmoothScroll AgentScroll = new SmoothScroll();
        public readonly SmoothScroll FilesRoutedScroll = new SmoothScroll();
        public bool AgentScrollOpen;
        public bool Resizing;
        public int ResizeControl;
        public bool WidthChanged;
        public float Grab;
        public bool FilesDividerDragging;
        public int FilesDividerControl;
        public bool FilesDividerChanged;
        public bool FilesDividerInput;

        public void BeginFrame()
        {
            AgentScrollOpen = false;
            FilesDividerInput = false;
        }
    }
}
