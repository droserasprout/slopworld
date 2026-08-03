using Verse;

namespace SlopWorld
{
    public static class SlopLayout
    {
        // A scene has the board and everything else stands down, so the room goes back
        // rather than leaving a button row indented against nothing.
        public static bool Shown => !Cutscene.Playing;

        public static float LeftInset => Shown ? AgentSidebar.Width : 0f;

        public static float TopInset => Shown ? TopBar.H : 0f;
    }
}
