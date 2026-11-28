using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;

public partial class Probe
{
    private static bool coreInstalled;
    private static bool entryStarted;

    private const string Languages = "/Data/Core/Languages/";
    private static readonly CoreLanguagePacks languagePacks = new(Languages);
    private static Action<string>? switchLanguage;

    [JSExport]
    public static string ConfigureCoreLanguage(string folder)
    {
        return languagePacks.Choose(folder, Runtime.SystemLanguageFolder());
    }

    [JSExport]
    public static void EnableLanguageMenu(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> select
    ) => switchLanguage = select;

    [JSExport]
    public static void InstallCoreLanguage(byte[] archive) => languagePacks.Install(archive);

    public static string StartupLanguage(string nativeDefault) =>
        languagePacks.StartupLanguage(nativeDefault);

    public static bool SwitchLanguage(string folder)
    {
        if (switchLanguage == null)
            return false;
        switchLanguage(folder);
        return true;
    }

    [JSExport]
    public static int InstallCore(byte[] archive)
    {
        if (coreInstalled)
            throw new InvalidOperationException("Core already installed");
        using var zip = new ZipArchive(new MemoryStream(archive));
        foreach (var entry in zip.Entries)
        {
            var path = Path.GetFullPath("/" + entry.FullName);
            if (entry.Name.Length == 0 && path.StartsWith(Languages, StringComparison.Ordinal))
            {
                languagePacks.Register(path[Languages.Length..].TrimEnd('/'));
                continue;
            }
            if (
                !(
                    path.StartsWith("/Data/Core/", StringComparison.Ordinal)
                    || path.StartsWith("/Rendering/Shaders/", StringComparison.Ordinal)
                ) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidOperationException("Only Core data files are supported");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = File.Create(path);
            input.CopyTo(output);
        }
        Directory.CreateDirectory("/RimWorld_Data");
        Directory.CreateDirectory("/Mods");
        coreInstalled = true;
        return zip.Entries.Count;
    }

    public static void RequireCoreOnly()
    {
        if (
            !coreInstalled
            || Directory.GetDirectories("/Data").Any(p => p != "/Data/Core")
            || Directory.EnumerateFileSystemEntries("/Mods").Any()
            || Directory.EnumerateFiles("/Data/Core", "*.dll", SearchOption.AllDirectories).Any()
        )
            throw new NotSupportedException(
                "This bootstrap supports only Core data; custom CLR injection is unavailable"
            );
    }

    public static void RejectModAssemblies(IReadOnlyList<Assembly> assemblies)
    {
        RequireCoreOnly();
        if (assemblies.Count != 0)
            throw new NotSupportedException("Mod assembly injection is unavailable");
    }

    [JSExport]
    public static void StartEntry()
    {
        if (entryStarted)
            throw new InvalidOperationException("Start a fresh runtime for another Entry scene");
        entryStarted = true;
        Environment.SetEnvironmentVariable("WAYLITH_NO_ASSET_PREFETCH", "1");
        RequireCoreOnly();
        Runtime.StartEntryEngine();
    }

    [JSExport]
    public static bool EntryFrame(int frame) => Runtime.UpdateEntryEngine(frame);

    [JSExport]
    public static string EntryReport() => Runtime.EntryReport();

    [JSExport]
    public static string GameSnapshot() => Runtime.GameSnapshot();

    [JSExport]
    public static void StopEntry()
    {
        switchLanguage = null;
        if (entryStarted)
            Runtime.Dispose();
    }
}
