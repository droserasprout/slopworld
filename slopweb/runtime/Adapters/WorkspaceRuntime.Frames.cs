using System.Reflection;
using System.Text.Json;
using SlopWorld;
using Waylith;

namespace BrowserAdapters;

public sealed partial class WorkspaceRuntime
{
    static readonly JsonElement EmptyGuiBatch = JsonElement.Parse("[]"u8);

    // Waylith's external frame driver is internal in the installed engine.
    static readonly Action<float, float> BeginEngineFrame = typeof(EngineApplication)
        .GetMethod(
            "BeginFrame",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(float), typeof(float)]
        )!
        .CreateDelegate<Action<float, float>>();
    static readonly Action EndEngineFrame = typeof(EngineApplication)
        .GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic, Type.EmptyTypes)!
        .CreateDelegate<Action>();

    public void WorkspaceFontFallbackComplete(string json)
    {
        if (!BrowserFontBank.CompleteFallbacks(json))
            return;
        BrowserGui.FontsChanged();
        // Native pane textures retain terminal rows independently of GUI batches.
        TerminalFont.Invalidate();
        BrowserActivity.Notify();
    }

    public string WorkspaceSelect(string name)
    {
        WorkspaceDrawing.RequireEntry();
        if (!SessionHub.Instance.Sessions.Any(s => s.Alive && s.Name == name))
            throw new InvalidOperationException("Requested workspace session is not live");
        SlopWorld.WorkspaceWindow.Open(name);
        return WorkspaceDrawing.Frame(EmptyGuiBatch, -1d, true);
    }

    public string WorkspaceUpdatePacket(string batch, double elapsed, double retainedPacket)
    {
        if (entry == null)
            throw new InvalidOperationException("Workspace engine is stopped");
        BeginEngineFrame(1f / 60, (float)elapsed);
        try
        {
            WorkspaceDrawing.BeginFrame(elapsed);
            using var events = batch == "[]" ? null : JsonDocument.Parse(batch);
            var items = events?.RootElement ?? EmptyGuiBatch;
            BrowserCursor.Update(items);
            DaemonClient.PumpCompletions(static e => throw e);
            SessionHub.Instance.Update();
            WorkspaceDrawing.Update();
            var frame = WorkspaceDrawing.Frame(items, retainedPacket, false);
            // Full ModBootstrap is absent in Entry. Preserve its native dirty-save pump.
            Settings.S.FlushIfDue();
            return frame;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
        finally
        {
            EndEngineFrame();
        }
    }

    public async Task<string> WorkspaceFrame(string origin, string requestedSession)
    {
        try
        {
            WorkspaceDrawing.RequireEntry();
            BrowserTransport.Configure(origin);
            BrowserGui.Install();
            if (BrowserFontBank.Paths().Length > 0)
                UiFont.Apply();
            BrowserModKeys.Install();
            BrowserWorkspaceDoors.Install();
            BrowserCursor.Install();
            WorkspaceDrawing.Initialize();
            StripOptions.Hide();
            ModOptions.Install();
            var hub = SessionHub.Instance;
            hub.Connect();
            var until = DateTime.UtcNow.AddSeconds(15);
            SessionInfo? session = null;
            while (DateTime.UtcNow < until)
            {
                DaemonClient.PumpCompletions(static e => throw e);
                hub.Update();
                session = hub.Sessions.FirstOrDefault(s =>
                    s.Alive
                    && (string.IsNullOrEmpty(requestedSession) || s.Name == requestedSession)
                );
                if (session != null)
                    break;
                // An authoritative empty catalog is a usable native workspace:
                // its sidebar and TopBar own session creation and connection UI.
                // A named reference still waits for that exact live session.
                if (string.IsNullOrEmpty(requestedSession) && hub.SessionsVersion > 0)
                    break;
                await Task.Delay(16);
            }
            if (session != null)
            {
                var name = session.Name;
                SlopWorld.WorkspaceWindow.Open(name);
            }
            else if (!string.IsNullOrEmpty(requestedSession))
                throw new InvalidOperationException("No matching live session for workspace");
            // The first presentation needs native chrome, layout and deferred windows
            // even when no terminal is open. It seeds the same resources as live frames.
            return WorkspaceDrawing.Frame(EmptyGuiBatch, -1d, true);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
    }
}
