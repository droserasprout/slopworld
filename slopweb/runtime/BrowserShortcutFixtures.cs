using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    static readonly List<string> shortcutReaders = new();

    // Test setup/observation only: navigation itself must arrive through DOM keys.
    // Refuse existing native readers so preview replacement cannot retire user tabs.
    [JSExport]
    public static string WorkspaceShortcutReaders(string action, string first, string second)
    {
        Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "RequireEntry");
        var files = Type("SlopWorld", "SlopWorld.FilesView");
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        string? CurrentReader() =>
            (string?)files.GetProperty("CurrentViewerHeader", hidden)!.GetValue(null);
        if (action == "prepare")
        {
            if (
                shortcutReaders.Count != 0
                || (bool)files.GetProperty("HasNativeReaders", hidden)!.GetValue(null)!
            )
                throw new InvalidOperationException(
                    "Shortcut fixtures require no existing native readers"
                );
            foreach (string path in new[] { first, second })
            {
                if (!path.EndsWith(".md", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Shortcut reader fixtures require Markdown paths"
                    );
                files
                    .GetMethod("ViewFile")!
                    .Invoke(null, new object?[] { "", path, Path.GetFileName(path), 0, null });
                string header =
                    CurrentReader()
                    ?? throw new InvalidOperationException("Native reader did not open");
                shortcutReaders.Add(header);
                if (!(bool)Invoke("SlopWorld", "SlopWorld.FilesView", "LockViewerFile", "", path)!)
                    throw new InvalidOperationException("Native reader fixture did not pin");
            }
        }
        else if (action == "cleanup")
        {
            foreach (string header in shortcutReaders.ToArray())
            {
                var reader = files
                    .GetMethod("FindReader", hidden)!
                    .Invoke(null, new object[] { header });
                if (reader != null)
                    reader.GetType().GetMethod("CloseTab")!.Invoke(reader, new object[] { header });
                shortcutReaders.Remove(header);
            }
        }
        else if (action != "state")
            throw new ArgumentException("Unknown shortcut fixture action", nameof(action));
        return JsonSerializer.Serialize(
            new { current = CurrentReader(), created = shortcutReaders.ToArray() }
        );
    }
}
