using Verse;

namespace SlopWorld
{
    public abstract partial class SlopLayout
    {
        // A scene has the board and everything else stands down, so the room goes back
        // rather than leaving a button row indented against nothing.
        public static bool Shown => !Cutscene.Playing;

        public static float LeftInset => Shown && !Settings.SidebarHidden ? AgentSidebar.Width : 0f;

        public static float TopInset => Shown ? TopBar.H : 0f;

        // Screenshot mode filters vanilla chrome separately; the top bar and inspect controls run before that filter, so Hidden is independent of layout insets.
        public static bool Hidden => Find.ScreenshotModeHandler?.FiltersCurrentEvent ?? false;
    }
}
