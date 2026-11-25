[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Adapters")]

namespace SlopWorld
{
    public static class ClientPlatform
    {
        internal static int BrowserRefreshRate { get; set; }
        public static bool IsBrowser { get; internal set; }
    }
}
