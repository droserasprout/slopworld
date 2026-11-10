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

    // Separate checkpoint: exercise the real constructor and game bootstrap
    // without claiming that the headless engine is a browser render backend.
    [JSExport]
    public static string BootstrapHeadless()
    {
        Results.Clear();
        if (File.Exists("/Assets.zip"))
            Check(
                "assets.open",
                () =>
                {
                    using var pack = (IDisposable?)Invoke(
                        "Waylith",
                        "Waylith.Assets.AssetPack",
                        "TryOpen",
                        "/Assets.zip"
                    );
                    if (pack == null)
                        throw new InvalidOperationException("Installed asset pack did not open");
                    return pack.GetType().GetProperty("Count")!.GetValue(pack);
                }
            );
        IDisposable? engine = null;
        Check(
            "engine.headless-constructor",
            () =>
            {
                var optionsType = Type("Waylith", "Waylith.EngineApplicationOptions");
                var options = Activator.CreateInstance(optionsType)!;
                optionsType.GetProperty("Headless")!.SetValue(options, true);
                // AudioBackend's real default is null. Do not instantiate MiniAudio,
                // whose no-audio mode still requires its native library.
                engine = (IDisposable)
                    Activator.CreateInstance(
                        Type("Waylith", "Waylith.EngineApplication"),
                        options
                    )!;
                return "constructed; no graphics or audio backend";
            }
        );
        if (engine != null)
        {
            Check(
                "game.bootstrap",
                () =>
                {
                    Invoke("RimWorld", "RimWorldWaylith.Bootstrap", "Initialize", engine);
                    return "initialized";
                }
            );
            Check(
                "engine.headless-dispose",
                () =>
                {
                    engine.Dispose();
                    engine.Dispose();
                    return "disposed twice";
                }
            );
        }
        return JsonSerializer.Serialize(new { checks = Results, rendered = false });
    }
}
