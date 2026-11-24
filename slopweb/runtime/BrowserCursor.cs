using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static string WorkspaceCursorTake() => Runtime.WorkspaceCursorTake();
}
