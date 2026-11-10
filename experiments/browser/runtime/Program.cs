using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;

[SupportedOSPlatform("browser")]
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

    // Geometry stays coupled to real game cell/value methods; the renderer owns drawing.
    [JSExport]
    public static string SceneGeometry()
    {
        var cellType = Type("Assembly-CSharp", "Verse.IntVec3");
        var cells = new List<object>();
        var vertices = new List<float>();
        for (int z = 0; z < 12; z++)
        for (int x = 0; x < 16; x++)
        {
            var cell = Activator.CreateInstance(cellType, x, 0, z)!;
            var cellX = (int)cellType.GetField("x")!.GetValue(cell)!;
            var cellZ = (int)cellType.GetField("z")!.GetValue(cell)!;
            var lengthSquared = (int)
                cellType.GetProperty("LengthHorizontalSquared")!.GetValue(cell)!;
            cells.Add(
                new
                {
                    x = cellX,
                    y = (int)cellType.GetField("y")!.GetValue(cell)!,
                    z = cellZ,
                    lengthSquared,
                    label = cell.ToString(),
                }
            );
            // Project real game X/Z cells into a small clip-space diagnostic grid.
            // This does not pretend to be Waylith's terrain mesh or a loaded colony.
            foreach (
                var (u, v) in new (float, float)[]
                {
                    (0, 0),
                    (1, 0),
                    (1, 1),
                    (0, 0),
                    (1, 1),
                    (0, 1),
                }
            )
                vertices.AddRange(
                    new float[]
                    {
                        -0.84f + (cellX + u * 0.94f) * 0.105f,
                        -0.78f + (cellZ + v * 0.94f) * 0.13f,
                        0,
                        0,
                        0,
                        1,
                        0.2f + (lengthSquared % 7) * 0.03f,
                        0.38f + ((cellX + cellZ) % 2) * 0.15f,
                        0.28f,
                        1,
                        u,
                        v,
                        0,
                        0,
                    }
                );
        }
        return JsonSerializer.Serialize(
            new
            {
                source = "Verse.IntVec3 from installed Assembly-CSharp.dll",
                cells,
                vertices,
            }
        );
    }

    private static Type Type(string assembly, string type) =>
        Assembly.Load(assembly).GetType(type, true)!;

    private static object? Invoke(string assembly, string type, string method, params object[] args)
    {
        var target = Type(assembly, type)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == method
                && m.GetParameters().Length == args.Length
                && m.GetParameters()
                    .Zip(args)
                    .All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))
            );
        return target.Invoke(null, args);
    }

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

    // Separate entry point keeps native initialization probes out of the combined demo.
    [JSExport]
    public static string Deep()
    {
        Results.Clear();
        Check(
            "engine.constructor",
            () =>
            {
                var options = Activator.CreateInstance(
                    Type("Waylith", "Waylith.EngineApplicationOptions")
                );
                using var engine = (IDisposable)
                    Activator.CreateInstance(
                        Type("Waylith", "Waylith.EngineApplication"),
                        options
                    )!;
                return "created";
            }
        );
        Check(
            "native.vulkan-initializer",
            () =>
            {
                RuntimeHelpers.RunClassConstructor(
                    Type("Brutal.Vulkan", "Brutal.VulkanApi.Vulkan").TypeHandle
                );
                return "initialized";
            }
        );
        Check(
            "native.audio-initialize",
            () =>
            {
                var type = Type("Waylith", "Waylith.Audio.MiniAudio.MiniAudioBackend");
                var backend = Activator.CreateInstance(type);
                return type.GetMethod("Initialize")!.Invoke(backend, new object[] { true });
            }
        );
        return JsonSerializer.Serialize(new { checks = Results });
    }

    [JSExport]
    public static string Run()
    {
        Results.Clear();
        Check(
            "game.positive-mod",
            () => Invoke("Assembly-CSharp", "Verse.GenMath", "PositiveMod", -7, 5)
        );
        Check(
            "game.cell-length",
            () =>
            {
                var t = Type("Assembly-CSharp", "Verse.IntVec3");
                var cell = Activator.CreateInstance(t, 3, 0, 4);
                return t.GetProperty("LengthHorizontalSquared")!.GetValue(cell);
            }
        );
        Check(
            "game.version",
            () =>
                Type("Assembly-CSharp", "RimWorld.VersionControl")
                    .GetProperty("CurrentVersionString")!
                    .GetValue(null)
        );
        Check("unity.math", () => Invoke("UnityStubs", "UnityEngine.Mathf", "Sqrt", 81f));
        Check("waylith.assembly", () => Assembly.Load("Waylith").FullName);
        Check("native.glfw-init", () => Invoke("Brutal.Glfw", "Brutal.GlfwApi.Glfw", "Init"));
        Check(
            "runtime.dynamic-method",
            () =>
            {
                var method = new DynamicMethod("probe", typeof(int), System.Type.EmptyTypes);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldc_I4, 42);
                il.Emit(OpCodes.Ret);
                return method.CreateDelegate<Func<int>>()();
            }
        );
        Check(
            "runtime.memory-map",
            () =>
            {
                using var map = MemoryMappedFile.CreateNew(null, 4096);
                return "created";
            }
        );
        // Intentionally exercise a platform-unsupported API to report its runtime failure.
#pragma warning disable CA1416
        Check(
            "runtime.thread-start",
            () =>
            {
                var thread = new Thread(() => { });
                thread.Start();
                return "started";
            }
        );
#pragma warning restore CA1416
        Check(
            "game.entrypoint",
            () =>
            {
                var assembly = Assembly.Load("RimWorld");
                var entry =
                    assembly.EntryPoint
                    ?? throw new MissingMethodException("RimWorld has no managed entry point");
                return entry.Invoke(
                    null,
                    entry.GetParameters().Length == 0
                        ? null
                        : new object[] { Array.Empty<string>() }
                );
            }
        );
        return JsonSerializer.Serialize(
            new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                browser = OperatingSystem.IsBrowser(),
                checks = Results,
            }
        );
    }
}
