namespace SlopWorld
{
    static class Settings
    {
        internal static ModSettings S { get; set; } = new();
    }
}

namespace BrowserAdapters
{
    // Metadata fixture for GUI adaptation. Its second target is deliberately missing.
    static class BrowserClipboard { }

    static class BrowserSidebar
    {
        internal static string HostHome = "";

        internal static void ConfigureHome(string home) => HostHome = home;
    }
}
