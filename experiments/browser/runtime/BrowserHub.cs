using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static async Task<string> ModHub(string origin)
    {
        Invoke("Adapters", "BrowserAdapters.BrowserTransport", "Configure", origin);
        var type = Type("SlopWorld", "SlopWorld.SessionHub");
        var hub = type.GetField("Instance")!.GetValue(null)!;
        object? Call(string method, params object[] args) =>
            type.GetMethod(method)!.Invoke(hub, args);
        var failures = new List<string>();
        async Task PumpUntil(Func<bool> ready)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            do
            {
                Invoke(
                    "SlopWorld",
                    "SlopWorld.DaemonClient",
                    "PumpCompletions",
                    (Action<Exception>)(e => failures.Add(e.ToString()))
                );
                Call("Update");
                if (ready())
                    return;
                await Task.Delay(16);
            } while (DateTime.UtcNow < until);
            throw new TimeoutException("SessionHub: " + type.GetProperty("Status")!.GetValue(hub));
        }
        var evidence = new List<object>();
        string? selected = null;
        try
        {
            Call("Connect");
            await PumpUntil(() => ((IList)type.GetProperty("Sessions")!.GetValue(hub)!).Count > 0);
            var sessions = ((IEnumerable)type.GetProperty("Sessions")!.GetValue(hub)!)
                .Cast<object>()
                .Where(s => (bool)s.GetType().GetField("Alive")!.GetValue(s)!)
                .Take(2)
                .ToArray();
            if (sessions.Length == 0)
                throw new InvalidOperationException("No live session to attach");
            foreach (var session in sessions)
            {
                if (selected != null)
                    Call("Unsubscribe", selected);
                selected = (string)session.GetType().GetField("Name")!.GetValue(session)!;
                Call("Subscribe", selected);
                object? screen = null;
                await PumpUntil(() =>
                {
                    screen = Call("Screen", selected);
                    return screen != null
                        && (bool)screen.GetType().GetProperty("HasSequence")!.GetValue(screen)!;
                });
                var st = screen!.GetType();
                var lines = (string[])st.GetField("Lines")!.GetValue(screen)!;
                evidence.Add(
                    new
                    {
                        name = selected,
                        cols = st.GetField("Cols")!.GetValue(screen),
                        rows = st.GetField("Rows")!.GetValue(screen),
                        nonemptyLines = lines.Count(s => s.Length > 0),
                        sequence = st.GetProperty("Seq")!.GetValue(screen),
                    }
                );
            }
            if (failures.Count > 0)
                throw new InvalidOperationException(string.Join("\n", failures));
            return JsonSerializer.Serialize(
                new
                {
                    online = type.GetProperty("Online")!.GetValue(hub),
                    sessions = evidence,
                    readOnly = true,
                }
            );
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
        finally
        {
            if (selected != null)
                Call("Unsubscribe", selected);
            Call("Disconnect");
        }
    }
}
