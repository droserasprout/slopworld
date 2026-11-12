using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    private static IDisposable? entryEngine;
    private static bool coreInstalled;
    private static bool entryStarted;

    [JSExport]
    public static int InstallCore(byte[] archive)
    {
        if (coreInstalled)
            throw new InvalidOperationException("Core already installed");
        using var zip = new ZipArchive(new MemoryStream(archive));
        foreach (var entry in zip.Entries)
        {
            var path = Path.GetFullPath("/" + entry.FullName);
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
    public static string StartEntry()
    {
        if (entryStarted)
            throw new InvalidOperationException("Start a fresh runtime for another Entry scene");
        entryStarted = true;
        // Use the engine's own on-demand asset path; prefetch starts native threads.
        Environment.SetEnvironmentVariable("WAYLITH_NO_ASSET_PREFETCH", "1");
        Console.WriteLine("Entry: starting real bootstrap");
        Results.Clear();
        Check(
            "entry.bootstrap",
            () =>
            {
                Invoke("Adapters", "BrowserAdapters.RuntimeServices", "ConfigureLogging");
                RequireCoreOnly();
                var options = Activator.CreateInstance(
                    Type("Waylith", "Waylith.EngineApplicationOptions")
                )!;
                options.GetType().GetProperty("Headless")!.SetValue(options, true);
                options
                    .GetType()
                    .GetProperty("AudioBackend")!
                    .SetValue(
                        options,
                        Activator.CreateInstance(Type("Adapters", "BrowserAdapters.SilentAudio"))
                    );
                entryEngine = (IDisposable)
                    Activator.CreateInstance(
                        Type("Waylith", "Waylith.EngineApplication"),
                        options
                    )!;
                Invoke("RimWorld", "RimWorldWaylith.Bootstrap", "Initialize", entryEngine);
                return "initialized";
            }
        );
        Console.WriteLine("Entry: bootstrap returned");
        return EntryReport();
    }

    [JSExport]
    public static string EntryFrame(int frame)
    {
        bool trace = frame < 15 || frame % 60 == 14;
        if (trace)
            Console.WriteLine("Entry: starting frame " + frame);
        Check(
            "entry.frame." + frame,
            () =>
            {
                if (entryEngine == null)
                    throw new InvalidOperationException("Entry is stopped");
                InputFrame("[]", 240);
                DrawCall("ClearFrameSubmissions");
                Invoke("Waylith", "Waylith.EngineApplication", "BeginFrame", 1f / 60, frame / 60f);
                try
                {
                    (
                        (Action?)entryEngine.GetType().GetField("OnUpdate")!.GetValue(entryEngine)
                    )?.Invoke();
                }
                finally
                {
                    Invoke("Waylith", "Waylith.EngineApplication", "EndFrame");
                }
                return "updated";
            }
        );
        if (trace)
            Console.WriteLine("Entry: finished frame " + frame);
        return EntryReport();
    }

    private static string EntryReport()
    {
        var runtime = Type("UnityStubs", "Waylith.UnityCompat.UnityRuntime")
            .GetProperty("Current")!
            .GetValue(null);
        var world = runtime == null ? null : Get(runtime, "World");
        var loaded = (bool)
            Type("Assembly-CSharp", "Verse.PlayDataLoader").GetProperty("Loaded")!.GetValue(null)!;
        string? humanBody = null;
        if (loaded)
        {
            var database = Type("Assembly-CSharp", "Verse.DefDatabase`1")
                .MakeGenericType(Type("Assembly-CSharp", "Verse.ThingDef"));
            var human = database
                .GetMethod("GetNamed")!
                .Invoke(null, new object[] { "Human", true })!;
            var race = human.GetType().GetField("race")!.GetValue(human)!;
            var body = race.GetType().GetField("body")!.GetValue(race)!;
            humanBody = body.GetType().GetField("defName")!.GetValue(body)!.ToString();
        }
        var messages = (
            (IEnumerable)
                Type("Assembly-CSharp", "Verse.Log").GetProperty("Messages")!.GetValue(null)!
        )
            .Cast<object>()
            .Select(m => m.GetType().GetField("text")!.GetValue(m)?.ToString())
            .ToArray();
        return JsonSerializer.Serialize(
            new
            {
                checks = Results,
                messages,
                world = world == null ? null : InstanceCall(world, "GetDiagnosticsSnapshot"),
                engineErrors = Type("Adapters", "BrowserAdapters.RuntimeServices")
                    .GetProperty("Errors")!
                    .GetValue(null),
                engineErrorCount = Type("Adapters", "BrowserAdapters.RuntimeServices")
                    .GetProperty("ErrorCount")!
                    .GetValue(null),
                omittedErrorCount = Type("Adapters", "BrowserAdapters.RuntimeServices")
                    .GetProperty("OmittedErrorCount")!
                    .GetValue(null),
                unityFrame = runtime == null ? null : Get(runtime, "FrameCount"),
                loaded,
                humanBody,
                activeScene = Get(
                    Invoke(
                        "UnityStubs",
                        "UnityEngine.SceneManagement.SceneManager",
                        "GetActiveScene"
                    )!,
                    "name"
                ),
                definitions = new[]
                {
                    "Verse.ThingDef",
                    "Verse.TerrainDef",
                    "Verse.RecipeDef",
                }.ToDictionary(
                    name => name,
                    name =>
                        Type("Assembly-CSharp", "Verse.DefDatabase`1")
                            .MakeGenericType(Type("Assembly-CSharp", name))
                            .GetProperty("DefCount")!
                            .GetValue(null)
                ),
                customClrInjection = false,
                rendered = false,
                colony = false,
            }
        );
    }

    [JSExport]
    public static string GameSnapshot() =>
        (string)Invoke("Adapters", "BrowserAdapters.WorkspaceScene", "Snapshot")!;

    [JSExport]
    public static void StopEntry()
    {
        var engine = entryEngine;
        entryEngine = null;
        if (engine == null)
            return;
        try
        {
            var runtime = Type("UnityStubs", "Waylith.UnityCompat.UnityRuntime")
                .GetProperty("Current")!
                .GetValue(null);
            if (runtime != null)
                InstanceCall(Get(runtime, "World"), "Shutdown");
        }
        finally
        {
            engine.Dispose();
        }
    }
}
