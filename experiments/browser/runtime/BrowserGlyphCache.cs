// Diagnostic switch for comparing glyph projection without changing native input.
using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static bool WorkspaceGlyphCache(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureGlyphCache", enabled)!;

    [JSExport]
    public static bool WorkspaceImageVersionCache(bool enabled) =>
        (bool)
            Invoke(
                "Adapters",
                "BrowserAdapters.BrowserGui",
                "ConfigureImageVersionCache",
                enabled
            )!;

    [JSExport]
    public static bool WorkspaceCellFastPath(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureCellFastPath", enabled)!;
}
