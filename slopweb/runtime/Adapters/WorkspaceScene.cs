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
        // Native OnGUI normally refreshes these values. Map section culling also
        // needs them; without a browser handoff its aspect ratio becomes 0/0.
        UI.ApplyUIScale();
    }

    public static string Snapshot()
    {
        var game = Current.Game;
        return JsonSerializer.Serialize(
            new
            {
                scene = SceneManager.GetActiveScene().name,
                programState = Current.ProgramState.ToString(),
                hasGame = game != null,
                hasWorld = game?.World != null,
                seed = game?.World?.info.seedString,
                worldTiles = game?.World?.grid.Surface.TilesCount ?? 0,
                ticks = game?.tickManager?.TicksGame ?? 0,
                timeSpeed = game?.tickManager?.CurTimeSpeed.ToString(),
                forcePaused = game?.tickManager?.ForcePaused ?? false,
                drawingMap = RimWorld.Planet.WorldRendererUtility.DrawingMap,
                uiWidth = UI.screenWidth,
                uiHeight = UI.screenHeight,
                maps = game
                    ?.Maps.Select(map => new
                    {
                        sizeX = map.Size.x,
                        sizeZ = map.Size.z,
                        colonists = map.mapPawns.FreeColonistsSpawnedCount,
                        things = map.listerThings.AllThings.Count,
                        centerTerrain = map.terrainGrid.TerrainAt(map.Center)?.defName,
                        pawns = map
                            .mapPawns.FreeColonistsSpawned.Select(pawn => new
                            {
                                id = pawn.thingIDNumber,
                                x = pawn.Position.x,
                                z = pawn.Position.z,
                                job = pawn.CurJob?.def.defName,
                            })
                            .ToArray(),
                    })
                    .ToArray(),
                rendered = false,
            }
        );
    }
}
