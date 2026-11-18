using System.Collections;
using System.Reflection;

namespace BrowserAdapters;

// A shared tmux terminal has one grid. A remote frame mismatch is not evidence
// that this client's resize was lost: another client may have resized it. Submit
// only new local geometry, reconnects, or new runs, not recurring mismatch retries.
public static class BrowserTerminalSize
{
    static readonly Dictionary<string, (int generation, long run, int cols, int rows)> requested =
        new();
    static Type? hubType;
    static object Hub =>
        (
            hubType ??= AppDomain
                .CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "SlopWorld")
                .GetType("SlopWorld.SessionHub")!
        )
            .GetField("Instance")!
            .GetValue(null)!;

    public static bool Allow(string name, int cols, int rows)
    {
        var hub = Hub;
        var info = hubType!.GetMethod("Get")!.Invoke(hub, new object[] { name });
        if (info == null)
            return false;
        var current = (
            (int)hubType.GetProperty("ConnectionGeneration")!.GetValue(hub)!,
            (long)info.GetType().GetField("RunId")!.GetValue(info)!,
            cols,
            rows
        );
        if (requested.TryGetValue(name, out var previous) && previous == current)
            return false;
        requested[name] = current;
        // Catalog deletion must release old names; the cache belongs to live metadata.
        var names = ((IEnumerable)hubType.GetProperty("Sessions")!.GetValue(hub)!)
            .Cast<object>()
            .Select(s => (string)s.GetType().GetField("Name")!.GetValue(s)!)
            .ToHashSet();
        foreach (var retired in requested.Keys.Where(n => !names.Contains(n)).ToArray())
            requested.Remove(retired);
        return true;
    }
}
