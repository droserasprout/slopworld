namespace SlopWorld
{
    // History offsets refer to physical rows in the daemon's current width. Reflow
    // cannot translate an arbitrary client anchor from history length alone. Keep
    // that grid while reading; the next live draw measures the latest pane bounds.
    internal static class TerminalViewportResize
    {
        internal static bool Deferred(int offset, bool jumpPending, int jumpOffset) =>
            offset > 0 || (jumpPending && jumpOffset > 0);
    }
}
