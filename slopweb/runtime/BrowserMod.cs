using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static string InitializeEco()
    {
        Results.Clear();
        Check("eco.mod-entry", () => Runtime.InitializeEco());
        return JsonSerializer.Serialize(new { checks = Results, fullBootstrap = false });
    }
}
