using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Loader;
using System.Runtime.Versioning;

[SupportedOSPlatform("browser")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1050",
    Justification = "The global Probe name is the managed export and offline bootstrap ABI."
)]
public partial class Probe
{
    public static void Main() { }

    private static readonly List<object> Results = new();

    [JSExport]
    public static string LoadAssembly(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return AssemblyLoadContext.Default.LoadFromStream(stream).FullName!;
    }

    [JSExport]
    public static string LoadPublishedAssembly(string name) => Assembly.Load(name).FullName!;

    // Bootstrap compatibility calls run while private engine assemblies arrive.
    // Frame and input dispatch live in the late-loaded workspace runtime.
    private static Type Type(string assembly, string type) =>
        Assembly.Load(assembly).GetType(type, true)!;

    private static void Check(string name, Func<object?> operation)
    {
        try
        {
            Results.Add(
                new
                {
                    name,
                    ok = true,
                    value = operation()?.ToString(),
                }
            );
        }
        catch (Exception error)
        {
            var chain = new List<string>();
            for (Exception? current = error; current != null; current = current.InnerException)
                chain.Add($"{current.GetType().FullName}: {current.Message}");
            Results.Add(
                new
                {
                    name,
                    ok = false,
                    errors = chain,
                    stack = error.ToString(),
                }
            );
        }
    }
}
