using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static async Task<string> ModHttp(string origin)
    {
        Invoke("Adapters", "BrowserAdapters.BrowserTransport", "Configure", origin);
        var health = Type("SlopWorld", "SlopWorld.Wire.Health");
        return await (Task<string>)
            typeof(Probe)
                .GetMethod(nameof(ModHttpTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(health)
                .Invoke(null, null)!;
    }

    private static async Task<string> ModHttpTyped<T>()
    {
        var completion = new TaskCompletionSource<string>();
        var client = Type("SlopWorld", "SlopWorld.DaemonClient");
        Action<T> ok = value => completion.TrySetResult(value?.ToString() ?? "null");
        Action<string> fail = error => completion.TrySetException(new IOException(error));
        client
            .GetMethod("Get")!
            .MakeGenericMethod(typeof(T))
            .Invoke(null, new object?[] { "/api/health", ok, fail, null, 5000 });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!completion.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Invoke(
                "SlopWorld",
                "SlopWorld.DaemonClient",
                "PumpCompletions",
                (Action<Exception>)(e => completion.TrySetException(e))
            );
            await Task.Delay(10);
        }
        if (!completion.Task.IsCompleted)
            throw new TimeoutException("Mod HTTP completion did not arrive");
        return await completion.Task;
    }

    [JSExport]
    public static async Task<string> ModSocket(string origin)
    {
        Invoke("Adapters", "BrowserAdapters.BrowserTransport", "Configure", origin);
        var entryType = Type("SlopWorld", "SlopWorld.ModEntry");
        var entry = Activator.CreateInstance(entryType, new object?[] { null })!;
        var settings = entryType.GetField("settings")!.GetValue(entry)!;
        settings.GetType().GetField("autoConnect")!.SetValue(settings, true);
        var eventType = Type("SlopWorld", "SlopWorld.Wire.Event");
        return await (Task<string>)
            typeof(Probe)
                .GetMethod(nameof(ModSocketTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(eventType)
                .Invoke(null, null)!;
    }

    private static async Task<string> ModSocketTyped<T>()
    {
        var type = Type("SlopWorld", "SlopWorld.HubTransport");
        var hub = Activator.CreateInstance(type, true)!;
        var messages = 0;
        var sessions = 0;
        var receivedSessions = false;
        type.GetField("OnMessage")!
            .SetValue(
                hub,
                (Action<T>)(
                    value =>
                    {
                        messages++;
                        var list = typeof(T).GetProperty("Sessions")?.GetValue(value);
                        if (list != null)
                        {
                            var rows = list.GetType().GetProperty("Sessions")!.GetValue(list)!;
                            sessions = (int)rows.GetType().GetProperty("Count")!.GetValue(rows)!;
                            receivedSessions = true;
                        }
                    }
                )
            );
        try
        {
            type.GetMethod("Connect")!.Invoke(hub, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!receivedSessions && DateTime.UtcNow < deadline)
            {
                type.GetMethod("Update")!.Invoke(hub, null);
                await Task.Delay(10);
            }
            var connected = (bool)type.GetProperty("Connected")!.GetValue(hub)!;
            var status = type.GetProperty("Status")!.GetValue(hub);
            if (!connected || !receivedSessions)
                throw new IOException("Hub socket failed: " + status);
            type.GetMethod("Disconnect")!.Invoke(hub, null);
            return JsonSerializer.Serialize(
                new
                {
                    connected,
                    messages,
                    sessions,
                    receivedSessions,
                    disconnected = !(bool)type.GetProperty("Connected")!.GetValue(hub)!,
                }
            );
        }
        finally
        {
            type.GetMethod("Disconnect")!.Invoke(hub, null);
        }
    }

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
        Check(
            "eco.tick-boundary",
            () =>
            {
                var type = Type("Assembly-CSharp", "Verse.TickManager");
                var game = Type("Assembly-CSharp", "Verse.Current")
                    .GetProperty("Game")!
                    .GetValue(null);
                if (game == null)
                    return "No colony: no simulation ticks";
                var ticks = game.GetType().GetField("tickManager")!.GetValue(game)!;
                var speed = type.GetProperty("CurTimeSpeed")!;
                speed.SetValue(ticks, Enum.Parse(speed.PropertyType, "Normal"));
                type.GetMethod("TickManagerUpdate")!.Invoke(ticks, null);
                return speed.GetValue(ticks)!.ToString()!;
            }
        );
        return JsonSerializer.Serialize(new { checks = Results, fullBootstrap = false });
    }

    [JSExport]
    public static string OfflineHook()
    {
        Results.Clear();
        Check(
            "offline.actual-setter",
            () =>
            {
                var prefs = Type("Assembly-CSharp", "Verse.Prefs");
                var dataType = Type("Assembly-CSharp", "Verse.PrefsData");
                var field = prefs.GetField("data", BindingFlags.NonPublic | BindingFlags.Static)!;
                var previous = field.GetValue(null);
                // Exercise the setter without loading preferences or starting a game.
                // The real Apply() skips main-thread-only work in this isolated runtime.
                var data = Activator.CreateInstance(dataType)!;
                field.SetValue(null, data);
                try
                {
                    var run = prefs.GetProperty("RunInBackground")!;
                    var values = new List<bool>();
                    foreach (var value in new[] { false, true, false })
                    {
                        run.SetValue(null, value);
                        values.Add((bool)run.GetValue(null)!);
                    }
                    return string.Join(",", values);
                }
                finally
                {
                    field.SetValue(null, previous);
                }
            }
        );
        return JsonSerializer.Serialize(new { checks = Results, modInitialized = false });
    }
}
