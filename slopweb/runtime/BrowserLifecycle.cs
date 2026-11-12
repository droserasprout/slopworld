using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    private static FileStream? assetUpload;

    // The host serves an ignored copy; chunks enter the browser VFS, never the
    // host installation. A partial transfer is not exposed as a complete pack.
    [JSExport]
    public static void BeginAssetUpload()
    {
        assetUpload?.Dispose();
        assetUpload = File.Create("/Assets.zip.partial");
    }

    [JSExport]
    public static void AssetChunk(byte[] bytes) =>
        (assetUpload ?? throw new InvalidOperationException("No active asset transfer")).Write(
            bytes
        );

    [JSExport]
    public static void EndAssetUpload(bool commit)
    {
        assetUpload?.Dispose();
        assetUpload = null;
        if (commit)
            File.Move("/Assets.zip.partial", "/Assets.zip", true);
        else
            File.Delete("/Assets.zip.partial");
    }
}
