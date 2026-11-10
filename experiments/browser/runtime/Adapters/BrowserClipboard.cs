using System.Text.Json;

namespace BrowserAdapters;

// Browser clipboard requests retain the native callback, including its pane/run
// lifetime guard. PRIMARY stays local and never overwrites the OS clipboard.
public static class BrowserClipboard
{
    static string buffer = "",
        primary = "";
    static bool supplied;
    static int serial;
    static readonly List<object> requests = new();
    static readonly Dictionary<
        int,
        (Action<string>? receive, Action? ok, Action<string>? fail)
    > pending = new();

    public static string Get() => buffer;

    public static void Set(string text) => Copy(text, null, null);

    public static bool UsesX11() => true; // Select native asynchronous clipboard policy.

    public static void Supply(string text)
    {
        buffer = text;
        supplied = true;
    }

    public static void EndEvent() => supplied = false;

    public static void ReadLocalPaste(
        Action<string> text,
        Action<byte[]>? image,
        Action<string>? fail
    ) => ReadLocal(false, text, fail);

    public static void ReadLocal(bool isPrimary, Action<string> receive, Action<string>? fail)
    {
        if (isPrimary)
        {
            receive(primary);
            return;
        }
        if (supplied)
        {
            supplied = false;
            receive(buffer);
            return;
        }
        int id = ++serial;
        pending.Add(id, (receive, null, fail));
        requests.Add(new { id, kind = "read" });
    }

    public static void Copy(string text, Action? ok, Action<string>? fail)
    {
        buffer = text;
        int id = ++serial;
        pending.Add(id, (null, ok, fail));
        requests.Add(
            new
            {
                id,
                kind = "write",
                text,
            }
        );
    }

    public static void CopyFromApplication(string text) => Copy(text, null, null);

    public static void CopyPrimary(string text, Action? ok, Action<string>? fail)
    {
        primary = text;
        ok?.Invoke();
    }

    public static string Take()
    {
        var result = JsonSerializer.Serialize(requests);
        requests.Clear();
        return result;
    }

    public static void Complete(int id, string text, string error)
    {
        if (!pending.Remove(id, out var callback))
            return;
        if (error.Length > 0)
        {
            // Native text-only paste can fall back to GUIUtility after failure.
            // Clear it so denied browser access cannot paste an earlier selection.
            if (callback.receive != null)
                buffer = "";
            callback.fail?.Invoke(error);
            return;
        }
        if (callback.receive != null)
        {
            buffer = text;
            callback.receive(text);
        }
        callback.ok?.Invoke();
    }
}
