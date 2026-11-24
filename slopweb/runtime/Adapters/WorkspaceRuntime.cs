using SlopWeb.Runtime;
using SlopWorld;

namespace BrowserAdapters;

public sealed partial class WorkspaceRuntime : IWorkspaceRuntime
{
    public void InstallPlatform(string directory) => BrowserPlatform.Install(directory);

    public string WorkspaceCursorTake() => BrowserCursor.Take();

    public string WorkspaceFontFallbackTake() => BrowserFontBank.TakeFallbacks();

    public void WorkspaceFontFallbackRetry() => BrowserFontBank.RetryFallbacks();

    public void WorkspaceRasterizer(Func<string, string> rasterize) =>
        BrowserFontBank.Rasterize = rasterize;

    public void WorkspaceFontsDispose() => BrowserFontBank.DisposeFonts();

    public void WorkspaceWake(Action wake) => BrowserActivity.Wake = wake;

    public float WorkspaceNextUpdateDelay() => WorkspaceDrawing.NextUpdateDelay();

    public int WorkspaceFrameLimit() => Settings.ExplicitFrameLimit;

    public void WorkspaceClearWake() => BrowserActivity.Wake = null;

    public bool WorkspacePacketCache(bool enabled) => BrowserGui.ConfigurePacketCache(enabled);

    public bool WorkspaceRowCommands(bool enabled) => BrowserGui.ConfigureTerminalRows(enabled);

    public string WorkspaceRowCommandsStats() => BrowserGui.TerminalRowStats();

    public string WorkspaceDynamicFonts(Action<string> changed)
    {
        BrowserPreferences.Fonts = changed;
        var settings = Settings.S;
        return BrowserPreferences.FontSelection(settings);
    }

    public void WorkspaceFontHold(string json)
    {
        var settings = Settings.S;
        BrowserPreferences.HoldFonts(settings, json);
        WorkspaceFontsReady();
    }

    public void WorkspaceFontRestore(string json)
    {
        var settings = Settings.S;
        BrowserPreferences.ApplyLocal(settings, json);
        WorkspaceFontsReady();
        BrowserPreferences.Write(settings);
    }

    public void WorkspaceGlyphsReady()
    {
        BrowserFontBank.GlyphsReady();
        BrowserGui.FontsChanged();
        TerminalFont.Invalidate();
        BrowserActivity.Notify();
    }

    public void WorkspaceFontsReady()
    {
        BrowserFontBank.RetryRasterization();
        BrowserGui.FontsChanged();
        InstalledFonts.Rescan();
        UiFont.Apply();
        TerminalFont.Invalidate();
        BrowserActivity.Notify();
    }

    public void WorkspaceFontCatalog(string json) => BrowserFontBank.InstallCatalog(json);

    public void WorkspaceFontChains(string json) => BrowserFontBank.InstallChains(json);

    public string WorkspaceFontSnapshot() => BrowserFontBank.Snapshot();

    public int WorkspaceFont(byte[] font, string bake, int defaultIndex) =>
        BrowserFontBank.Install(font, bake, defaultIndex);

    public string WorkspaceTitle() => WindowTitle.Current();

    public bool WorkspaceFullscreenKey(string key) => BrowserKeyBindings.FullscreenKey(key);

    public void WorkspaceWindow(Action<bool> request) => BrowserWindow.Request = request;

    public void WorkspaceOpenUrl(Action<string> request) => BrowserWindow.UrlRequest = request;

    public void WorkspaceFullscreenState(bool fullscreen) => Settings.S.fullscreen = fullscreen;

    public void WorkspacePreferences(string json, Action<string> persist)
    {
        var settings = Settings.S;
        BrowserPreferences.ApplyLocal(settings, json);
        BrowserPreferences.Persist = persist;
    }

    public void WorkspaceKeyBindings(string json, Action<string> persist)
    {
        BrowserKeyBindings.Apply(json);
        BrowserKeyBindings.Persist = persist;
    }

    public void WorkspaceSavePreferences()
    {
        var settings = Settings.S;
        BrowserPreferences.Write(settings);
    }

    public void ConfigureWorkspaceViewport(int width, int height, float scale) =>
        WorkspaceDrawing.ConfigureViewport(width, height, scale);

    public void WorkspaceProfile(bool enabled) => BrowserGuiInput.Profile = enabled;

    public string ClipboardTake() => BrowserClipboard.Take();

    public void ClipboardComplete(int id, string text, string error) =>
        BrowserClipboard.Complete(id, text, error);

    public void ClipboardCompleteImage(int id, string base64, string error) =>
        BrowserClipboard.CompleteImage(id, base64, error);

    public string WorkspaceState() => WorkspaceDrawing.Snapshot();

    public string WorkspaceAudio() => BrowserAudio.Instance.Snapshot();

    public void WorkspaceAudioIdentify(string name) => BrowserAudio.Instance.Identify(name);

    public void WorkspaceAudioReport(
        int revision,
        bool playing,
        string stream,
        string title,
        string error
    ) => BrowserAudio.Instance.Report(revision, playing, stream, title, error);

    public void WorkspaceAudioQuit() => Radio.Quit();

    public void WorkspaceAudioEnded(int revision) => BrowserAudio.Instance.Ended(revision);

    public bool WorkspaceBackgroundVisible() => BrowserUnderlyingWorkspace.BackgroundVisible();

    public float WorkspaceBackgroundDimming()
    {
        var settings = Settings.S;
        return BrowserPreferences.BackgroundDimming(settings);
    }

    public int InstallModContent(byte[] archive) => BrowserModContent.Install(archive);
}
