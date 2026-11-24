namespace SlopWorld
{
    // Read-only observations used by the workspace profiler.
    sealed partial class TerminalPanel
    {
        internal static object GeometrySnapshot(TerminalPanel panel) =>
            new
            {
                bounds = panel == null
                    ? null
                    : new[]
                    {
                        panel.Bounds.X,
                        panel.Bounds.Y,
                        panel.Bounds.Width,
                        panel.Bounds.Height,
                    },
                cellHeight = panel == null ? (float?)null : TerminalFont.CellH,
                rows = panel?._state.Rows,
                cols = panel?._state.Cols,
            };

        internal static object ScrollSnapshot(TerminalPanel panel) =>
            new
            {
                offset = panel?._scrollOff,
                pixels = panel?._historyLastPixels,
                maximumPixels = panel?._historyMax,
                wanted = panel?._wantedScrollOff,
                sent = panel?._sentScrollOff,
                pending = panel?._scrollPending,
                requests = panel?._historyRequests.Count,
                viewReady = panel?._historyViewReady,
            };

        internal static object CacheSnapshot(TerminalPanel panel, string outsideTargetTrace) =>
            new
            {
                disabled = panel?._noCache,
                exists = panel?._cache != null,
                created = panel?._cache != null && panel._cache.IsCreated(),
                session = panel?._cacheKey.Session,
                revision = panel?._cacheContentRevision,
                outsideTargetTrace,
            };
    }
}
