using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static string WorkspaceCursorTake() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserCursor", "Take")!;
}
