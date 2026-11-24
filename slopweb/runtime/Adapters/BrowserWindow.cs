// Owns browser window intent. DOM fullscreenchange remains the source of actual state;
// native settings, palette commands and key bindings all keep using LinuxGameWindow.Set.
namespace BrowserAdapters;

public static class BrowserWindow
{
    public static Action<bool>? Request;
    public static Action<string>? UrlRequest;

    public static void OpenUrl(string url) => UrlRequest?.Invoke(url);

    public static void Set(bool fullscreen) => Request?.Invoke(fullscreen);
}
