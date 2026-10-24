namespace SlopWorld
{
    // Shared pure geometry value. Callers convert it to Unity Rect only while drawing.
    public readonly struct UiLayoutRect
    {
        public readonly float X,
            Y,
            Width,
            Height;

        public UiLayoutRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = NonNegative(width);
            Height = NonNegative(height);
        }

        public float XMax => X + Width;
        public float YMax => Y + Height;

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }
}
