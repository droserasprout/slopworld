// Diagnostic switch for comparing glyph projection without changing native input.
using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static bool WorkspaceRowCommands(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureTerminalRows", enabled)!;

    [JSExport]
    public static string WorkspaceRowCommandsStats() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserGui", "TerminalRowStats")!;

    [JSExport]
    public static bool WorkspaceGlyphCache(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureGlyphCache", enabled)!;

    [JSExport]
    public static bool WorkspaceGlyphTailClip(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureGlyphTailClip", enabled)!;

    [JSExport]
    public static bool WorkspaceCellFastPath(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureCellFastPath", enabled)!;
}
