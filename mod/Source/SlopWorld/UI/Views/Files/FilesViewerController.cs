using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Owns native reader tabs, preview handoffs, editor launch and serialized reader probes.
    // FileReaders owns shared pagers; each tab rejects probes for replaced reader identities.
    sealed class FilesViewerController
    {
        readonly Action<string> _select;
        readonly Action _clearSelection;
        readonly PreviewTabs<NativeFileTab> _nativeViewers = new PreviewTabs<NativeFileTab>(() =>
            new NativeFileTab()
        );
        int _nativeHeader;
        float _nextReaderCheck;
        bool _checkingReaders;

        // FileReaders retains the shared pager collection used by Git as well.
        static PagerTabs Viewers => FileReaders.Tabs;

        public FilesViewerController(Action<string> select, Action clearSelection)
        {
            _select = select;
            _clearSelection = clearSelection;
        }

        public void Open(FileNode node)
        {
            if (!ReaderScopeAvailable(node.Project))
                return;
            GitView.CancelPendingDiff();
            _select(ContentTreeView.SelectionKey(node.Project, node.Relative ?? ""));
            // Marked, and nobody showing it: whatever was in the pane is not about this row.
            if (!FileTypes.IsText(node.Name) && !FileTypes.IsImage(node.Name))
            {
                Viewers.ReleasePreview();
                ReleaseNativePreview();
                return;
            }
            Sidebar.RevealOpenFiles();
            if (FileTypes.IsMarkdown(node.Name) || FileTypes.IsImage(node.Name))
            {
                if (_nativeViewers.Reopen(node.Project, node.Path))
                {
                    Viewers.ReleasePreview();
                    return;
                }
                OpenNative(node.Project, node.Path, node.Name);
                return;
            }
            if (!Viewers.Reopen(node.Project, node.Path))
                View(node);
        }

        // Markdown and JPG/PNG files use native readers. Other text files use the pager. The explicit
        // source action below keeps raw Markdown available without changing the default preview.
        public void View(FileNode node) => ViewFile(node.Project, node.Path, "view-" + node.Name);

        public void ViewSource(FileNode node) =>
            ViewSourceFile(node.Project, node.Path, "view-" + node.Name);

        // Files and Git share source readers. Opening one preserves the active sidebar tab.
        public void ViewFile(
            string project,
            string path,
            string label,
            int line = 0,
            string previewRoot = null
        )
        {
            if (!string.IsNullOrEmpty(project))
                project = SidebarScopes.Key(project);
            GitView.CancelPendingDiff();
            if (!ReaderScopeAvailable(project))
                return;
            Sidebar.RevealOpenFiles();
            label = ReaderLabel(project, label);
            _select(ContentTreeView.SelectionKey(project, SidebarScopes.Relative(project, path)));
            if (!string.IsNullOrEmpty(project))
                Sidebar.RememberFile(project, path);
            string name = System.IO.Path.GetFileName(path);
            if (FileTypes.UseNativePreview(name, line, previewRoot))
            {
                if (previewRoot == null && _nativeViewers.Reopen(project, path))
                {
                    Viewers.ReleasePreview();
                    return;
                }
                OpenNative(project, path, name, previewRoot);
                return;
            }
            if (line > 0)
            {
                if (Viewers.ReuseFile(project, path))
                    return;
                Viewers.ForPreview().ViewFileAt(project, path, line, label);
                return;
            }
            if (Viewers.ReuseFile(project, path))
                return;
            Viewers.ForPreview().ViewFile(project, path, label);
        }

        // Storage readers have no scope. Scoped readers must still refer to a ready checkout,
        // even when its parent project survives deletion of the worktree.
        bool ReaderScopeAvailable(string project)
        {
            if (string.IsNullOrEmpty(project))
                return true;
            SidebarScopes.Update();
            var scope = SidebarScopes.Find(SidebarScopes.Key(project));
            if (scope != null && scope.Ready)
                return true;
            _clearSelection();
            UiLayout.Fail("This reader's checkout is no longer available.");
            return false;
        }

        internal static string ReaderLabel(string scope, string label)
        {
            var origin = SidebarScopes.Find(scope);
            return origin == null
                ? label
                : label + " [" + origin.Project + " / " + origin.Label + "]";
        }

        void OpenNative(
            string project,
            string path,
            string name,
            string previewRoot = null,
            bool plainText = false
        )
        {
            var presentation = ReaderRow.NativeKind(name, plainText);
            ContentView view =
                presentation == ReaderPresentation.Image
                    ? (ContentView)new ImagePreview(path, name, previewRoot)
                    : new MarkdownPreview(
                        project,
                        path,
                        name,
                        previewRoot,
                        plainText: presentation == ReaderPresentation.Source
                    );
            (view as MarkdownPreview)?.RetainVisibleDocument(
                TerminalWindow.ShowingAs<MarkdownPreview>()
            );
            var tab = _nativeViewers.ForPreview();
            tab.OriginLabel = ReaderLabel(project, "view-" + name);
            tab.OriginProject = SidebarScopes.ProjectName(project);
            tab.Project = project ?? "";
            tab.Path = path;
            tab.PreviewRoot = previewRoot;
            tab.Presentation = presentation;
            tab.View = view;
            tab.Header = "view-native-" + (++_nativeHeader);
            TerminalWindow.OpenContent(tab.View);
            // Publish the native replacement before retiring the pager. This also
            // cancels an in-flight pager when the user switches back to native content.
            Viewers.ReleasePreview();
        }

        // Native previews have no daemon session to appear in the routed list.
        // Therefore, give it the same lightweight header identity as a pager tab.
        public void AddRoutedPreviews(List<SessionInfo> result)
        {
            foreach (var tab in _nativeViewers.All)
                if (Sidebar.Passes(tab.HeaderInfo().Project))
                    result.Add(tab.HeaderInfo());
        }

        public bool OpenViewerHeader(string session)
        {
            if (!_nativeViewers.ReopenSession(session))
                return false;
            Viewers.ReleasePreview();
            return true;
        }

        internal string CurrentViewerHeader
        {
            get
            {
                foreach (var tab in _nativeViewers.All)
                    if (ReferenceEquals(tab.View, TerminalWindow.Showing))
                        return tab.Header;
                return null;
            }
        }

        public bool IsNativeViewerHeader(string session)
        {
            return _nativeViewers.ContainsSession(session);
        }

        internal bool HasNativeReaders => _nativeViewers.All.Any();

        internal void ViewSourceFile(
            string project,
            string path,
            string label,
            string previewRoot = null
        )
        {
            if (!ReaderScopeAvailable(project))
                return;
            Sidebar.RevealOpenFiles();
            GitView.CancelPendingDiff();
            // Linked Markdown sources keep their captured read boundary, including when
            // explicitly switching from rendered content to source.
            if (previewRoot != null)
            {
                OpenNative(
                    project,
                    path,
                    System.IO.Path.GetFileName(path),
                    previewRoot,
                    plainText: true
                );
                return;
            }
            _select(ContentTreeView.SelectionKey(project, SidebarScopes.Relative(project, path)));
            if (!string.IsNullOrEmpty(project))
                Sidebar.RememberFile(project, path);
            if (Viewers.ReuseFile(project, path))
                return;
            Viewers.ForPreview().ViewFile(project, path, ReaderLabel(project, label));
        }

        public void EditFile(string project, string path, string label, int line = 0)
        {
            if (!string.IsNullOrEmpty(project))
                project = SidebarScopes.Key(project);
            if (!ReaderScopeAvailable(project))
                return;
            Sidebar.RevealOpenFiles();
            GitView.CancelPendingDiff();
            if (!string.IsNullOrEmpty(project))
                Sidebar.RememberFile(project, path);
            if (string.IsNullOrEmpty(project))
            {
                SessionHub.Instance.SessionStore.Run(
                    "",
                    Pager.EditorCommand(path, line),
                    label,
                    session => TerminalWindow.Open(session),
                    UiLayout.Fail,
                    options: new SessionRunOptions
                    {
                        Host = true,
                        Temp = true,
                        Intent = "edit",
                        Reader = new ReaderLaunchOptions { Path = path },
                    }
                );
                return;
            }
            if (SidebarScopes.Project(project) == null)
            {
                UiLayout.Fail("This reader's project is no longer available.");
                return;
            }
            SessionHub.Instance.SessionStore.Run(
                project,
                Pager.EditorCommand(path, line),
                label,
                session => TerminalWindow.Open(session),
                UiLayout.Fail,
                options: new SessionRunOptions
                {
                    Host = true,
                    Path = path,
                    Intent = "edit",
                    Reader = new ReaderLaunchOptions { Path = path, Scope = project },
                }
            );
        }

        public string ViewerPath(string session) => Viewers.FilePath(session);

        // Probe exact paths independently of tree filters, folds and listing caps. Serialize
        // these cheap requests so many pinned readers cannot starve foreground browsing.
        internal void RefreshReadersIfDue()
        {
            if (
                _checkingReaders
                || Time.realtimeSinceStartup < _nextReaderCheck
                || !SessionHub.Instance.Online
            )
                return;
            _nextReaderCheck = Time.realtimeSinceStartup + 5f;
            var readers = new Queue<IPreviewTab>();
            foreach (var tab in Viewers.All)
                if (!string.IsNullOrEmpty(tab.FilePath))
                    readers.Enqueue(tab);
            foreach (var tab in _nativeViewers.All)
                readers.Enqueue(tab);
            _checkingReaders = true;
            CheckNextReader(readers);
        }

        void CheckNextReader(Queue<IPreviewTab> readers)
        {
            if (readers.Count == 0)
            {
                _checkingReaders = false;
                return;
            }
            var probe = new ReaderProbe(readers.Dequeue());
            if (string.IsNullOrEmpty(probe.Path))
            {
                CheckNextReader(readers);
                return;
            }
            DaemonClient.Get<Wire.FileStatResult>(
                WireProtocol.Routes.FileStat + "?path=" + System.Uri.EscapeDataString(probe.Path),
                result =>
                {
                    probe.Apply(result.IsFile, result.Stamp);
                    CheckNextReader(readers);
                },
                _ => CheckNextReader(readers)
            );
        }

        public void InvalidateReaders(string path)
        {
            System.Predicate<IPreviewTab> removed = tab =>
                tab.FilePath == path
                || (
                    tab.FilePath != null
                    && tab.FilePath.StartsWith(
                        path.TrimEnd('/') + "/",
                        System.StringComparison.Ordinal
                    )
                );
            Viewers.Invalidate(tab => removed(tab));
            _nativeViewers.Invalidate(tab => removed(tab));
        }

        public void ReleaseViewer()
        {
            GitView.CancelPendingDiff();
            _clearSelection();
            Viewers.ReleasePreview();
            ReleaseNativePreview();
        }

        internal void ReleaseNativePreview()
        {
            _nativeViewers.ReleasePreview();
        }

        internal Action PrepareNativeReplacement() => _nativeViewers.Preview.PrepareReplacement();

        public bool LockViewerFile(string project, string path)
        {
            if (Viewers.LockPreview(project, path))
                return true;
            return _nativeViewers.LockPreview(project, path);
        }

        public void CloseViewerIf(string session) => Viewers.CloseIf(session);

        internal IPreviewTab FindReader(string session) =>
            (IPreviewTab)Viewers.FindSession(session) ?? _nativeViewers.FindSession(session);
    }
}
