using SlopWorld;

namespace BrowserAdapters;

// Install services after private assemblies load and before the first client is constructed.
public static class BrowserPlatform
{
    public static void Install(string directory)
    {
        BrowserTransport.Configure(directory);
        var bridge = new ConnectionInfo(new Uri(BrowserTransport.BaseUrl), "");
        Endpoint.Provider = () => bridge;
        DaemonHttpTransport.CreateClient = BrowserTransport.CreateHttp;
        HubTransport.CreateSocket = () => new BrowserSocket();
        HubTransport.ScheduleConnect = action => action();
        HubTransport.ResultReady = BrowserActivity.Notify;
        ClientPlatform.IsBrowser = true;
        TerminalIO.AllowResize = BrowserTerminalSize.Allow;
        DesktopClipboard.ReadLocalPasteHandler = BrowserClipboard.ReadLocalPaste;
        DesktopClipboard.ReadLocalHandler = BrowserClipboard.ReadLocal;
        DesktopClipboard.CopyHandler = BrowserClipboard.Copy;
        DesktopClipboard.CopyPrimaryHandler = BrowserClipboard.CopyPrimary;
        DesktopClipboard.CopyFromApplicationHandler = BrowserClipboard.CopyFromApplication;
        UiClipboard.Provider = new DesktopUiClipboard();
        BrowserJukebox.Current = BrowserAudio.Instance;
        DesktopClipboard.SupportsSelections = () => BrowserClipboard.UsesLinuxSelections;
        LinuxGameWindow.SetFullscreen = BrowserWindow.Set;
        TextureReadback.ReadPixels = BrowserCursor.ReadBack;
        FontconfigFonts.FindFamilies = BrowserFontBank.Families;
        NativeFonts.CatalogNames = mono =>
            BrowserFontBank.HasCatalog() ? BrowserFontBank.Names(mono) : null!;
        CursorTextures.Present = BrowserCursor.SetCursor;
        ProfileSettings.Save = BrowserPreferences.Changed;
        ModSettings.Changed = BrowserPreferences.Changed;
        ColonistBarStrip.MaplessWorkspace = true;
        SidebarRowRenderer.HomeFolder = BrowserSidebar.HomeFolder;
        TopBar.CoreAvailable = BrowserWorkspaceDoors.Available;
        TopBar.JukeboxAvailable = BrowserWorkspaceDoors.Available;
        TopBar.OpenCore = BrowserWorkspaceDoors.OpenCore;
    }
}
