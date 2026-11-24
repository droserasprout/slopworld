using System.Reflection;
using System.Text.Json;
using SlopWorld;
using UnityEngine.SceneManagement;
using Verse;
using Waylith;
using Waylith.UnityCompat;

namespace BrowserAdapters;

public sealed partial class WorkspaceRuntime : IDisposable
{
    EngineApplication? entry;

    public string SystemLanguageFolder() => LanguageDatabase.SystemLanguageFolderName();

    public bool InitializeEco()
    {
        var mod = new ModEntry(
            BrowserModContent.Current()
                ?? throw new InvalidOperationException("Mount mod content before mod startup")
        );
        mod.settings.ecoMode = true;
        return Eco.Resting;
    }

    public void StartEntryEngine()
    {
        RuntimeServices.ConfigureLogging();
        entry = new EngineApplication(
            new EngineApplicationOptions { Headless = true, AudioBackend = new SilentAudio() }
        );
        SilentAudio.ActivateForEntry(entry);
        var bootstrap = Assembly.Load("RimWorld").GetType("RimWorldWaylith.Bootstrap", true)!;
        bootstrap
            .GetMethod(
                "Initialize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(EngineApplication)]
            )!
            .CreateDelegate<Action<EngineApplication>>()(entry);
    }

    public bool UpdateEntryEngine(int frame)
    {
        if (entry == null)
            throw new InvalidOperationException("Entry is stopped");
        InputFrame("[]", 240);
        Waylith.Rendering.MeshDrawSubmission.ClearFrameSubmissions();
        BeginEngineFrame(1f / 60, frame / 60f);
        try
        {
            entry.OnUpdate?.Invoke();
        }
        finally
        {
            EndEngineFrame();
        }
        return PlayDataLoader.Loaded;
    }

    public string GameSnapshot() => WorkspaceScene.Snapshot();

    public string EntryReport()
    {
        var runtime = UnityRuntime.Current;
        var loaded = PlayDataLoader.Loaded;
        return JsonSerializer.Serialize(
            new
            {
                messages = Log.Messages.Select(m => m.text).ToArray(),
                world = runtime?.World.GetDiagnosticsSnapshot(),
                engineErrors = RuntimeServices.Errors,
                engineErrorCount = RuntimeServices.ErrorCount,
                omittedErrorCount = RuntimeServices.OmittedErrorCount,
                unityFrame = runtime?.FrameCount,
                loaded,
                humanBody = loaded
                    ? DefDatabase<ThingDef>.GetNamed("Human").race.body.defName
                    : null,
                activeScene = SceneManager.GetActiveScene().name,
                definitions = new Dictionary<string, int>
                {
                    ["Verse.ThingDef"] = DefDatabase<ThingDef>.DefCount,
                    ["Verse.TerrainDef"] = DefDatabase<TerrainDef>.DefCount,
                    ["Verse.RecipeDef"] = DefDatabase<RecipeDef>.DefCount,
                },
                customClrInjection = false,
                rendered = false,
                colony = false,
            }
        );
    }

    public void Dispose()
    {
        var engine = entry;
        entry = null;
        if (engine == null)
            return;
        try
        {
            UnityRuntime.Current?.World.Shutdown();
        }
        finally
        {
            engine.Dispose();
        }
    }
}
