using System.Text.Json;
using UnityEngine.SceneManagement;
using Verse;

namespace BrowserAdapters;

// Enter the installed quick-test/new-game path. No synthetic world, map, pawn,
// or tick results: all state below comes from Current.Game and its map owners.
public static class GameSession
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

    public static void RequestPlay()
    {
        if (!PlayDataLoader.Loaded || Current.Game != null)
            throw new InvalidOperationException("A fresh loaded Entry scene is required");
        SceneManager.LoadScene("Play");
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

    public static void Resume()
    {
        if (
            Current.ProgramState != ProgramState.Playing
            || Current.Game == null
            || Current.Game.Maps.Count == 0
        )
            throw new InvalidOperationException("No initialized colony to resume");
        Current.Game.tickManager.CurTimeSpeed = TimeSpeed.Normal;
    }

    public static string Camera()
    {
        var camera = Find.Camera;
        var matrix = camera.projectionMatrix * camera.worldToCameraMatrix;
        var columns = new float[16];
        for (int column = 0; column < 4; column++)
        for (int row = 0; row < 4; row++)
            columns[column * 4 + row] = matrix[row, column];
        return JsonSerializer.Serialize(
            new
            {
                width = camera.pixelWidth,
                height = camera.pixelHeight,
                viewProjection = columns,
            }
        );
    }
}
