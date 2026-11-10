using System.Collections;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static string ClipboardTake() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserClipboard", "Take")!;

    [JSExport]
    public static void ClipboardComplete(int id, string text, string error) =>
        Invoke("Adapters", "BrowserAdapters.BrowserClipboard", "Complete", id, text, error);

    [JSExport]
    public static string WorkspaceState() =>
        (string)Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Snapshot")!;

    // Read-only test observation of the actual mod receive buffer. Input checks
    // still go through DOM events, the native terminal controls, and host tmux.
    [JSExport]
    public static bool WorkspaceTerminalContains(string name, string expected)
    {
        var hub = Type("SlopWorld", "SlopWorld.SessionHub").GetField("Instance")!.GetValue(null)!;
        var screen = hub.GetType().GetMethod("Screen")!.Invoke(hub, new object[] { name });
        return screen != null
            && ((string[])screen.GetType().GetField("Lines")!.GetValue(screen)!).Any(line =>
                line.Contains(expected, StringComparison.Ordinal)
            );
    }

    [JSExport]
    public static int InstallModContent(byte[] archive) =>
        (int)Invoke("Adapters", "BrowserAdapters.BrowserModContent", "Install", archive)!;

    [JSExport]
    public static string WorkspaceUpdate(string batch, double elapsed)
    {
        if (entryEngine == null)
            throw new InvalidOperationException("Workspace engine is stopped");
        Invoke("Waylith", "Waylith.EngineApplication", "BeginFrame", 1f / 60, (float)elapsed);
        try
        {
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "BeginFrame", elapsed);
            Invoke(
                "SlopWorld",
                "SlopWorld.DaemonClient",
                "PumpCompletions",
                (Action<Exception>)(e => throw e)
            );
            var hub = Type("SlopWorld", "SlopWorld.SessionHub")
                .GetField("Instance")!
                .GetValue(null)!;
            hub.GetType().GetMethod("Update")!.Invoke(hub, null);
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Update");
            var frame = (string)
                Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Frame", batch)!;
            Invoke("SlopWorld", "SlopWorld.WorkspaceWindow", "FlushTerminalInput");
            return frame;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
        finally
        {
            Invoke("Waylith", "Waylith.EngineApplication", "EndFrame");
        }
    }

    [JSExport]
    public static async Task<string> WorkspaceFrame(string origin)
    {
        try
        {
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "RequireEntry");
            Invoke("Adapters", "BrowserAdapters.BrowserTransport", "Configure", origin);
            Invoke("Adapters", "BrowserAdapters.BrowserGui", "Install");
            Invoke("Adapters", "BrowserAdapters.BrowserModKeys", "Install");
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Initialize");
            Invoke("SlopWorld", "SlopWorld.StripOptions", "Hide");
            Invoke("SlopWorld", "SlopWorld.ModOptions", "Install");
            var type = Type("SlopWorld", "SlopWorld.SessionHub");
            var hub = type.GetField("Instance")!.GetValue(null)!;
            type.GetMethod("Connect")!.Invoke(hub, null);
            var until = DateTime.UtcNow.AddSeconds(15);
            object? session = null;
            while (DateTime.UtcNow < until)
            {
                Invoke(
                    "SlopWorld",
                    "SlopWorld.DaemonClient",
                    "PumpCompletions",
                    (Action<Exception>)(e => throw e)
                );
                type.GetMethod("Update")!.Invoke(hub, null);
                session = ((IEnumerable)type.GetProperty("Sessions")!.GetValue(hub)!)
                    .Cast<object>()
                    .FirstOrDefault(s => (bool)s.GetType().GetField("Alive")!.GetValue(s)!);
                if (session != null)
                    break;
                await Task.Delay(16);
            }
            if (session == null)
                throw new InvalidOperationException("No live session for workspace");
            var name = (string)session.GetType().GetField("Name")!.GetValue(session)!;
            var window = Invoke("SlopWorld", "SlopWorld.WorkspaceWindow", "Open", name)!;
            return (string)
                Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Repaint", window)!;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
    }
}
