using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Stable entry points for sidebar, Git and terminal callers. Behavior belongs to the
    // browser, directory store, reader controller and file-action owner below.
    public static class FilesView
    {
        static readonly FilesBrowser Browser = new FilesBrowser();
        // Preserve lazy initialization before any entry point, including type-policy calls.
        static FilesView() { }

        public static bool AllFolded => Browser.Store.AllFolded;
        public static void SetAllFolded(bool folded) => Browser.Store.SetAllFolded(folded);
        internal static int TreeRevision => Browser.Store.Controller.Revision;
        public static void Draw(Rect body, bool anchorBoundary = true) => Browser.Draw(body, anchorBoundary);
        public static void Clicks() => Browser.Clicks();
        public static bool FocusPath(string project, string path) => Browser.FocusPath(project, path);
        public static bool FocusLocation(string project, string path) => Browser.FocusLocation(project, path);
        public static void FocusDirectory(string path, string label) => Browser.FocusDirectory(path, label);
        public static void ClearFocus() => Browser.Store.ClearFocus();
        public static void Reload() => Browser.Store.Reload();
        public static void Entered() => Browser.Store.Entered();
        public static string ResolveProjectPath(string project, string path, string cwd = null) =>
            FilesBrowser.ResolveProjectPath(project, path, cwd);

        public static bool IsText(string name) => FileTypes.IsText(name);
        public static bool IsMarkdown(string name) => FileTypes.IsMarkdown(name);
        public static bool IsImage(string name) => FileTypes.IsImage(name);

        public static void ViewFile(string project, string path, string label, int line = 0, string previewRoot = null) =>
            Browser.Viewer.ViewFile(project, path, label, line, previewRoot);
        public static void EditFile(string project, string path, string label, int line = 0) =>
            Browser.Viewer.EditFile(project, path, label, line);
        internal static string ReaderLabel(string scope, string label) => FilesViewerController.ReaderLabel(scope, label);
        public static void AddRoutedPreviews(List<SessionInfo> result) => Browser.Viewer.AddRoutedPreviews(result);
        public static bool OpenViewerHeader(string session) => Browser.Viewer.OpenViewerHeader(session);
        public static bool IsNativeViewerHeader(string session) => Browser.Viewer.IsNativeViewerHeader(session);
        public static string ViewerPath(string session) => Browser.Viewer.ViewerPath(session);
        internal static void RefreshReadersIfDue() => Browser.Viewer.RefreshReadersIfDue();
        public static void ReleaseViewer() => Browser.Viewer.ReleaseViewer();
        internal static void ReleaseNativePreview() => Browser.Viewer.ReleaseNativePreview();
        public static bool IsViewerSession(string session) => Browser.Viewer.IsViewerSession(session);
        public static bool IsViewerLocked(string session) => Browser.Viewer.IsViewerLocked(session);
        public static bool LockViewer(string session) => Browser.Viewer.LockViewer(session);
        public static bool LockViewerFile(string project, string path) => Browser.Viewer.LockViewerFile(project, path);
        public static void CloseViewerIf(string session) => Browser.Viewer.CloseViewerIf(session);
        public static bool CloseViewerTab(string session) => Browser.Viewer.CloseViewerTab(session);

        public static void AddOpenIn(List<FloatMenuOption> opts, string path, string project = null) =>
            Browser.Actions.AddOpenIn(opts, path, project);
        public static void AddFileActions(List<FloatMenuOption> opts, string project, string path,
            string name, string relative = null) => Browser.Actions.AddFileActions(opts, project, path, name, relative);
        internal static Wire.FileActionReq ScopeAction(string scope, string path, string command, bool host = false) =>
            FilesActions.ScopeAction(scope, path, command, host);
    }

}
