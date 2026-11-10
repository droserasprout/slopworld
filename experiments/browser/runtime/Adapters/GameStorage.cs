using Verse;

namespace BrowserAdapters;

// The installed serializer owns save contents. This adapter only transfers one
// experiment-owned file between the browser VFS and the local harness.
public static class GameStorage
{
    private const string SaveName = "BrowserColony";

    public static byte[] Save()
    {
        if (Current.ProgramState != ProgramState.Playing || Current.Game?.Maps.Count is not > 0)
            throw new InvalidOperationException("No initialized colony to save");
        var path = GenFilePaths.FilePathForSavedGame(SaveName);
        // SaveGame logs failures instead of throwing. Removing the prior file
        // prevents a failed write from exporting a stale successful save.
        File.Delete(path);
        GameDataSaveLoader.SaveGame(SaveName);
        return File.ReadAllBytes(path);
    }

    public static void Load(byte[] contents)
    {
        if (!PlayDataLoader.Loaded || Current.Game != null)
            throw new InvalidOperationException("A fresh loaded Entry scene is required");
        File.WriteAllBytes(GenFilePaths.FilePathForSavedGame(SaveName), contents);
        GameDataSaveLoader.LoadGame(SaveName);
    }
}
