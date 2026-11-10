using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[SupportedOSPlatform("browser")]
public partial class BrowserProbe
{
    public static void Main() { }

    [JSExport]
    public static string Run() => SlopWorld.BrowserCompat.RuntimeProbe.Run();

    [JSExport]
    public static Task<string> RunAsync(string url) =>
        SlopWorld.BrowserCompat.RuntimeProbe.RunAsync(url);

    [JSExport]
    public static string LoadMod(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return AssemblyLoadContext.Default.LoadFromStream(stream).FullName;
    }
}
