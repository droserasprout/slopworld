using SlopWorld;

namespace BrowserAdapters;

// A shared tmux terminal has one grid. A remote frame mismatch is not evidence
// that this client's resize was lost: another client may have resized it. Submit
// only new local geometry, reconnects, or new runs, not recurring mismatch retries.
public static class BrowserTerminalSize
{
    static readonly Dictionary<string, (int generation, long run, int cols, int rows)> requested =
        new();

    public static bool Allow(string name, int cols, int rows)
    {
        var hub = SessionHub.Instance;
        var info = hub.Get(name);
        if (info == null)
            return false;
        var current = (hub.ConnectionGeneration, info.RunId, cols, rows);
        if (requested.TryGetValue(name, out var previous) && previous == current)
            return false;
        requested[name] = current;
        // Catalog deletion must release old names; the cache belongs to live metadata.
        var names = hub.Sessions.Select(session => session.Name).ToHashSet();
        foreach (var retired in requested.Keys.Where(n => !names.Contains(n)).ToArray())
            requested.Remove(retired);
        return true;
    }
}
