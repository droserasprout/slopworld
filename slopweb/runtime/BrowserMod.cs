using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static string InitializeEco()
    {
        Results.Clear();
        Check(
            "eco.mod-entry",
            () =>
            {
                var type = Type("SlopWorld", "SlopWorld.ModEntry");
                var content = Invoke("Adapters", "BrowserAdapters.BrowserModContent", "Current");
                var entry = Activator.CreateInstance(type, new object?[] { content })!;
                var settings = type.GetField("settings")!.GetValue(entry)!;
                settings.GetType().GetField("ecoMode")!.SetValue(settings, true);
                return Type("SlopWorld", "SlopWorld.Eco").GetProperty("Resting")!.GetValue(null)!;
            }
        );
        return JsonSerializer.Serialize(new { checks = Results, fullBootstrap = false });
    }
}
