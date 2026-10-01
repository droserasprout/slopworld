namespace SlopWorld
{
    // Project editor labels are independent of the daemon's mount wire names.
    public static class MountPresentation
    {
        public static string ModeLabel(MountMode mode) =>
            mode == MountMode.Cache ? "Cache" : mode == MountMode.None ? "None" : mode == MountMode.Ro ? "Read-only" : "Read-write";
    }
}
