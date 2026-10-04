using Verse;

namespace SlopWorld
{
    // Workspace and map chrome share scene and screenshot visibility policy.
    public static class WorkspaceVisibility
    {
        public static bool Shown => !Cutscene.Playing;
        public static bool Hidden => Find.ScreenshotModeHandler?.FiltersCurrentEvent ?? false;
    }
}
