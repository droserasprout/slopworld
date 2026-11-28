using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static void InputFrame(string batch, int height) => Runtime.InputFrame(batch, height);
}
