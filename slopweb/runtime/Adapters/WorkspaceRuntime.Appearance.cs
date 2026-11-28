using System.Text.Json;
using SlopWorld;

namespace BrowserAdapters;

public sealed partial class WorkspaceRuntime
{
    public void WorkspaceAppearance(string json)
    {
        // Host imports and origin storage share one presentation validator. Keep
        // display-only host metadata separate from persisted preferences.
        var settings = Settings.S;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("hostHome", out var home))
            BrowserSidebar.ConfigureHome(home.GetString()!);
        BrowserPreferences.Apply(settings, json);
    }
}
