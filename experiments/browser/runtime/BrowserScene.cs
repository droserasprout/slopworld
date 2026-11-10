using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    private static IDisposable? sceneEngine;
    private static object? sceneRuntime,
        sceneRenderer,
        sceneTransform,
        sceneMesh,
        sceneMaterial;
    private static double? previousSceneTime;
    private static double sceneStartTime;
    private static bool sceneStarted;

    private static object? InstanceCall(object owner, string method, params object[] args) =>
        owner
            .GetType()
            .GetMethods()
            .Single(m =>
                m.Name == method
                && !m.IsGenericMethod
                && m.GetParameters().Length == args.Length
                && m.GetParameters()
                    .Zip(args)
                    .All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))
            )
            .Invoke(owner, args);

    [JSExport]
    public static void StartScene()
    {
        // Installed driver subscriptions and job shutdown are process-lifetime
        // state. Restart requires a fresh WASM runtime until those owners adapt.
        if (sceneStarted)
            throw new InvalidOperationException("Start a fresh runtime for another scene");
        sceneStarted = true;
        try
        {
            var optionsType = Type("Waylith", "Waylith.EngineApplicationOptions");
            var options = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("Headless")!.SetValue(options, true);
            sceneEngine = (IDisposable)
                Activator.CreateInstance(Type("Waylith", "Waylith.EngineApplication"), options)!;
            sceneRuntime = Activator.CreateInstance(
                Type("UnityStubs", "Waylith.UnityCompat.UnityRuntime"),
                Get(sceneEngine, "Runtime")
            )!;
            var gameObject = Activator.CreateInstance(
                Type("UnityStubs", "UnityEngine.GameObject"),
                "Browser engine scene"
            )!;
            var meshFilter = InstanceCall(
                gameObject,
                "AddComponent",
                Type("UnityStubs", "UnityEngine.MeshFilter")
            )!;
            sceneRenderer = InstanceCall(
                gameObject,
                "AddComponent",
                Type("UnityStubs", "UnityEngine.MeshRenderer")
            )!;
            sceneTransform = Get(gameObject, "transform");
            sceneMesh = Invoke("Assembly-CSharp", "Verse.MeshMakerPlanes", "NewPlaneMesh", 1f)!;
            meshFilter.GetType().GetProperty("sharedMesh")!.SetValue(meshFilter, sceneMesh);
            sceneMaterial = Type("UnityStubs", "UnityEngine.Material")
                .GetConstructor(new[] { Type("UnityStubs", "UnityEngine.Shader") })!
                .Invoke(new object?[] { null });
            var color = Activator.CreateInstance(
                Type("UnityStubs", "UnityEngine.Color"),
                0f,
                1f,
                0f,
                1f
            )!;
            sceneMaterial.GetType().GetProperty("color")!.SetValue(sceneMaterial, color);
            sceneRenderer
                .GetType()
                .GetProperty("sharedMaterial")!
                .SetValue(sceneRenderer, sceneMaterial);
            previousSceneTime = null;
        }
        catch (Exception error)
        {
            try
            {
                StopScene();
            }
            catch { }
            throw new InvalidOperationException(error.ToString());
        }
    }

    [JSExport]
    public static string SceneFrame(double timestamp, int phase)
    {
        if (sceneEngine == null || sceneRuntime == null)
            throw new InvalidOperationException("Scene is stopped");
        try
        {
            if (
                !double.IsFinite(timestamp)
                || (previousSceneTime.HasValue && timestamp < previousSceneTime)
            )
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            if (!previousSceneTime.HasValue)
                sceneStartTime = timestamp;
            float delta = (float)((timestamp - (previousSceneTime ?? timestamp)) / 1000);
            float elapsed = (float)((timestamp - sceneStartTime) / 1000);
            previousSceneTime = timestamp;
            DrawCall("ClearFrameSubmissions");
            InputFrame("[]", 240);
            // Test control changes scene state; the actual world driver owns drawing.
            sceneRenderer!.GetType().GetProperty("enabled")!.SetValue(sceneRenderer, phase != 1);
            object Vector(float x, float y, float z) =>
                Activator.CreateInstance(Type("UnityStubs", "UnityEngine.Vector3"), x, y, z)!;
            sceneTransform!
                .GetType()
                .GetProperty("position")!
                .SetValue(sceneTransform, Vector(phase == 0 ? -.5f : .5f, 0, 0));
            sceneTransform
                .GetType()
                .GetProperty("localScale")!
                .SetValue(sceneTransform, Vector(.7f, 1, .7f));
            Invoke("Waylith", "Waylith.EngineApplication", "BeginFrame", delta, elapsed);
            try
            {
                Invoke("UnityStubs", "UnityStubs.EndOfFrame", "Run");
                Invoke("UnityStubs", "UnityStubs.FrameStart", "Run");
                InstanceCall(sceneRuntime, "BeginFrame", delta, elapsed);
                InstanceCall(sceneRuntime, "Update", delta, elapsed);
                var snapshot = JsonSerializer.Deserialize<JsonElement>(CaptureDraws());
                return JsonSerializer.Serialize(
                    new
                    {
                        snapshot,
                        unityFrame = Get(sceneRuntime, "FrameCount"),
                        engineFrame = Type("Waylith", "Waylith.Time")
                            .GetProperty("FrameCount")!
                            .GetValue(null),
                        delta,
                        elapsed,
                    }
                );
            }
            finally
            {
                Invoke("Waylith", "Waylith.EngineApplication", "EndFrame");
            }
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }

    [JSExport]
    public static void StopScene()
    {
        var engine = sceneEngine;
        if (engine == null)
            return;
        sceneEngine = null;
        try
        {
            if (sceneRuntime != null)
                InstanceCall(Get(sceneRuntime, "World"), "Shutdown");
            DrawCall("ClearFrameSubmissions");
        }
        finally
        {
            try
            {
                if (sceneMesh != null)
                    InstanceCall(sceneMesh, "Dispose");
                if (sceneMaterial != null)
                    ((IDisposable)Get(sceneMaterial, "engineMaterial")).Dispose();
            }
            finally
            {
                engine.Dispose();
                sceneRuntime = sceneRenderer = sceneTransform = sceneMesh = sceneMaterial = null;
                previousSceneTime = null;
            }
        }
    }
}
