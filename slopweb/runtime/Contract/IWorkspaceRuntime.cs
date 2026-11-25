namespace SlopWeb.Runtime;

public interface IWorkspaceRuntime
{
    void InputFrame(string batch, int height);
    string WorkspaceCursorTake();
    string SystemLanguageFolder();
    bool InitializeEco();
    void StartEntryEngine();
    bool UpdateEntryEngine(int frame);
    void Dispose();
    string EntryReport();
    string GameSnapshot();
    void InstallPlatform(
        string directory,
        Func<string> readLikes,
        Func<string?, string, Task> writeLikes
    );
    void WorkspaceLikesChanged();
    string WorkspaceFontFallbackTake();
    void WorkspaceFontFallbackComplete(string json);
    void WorkspaceFontFallbackRetry();
    void WorkspaceRasterizer(Func<string, string> rasterize);
    void WorkspaceFontsDispose();
    void WorkspaceWake(Action wake);
    float WorkspaceNextUpdateDelay();
    int WorkspaceFrameLimit();
    void WorkspaceClearWake();
    bool WorkspacePacketCache(bool enabled);
    bool WorkspaceRowCommands(bool enabled);
    string WorkspaceRowCommandsStats();
    string WorkspaceDynamicFonts(Action<string> changed);
    void WorkspaceFontHold(string json);
    void WorkspaceFontRestore(string json);
    void WorkspaceGlyphsReady();
    void WorkspaceFontsReady();
    void WorkspaceFontCatalog(string json);
    void WorkspaceFontChains(string json);
    string WorkspaceFontSnapshot();
    int WorkspaceFont(byte[] font, string bake, int defaultIndex);
    void WorkspaceAppearance(string json);
    string WorkspaceTitle();
    bool WorkspaceFullscreenKey(string key);
    void WorkspaceWindow(Action<bool> request);
    void WorkspaceOpenUrl(Action<string> request);
    void WorkspaceFullscreenState(bool fullscreen);
    void WorkspacePreferences(Func<string, string> read, Func<string, string, bool> persist);
    void WorkspaceKeyBindings(string json, Action<string> persist);
    void WorkspaceSavePreferences();
    string WorkspaceSelect(string name);
    void ConfigureWorkspaceViewport(int width, int height, float scale);
    void WorkspaceProfile(bool enabled);
    string ClipboardTake();
    void ClipboardComplete(int id, string text, string error);
    void ClipboardCompleteImage(int id, string base64, string error);
    string WorkspaceState();
    string WorkspaceAudio();
    void WorkspaceAudioInstall(string name, Func<string, bool> submitStop);
    void WorkspaceAudioReport(
        int revision,
        bool playing,
        string stream,
        string title,
        string error
    );
    void WorkspaceAudioQuit();
    void WorkspaceAudioEnded(int revision);
    bool WorkspaceBackgroundVisible();
    float WorkspaceBackgroundDimming();
    int InstallModContent(byte[] archive);
    string WorkspaceUpdatePacket(string batch, double elapsed, double retainedPacket);
    Task<string> WorkspaceFrame(string origin, string requestedSession);
}
