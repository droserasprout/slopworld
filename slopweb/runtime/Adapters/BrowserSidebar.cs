using SlopWorld;
using UnityEngine;

namespace BrowserAdapters;

// Entry-only sidebar dispatch. The mod still owns row grouping, rendering, menus,
// selection and filters; no pawn identities or colony are synthesized.
public static class BrowserSidebar
{
    static string hostHome = "";

    // Read native UI-space geometry for diagnostic pointer targeting. Layout and
    // hit testing remain in Sidebar, including side and open-files preferences.
    public static object Snapshot()
    {
        static float[] Box(Rect rect) => [rect.x, rect.y, rect.width, rect.height];
        return new
        {
            panel = Box(Sidebar.Panel),
            body = Box(Sidebar.Body),
            add = Box(Sidebar.AddBar),
        };
    }

    public static void ConfigureHome(string home)
    {
        if (home.Length > 4096 || !home.StartsWith('/') || home.Contains('\0'))
            throw new ArgumentException("Invalid host display home directory", nameof(home));
        hostHome = home;
    }

    // Only the native sidebar's cwd-label formatter calls this boundary. It
    // conveys no browser filesystem access and does not change process HOME.
    public static string HomeFolder(Environment.SpecialFolder folder) =>
        folder == Environment.SpecialFolder.UserProfile
            ? hostHome
            : throw new InvalidOperationException("Unexpected sidebar folder request");

    public static void Draw(bool interactive) => ColonistBarStrip.Draw(interactive);
}
