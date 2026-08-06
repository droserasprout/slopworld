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

        // Screenshot mode, F11. Vanilla filters its own chrome on this between the map
        // interface and the window stack, and the sidebar rides ColonistBarOnGUI so it is
        // already behind that gate. The top bar is drawn from a MapComponent and the Edit
        // button off the inspect pane's ExtraOnGUI, both of which run *before* it, so each
        // has to ask. Not folded into Shown: the insets are a layout answer and moving them
        // for a hidden interface would shuffle everything on the frame the key is pressed.
        public static bool Hidden => Find.ScreenshotModeHandler?.FiltersCurrentEvent ?? false;
    }
}
