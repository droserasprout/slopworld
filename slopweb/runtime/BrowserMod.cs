using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    [JSExport]
    public static bool InitializeEco() => Runtime.InitializeEco();
}
