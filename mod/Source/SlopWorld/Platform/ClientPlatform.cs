[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Adapters")]

namespace SlopWorld
{
    public static class ClientPlatform
    {
        public static bool IsBrowser { get; internal set; }
    }
}
