// Owns browser window intent. DOM fullscreenchange remains the source of actual state;
// native settings, palette commands and key bindings all keep using LinuxGameWindow.Set.
using System.Reflection;

namespace BrowserAdapters;

public static class BrowserWindow
{
    public static Action<bool>? Request;

    public static void Set(bool fullscreen) => Request?.Invoke(fullscreen);

    public static void UpdateState(bool fullscreen)
    {
        var settings = Type.GetType("SlopWorld.Settings, SlopWorld", true)!
            .GetProperty("S", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        settings.GetType().GetField("fullscreen")!.SetValue(settings, fullscreen);
    }
}
