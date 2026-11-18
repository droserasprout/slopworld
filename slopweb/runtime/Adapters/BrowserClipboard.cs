using System.Text.Json;

namespace BrowserAdapters;

// Browser clipboard requests retain the native callback, including its pane/run
// lifetime guard. PRIMARY is published through DOM selection, independently of CLIPBOARD.
public static class BrowserClipboard
{
    static string buffer = "";
    static bool supplied;
    static bool suppliedImage;
    static int serial;
    static readonly HashSet<int> primaryReads = new();
    static readonly List<object> requests = new();
    static readonly Dictionary<
        int,
        (Action<string>? receive, Action<byte[]>? image, Action? ok, Action<string>? fail)
    > pending = new();

    public static string Get() => buffer;

    public static void Set(string text) => Copy(text, null, null);

    public static bool UsesLinuxSelections => true; // Select native asynchronous clipboard policy.

    public static void Supply(string text, bool hasImage = false)
    {
        buffer = text;
        supplied = true;
        suppliedImage = hasImage;
    }

    public static void EndEvent() => supplied = false;

    public static void ReadLocalPaste(
        Action<string> text,
        Action<byte[]>? image,
        Action<string>? fail
    ) => ReadClipboard(text, image, fail);

    public static void ReadLocal(bool isPrimary, Action<string> receive, Action<string>? fail)
    {
        if (isPrimary)
        {
            int id = ++serial;
            pending.Add(id, (receive, null, null, fail));
            primaryReads.Add(id);
            requests.Add(new { id, kind = "readPrimary" });
            return;
        }
        ReadClipboard(receive, null, fail);
    }

    static void ReadClipboard(Action<string> receive, Action<byte[]>? image, Action<string>? fail)
    {
        bool fromEvent = supplied;
        if (fromEvent)
        {
            supplied = false;
            if (buffer.Length > 0 || image == null || !suppliedImage)
            {
                receive(buffer);
                return;
            }
        }
        // Allocate the reply owner during the original native key dispatch. File
        // reads must never redispatch V against whichever pane is selected later.
        int id = ++serial;
        pending.Add(id, (receive, image, null, fail));
        requests.Add(
            new
            {
                id,
                kind = fromEvent ? "eventImage" : "read",
                images = image != null,
            }
        );
    }

    public static void Copy(string text, Action? ok, Action<string>? fail)
    {
        buffer = text;
        int id = ++serial;
        pending.Add(id, (null, null, ok, fail));
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
        int id = ++serial;
        pending.Add(id, (null, null, ok, fail));
        requests.Add(
            new
            {
                id,
                kind = "primary",
                text,
            }
        );
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
        bool isPrimary = primaryReads.Remove(id);
        if (error.Length > 0)
        {
            // Native text-only paste can fall back to GUIUtility after failure.
            // Clear it so denied browser access cannot paste an earlier selection.
            if (callback.receive != null && !isPrimary)
                buffer = "";
            callback.fail?.Invoke(error);
            return;
        }
        if (callback.receive != null)
        {
            if (!isPrimary)
                buffer = text;
            callback.receive(text);
        }
        callback.ok?.Invoke();
    }

    public static void CompleteImage(int id, string base64, string error)
    {
        if (error.Length > 0)
        {
            Complete(id, "", error);
            return;
        }
        if (!pending.Remove(id, out var callback))
            return;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            callback.fail?.Invoke("Invalid clipboard PNG encoding");
            return;
        }
        callback.image?.Invoke(bytes);
    }
}
