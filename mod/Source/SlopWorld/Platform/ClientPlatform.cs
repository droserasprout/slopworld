namespace SlopWorld
{
    // Client presentation capabilities, independent of the daemon's host/sidecar runtime.
    // Browser preparation replaces this getter in its isolated mod assembly.
    public static class ClientPlatform
    {
        public static bool IsBrowser => false;
    }
}
