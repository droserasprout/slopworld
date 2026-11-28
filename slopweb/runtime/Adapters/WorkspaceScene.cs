using System.Text.Json;
using UnityEngine.SceneManagement;
using Verse;

namespace BrowserAdapters;

// Own viewport configuration and read-only native scene state for the Entry workspace.
public static class WorkspaceScene
{
    public static void ConfigureViewport(int width, int height)
    {
        if (!PlayDataLoader.Loaded || width <= 0 || height <= 0)
            throw new InvalidOperationException("A loaded game and positive viewport are required");
        var runtime =
            Waylith.UnityCompat.UnityRuntime.Current
            ?? throw new InvalidOperationException("The browser runtime is not initialized");
        runtime.ConfigureScreen(width, height);
        runtime.ConfigureScreenWindow(width, height);
        // Native OnGUI normally refreshes the scaled Entry viewport.
        UI.ApplyUIScale();
    }

    // Entry status deliberately never traverses a world, map or simulation.
    public static object Status() =>
        new
        {
            scene = SceneManager.GetActiveScene().name,
            programState = Current.ProgramState.ToString(),
            hasGame = Current.Game != null,
            uiWidth = UI.screenWidth,
            uiHeight = UI.screenHeight,
        };

    public static string Snapshot() => JsonSerializer.Serialize(Status());
}
