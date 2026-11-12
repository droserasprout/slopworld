using BrowserAdapters;
using SlopWorld;

// Reflection targets for the real startup export. Settings and validation use
// production sources; only the game-loaded assembly lookup is substituted.
public static class AppearanceSettings
{
    public static ModSettings S { get; set; } = new();
}

public partial class Probe
{
    static readonly object entryEngine = new();
    public static string HostHome = "";

    static Type Type(string assembly, string name) =>
        name == "SlopWorld.Settings"
            ? typeof(AppearanceSettings)
            : throw new InvalidOperationException("Unexpected appearance type: " + name);

    static object? Invoke(string assembly, string owner, string method, params object[] args)
    {
        if (owner == "BrowserAdapters.BrowserPreferences" && method == "Apply")
            BrowserPreferences.Apply(args[0], (string)args[1]);
        else if (owner == "BrowserAdapters.BrowserSidebar" && method == "ConfigureHome")
            HostHome = (string)args[0];
        else
            throw new InvalidOperationException(
                "Unexpected appearance dispatch: " + owner + "." + method
            );
        return null;
    }
}
