namespace SlopWorld
{
    public enum SidebarTab
    {
        Agents,
        Files,
        Search,
        Git,
        Library,
        Tasks,
    }

    public readonly struct SidebarTabActionContext
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public SidebarTabActionContext(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public SidebarTabActionContext ShiftX(float amount) =>
            new SidebarTabActionContext(X + amount, Y, Width, Height);
    }
}
