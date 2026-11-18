// Owns browser window intent. DOM fullscreenchange remains the source of actual state;
// native settings, palette commands and key bindings all keep using LinuxGameWindow.Set.
using System.Reflection;

namespace BrowserAdapters;

public static class BrowserWindow
{
    public static Action<bool>? Request;
    public static Action<string>? UrlRequest;

    public static void OpenUrl(string url) => UrlRequest?.Invoke(url);

    static readonly Lazy<Func<string>> ReadTitle = new(() =>
        Type.GetType("SlopWorld.WindowTitle, SlopWorld", true)!
            .GetMethod("Current")!
            .CreateDelegate<Func<string>>()
    );

    public static string Title() => ReadTitle.Value();

    public static void Set(bool fullscreen) => Request?.Invoke(fullscreen);

    public static void UpdateState(bool fullscreen)
    {
        var settings = Type.GetType("SlopWorld.Settings, SlopWorld", true)!
            .GetProperty("S", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        settings.GetType().GetField("fullscreen")!.SetValue(settings, fullscreen);
    }
}
