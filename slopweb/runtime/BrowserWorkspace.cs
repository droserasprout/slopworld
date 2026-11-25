using System.Runtime.InteropServices.JavaScript;
using SlopWeb.Runtime;

public partial class Probe
{
    static readonly LateRuntime<IWorkspaceRuntime> Workspace = new(() =>
        (IWorkspaceRuntime)
            Activator.CreateInstance(Type("Adapters", "BrowserAdapters.WorkspaceRuntime"))!
    );
    static IWorkspaceRuntime Runtime => Workspace.Value;

    [JSExport]
    public static void InstallPlatform(string directory) => Runtime.InstallPlatform(directory);

    [JSExport]
    public static string WorkspaceFontFallbackTake() => Runtime.WorkspaceFontFallbackTake();

    [JSExport]
    public static void WorkspaceFontFallbackComplete(string json) =>
        Runtime.WorkspaceFontFallbackComplete(json);

    [JSExport]
    public static void WorkspaceFontFallbackRetry() => Runtime.WorkspaceFontFallbackRetry();

    [JSExport]
    public static void WorkspaceRasterizer(
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Func<string, string> rasterize
    ) => Runtime.WorkspaceRasterizer(rasterize);

    [JSExport]
    public static void WorkspaceFontsDispose() => Runtime.WorkspaceFontsDispose();

    [JSExport]
    public static void WorkspaceWake([JSMarshalAs<JSType.Function>] Action wake) =>
        Runtime.WorkspaceWake(wake);

    [JSExport]
    public static float WorkspaceNextUpdateDelay() => Runtime.WorkspaceNextUpdateDelay();

    [JSExport]
    public static int WorkspaceFrameLimit() => Runtime.WorkspaceFrameLimit();

    [JSExport]
    public static void WorkspaceClearWake() => Runtime.WorkspaceClearWake();

    [JSExport]
    public static bool WorkspacePacketCache(bool enabled) => Runtime.WorkspacePacketCache(enabled);

    [JSExport]
    public static bool WorkspaceRowCommands(bool enabled) => Runtime.WorkspaceRowCommands(enabled);

    [JSExport]
    public static string WorkspaceRowCommandsStats() => Runtime.WorkspaceRowCommandsStats();

    [JSExport]
    public static string WorkspaceDynamicFonts(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> changed
    ) => Runtime.WorkspaceDynamicFonts(changed);

    [JSExport]
    public static void WorkspaceFontHold(string json) => Runtime.WorkspaceFontHold(json);

    [JSExport]
    public static void WorkspaceFontRestore(string json) => Runtime.WorkspaceFontRestore(json);

    [JSExport]
    public static void WorkspaceGlyphsReady() => Runtime.WorkspaceGlyphsReady();

    [JSExport]
    public static void WorkspaceFontsReady() => Runtime.WorkspaceFontsReady();

    [JSExport]
    public static void WorkspaceFontCatalog(string json) => Runtime.WorkspaceFontCatalog(json);

    [JSExport]
    public static void WorkspaceFontChains(string json) => Runtime.WorkspaceFontChains(json);

    [JSExport]
    public static string WorkspaceFontSnapshot() => Runtime.WorkspaceFontSnapshot();

    [JSExport]
    public static int WorkspaceFont(byte[] font, string bake, int defaultIndex) =>
        Runtime.WorkspaceFont(font, bake, defaultIndex);

    [JSExport]
    public static void WorkspaceAppearance(string json) => Runtime.WorkspaceAppearance(json);

    [JSExport]
    public static string WorkspaceTitle() => Runtime.WorkspaceTitle();

    [JSExport]
    public static bool WorkspaceFullscreenKey(string key) => Runtime.WorkspaceFullscreenKey(key);

    [JSExport]
    public static void WorkspaceWindow(
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> request
    ) => Runtime.WorkspaceWindow(request);

    [JSExport]
    public static void WorkspaceOpenUrl(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> request
    ) => Runtime.WorkspaceOpenUrl(request);

    [JSExport]
    public static void WorkspaceFullscreenState(bool fullscreen) =>
        Runtime.WorkspaceFullscreenState(fullscreen);

    [JSExport]
    public static void WorkspacePreferences(
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Func<string, string> read,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String, JSType.Boolean>>]
            Func<string, string, bool> persist
    ) => Runtime.WorkspacePreferences(read, persist);

    [JSExport]
    public static void WorkspaceKeyBindings(
        string json,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> persist
    ) => Runtime.WorkspaceKeyBindings(json, persist);

    [JSExport]
    public static void WorkspaceSavePreferences() => Runtime.WorkspaceSavePreferences();

    [JSExport]
    public static string WorkspaceSelect(string name) => Runtime.WorkspaceSelect(name);

    [JSExport]
    public static void ConfigureWorkspaceViewport(int width, int height, float scale) =>
        Runtime.ConfigureWorkspaceViewport(width, height, scale);

    [JSExport]
    public static void WorkspaceProfile(bool enabled) => Runtime.WorkspaceProfile(enabled);

    [JSExport]
    public static string ClipboardTake() => Runtime.ClipboardTake();

    [JSExport]
    public static void ClipboardComplete(int id, string text, string error) =>
        Runtime.ClipboardComplete(id, text, error);

    [JSExport]
    public static void ClipboardCompleteImage(int id, string base64, string error) =>
        Runtime.ClipboardCompleteImage(id, base64, error);

    [JSExport]
    public static string WorkspaceState() => Runtime.WorkspaceState();

    [JSExport]
    public static string WorkspaceAudio() => Runtime.WorkspaceAudio();

    [JSExport]
    public static void WorkspaceAudioIdentify(string name) => Runtime.WorkspaceAudioIdentify(name);

    [JSExport]
    public static void WorkspaceAudioReport(
        int revision,
        bool playing,
        string stream,
        string title,
        string error
    ) => Runtime.WorkspaceAudioReport(revision, playing, stream, title, error);

    [JSExport]
    public static void WorkspaceAudioQuit() => Runtime.WorkspaceAudioQuit();

    [JSExport]
    public static void WorkspaceAudioEnded(int revision) => Runtime.WorkspaceAudioEnded(revision);

    [JSExport]
    public static bool WorkspaceBackgroundVisible() => Runtime.WorkspaceBackgroundVisible();

    [JSExport]
    public static float WorkspaceBackgroundDimming() => Runtime.WorkspaceBackgroundDimming();

    [JSExport]
    public static int InstallModContent(byte[] archive) => Runtime.InstallModContent(archive);

    [JSExport]
    public static string WorkspaceUpdatePacket(
        string batch,
        double elapsed,
        double retainedPacket
    ) => Runtime.WorkspaceUpdatePacket(batch, elapsed, retainedPacket);

    [JSExport]
    public static Task<string> WorkspaceFrame(string origin, string requestedSession) =>
        Runtime.WorkspaceFrame(origin, requestedSession);

    [JSExport]
    public static string WorkspaceUpdate(string batch, double elapsed) =>
        WorkspaceUpdatePacket(batch, elapsed, -1);
}
