// Entry session rows share the worker icon; no session-specific appearance assets.
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserPortraits
{
    public static void Bind()
    {
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var icons = mod.GetType("SlopWorld.Icons", true)!;
        var agents = icons.GetProperty("Agents")!;
        mod.GetType("SlopWorld.SessionPortraits", true)!
            .GetField("Source")!
            .SetValue(null, new Func<string, Texture?>(_ => (Texture?)agents.GetValue(null)));
    }
}
