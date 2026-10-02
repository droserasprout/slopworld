using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class FilesView
    {
        // ------------------------------------------------------------------ drawing

        public static void Draw(Rect body, bool anchorBoundary = true)
        {
            if (!SmoothScroll.WheelOnly)
            {
                RefreshIfDue();
                GitView.RefreshIfDue();
                RefreshReadersIfDue();
            }
            Tree.Draw(body, anchorBoundary);
        }


        public static void Clicks() => Tree.Clicks();

        static void Open(Node node)
        {
            if (!ReaderScopeAvailable(node.Project)) return;
            GitView.CancelPendingDiff();
            Tree.Select(node);
            // Marked, and nobody showing it: whatever was in the pane is not about this row.
            if (!IsText(node.Name) && !IsImage(node.Name))
            {
                Viewers.ReleasePreview();
                ReleaseNativePreview();
                return;
            }
            if (IsMarkdown(node.Name) || IsImage(node.Name))
            {
                if (NativeViewers.Reopen(node.Project, node.Path)) return;
                Viewers.ReleasePreview();
                OpenNative(node.Project, node.Path, node.Name);
                return;
            }
            if (!Viewers.Reopen(node.Project, node.Path)) View(node);
        }

        // Both trees open readers in the shared pane without changing sidebar navigation.
        static void Act(Node node, RowAct act)
        {
            switch (act)
            {
                case RowAct.View:
                    Open(node);
                    break;

                case RowAct.Edit:
                    // Editors belong to Files, including when the row is from Git.
                    EditFile(node.Project, node.Path, "edit-" + node.Name);
                    break;

                case RowAct.Diff:
                    // Git supplies the command. The shared collection owns its reader.
                    GitView.OpenDiff(node.Project, node.Path, "diff-" + node.Name);
                    break;
            }
        }

        // ------------------------------------------------------------------ menu

        // `project` is non-null only for project headings, which also offer a host terminal.
        static List<FloatMenuOption> Menu(Node node, string project = null)
        {
            return MenuBuilder.Build(node, project);
        }

        static class MenuBuilder
        {
            public static List<FloatMenuOption> Build(Node node, string project = null)
            {
                var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => FilesView.Copy(node.Path)),
            };

                string rel = FilesView.Relative(node);
                if (rel != null)
                    opts.Add(new FloatMenuOption("Copy relative path", () => FilesView.Copy(rel)));

                if (project != null && SidebarScopes.Project(project) != null)
                    opts.Add(new FloatMenuOption("Terminal (host)", () =>
                        SessionHub.Instance.SessionStore.RunHostShell(project,
                        session => TerminalWindow.Open(session), UiLayout.Fail)));

                FilesView.AddFileActions(opts, node.Project, node.Path, node.Name,
                    FilesView.Relative(node));

                if (!node.IsDir && (FilesView.IsText(node.Name) || FilesView.IsImage(node.Name)))
                {
                    opts.Add(new FloatMenuOption("View", () => FilesView.View(node)));
                    if (FilesView.IsMarkdown(node.Name))
                        opts.Add(new FloatMenuOption("View in pager", () => FilesView.ViewSource(node)));
                    if (FilesView.IsText(node.Name))
                        opts.Add(new FloatMenuOption("Edit", () => FilesView.EditFile(node.Project, node.Path,
                            "edit-" + node.Name)));
                }

                if (!FilesView.IsRoot(node))
                {
                    opts.Add(UiMenu.Separator());
                    opts.Add(new FloatMenuOption("Rename", () => FilesView.Rename(node)));
                    opts.Add(new FloatMenuOption("Remove", () => FilesView.Remove(node)));
                }

                // The desktop MIME database resolves directory paths to file managers.
                FilesView.AddOpenIn(opts, node.Path, node.Project);

                if (node.IsDir)
                {
                    opts.Add(UiMenu.Separator());
                    opts.Add(new FloatMenuOption("New file", () => FilesView.Create(node, "file")));
                    opts.Add(new FloatMenuOption("New folder", () => FilesView.Create(node, "folder")));
                    opts.Add(new FloatMenuOption("Shell here", () => FilesView.ShellHere(node)));
                }

                return opts;
            }
        }

        // Host application selection is shared by every sidebar tree that names a path.
        // Keep the submenu here so Files and Git use the same asynchronous MIME lookup and
        // portal fallback.
        public static void AddOpenIn(List<FloatMenuOption> opts, string path, string project = null)
        {
            if (!SessionHub.Instance.Capabilities.DesktopOpen) return;
            opts.Add(new UiSubmenu("Open in", () => OpenInOptions(path, project)));
        }

        static List<FloatMenuOption> OpenInOptions(string path, string project)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Loading applications", null),
            };

            DaemonClient.Get<Wire.AppsReply>(WireProtocol.Routes.OpenApps + "?path=" + System.Uri.EscapeDataString(path), j =>
            {
                options.Clear();
                foreach (var app in j.Apps)
                {
                    string id = app.Id;
                    string name = app.Name;
                    // `gio launch` accepts a desktop-file path, not the ID printed by
                    // `gio mime`. The daemon resolves that path in the same XDG search roots
                    // it used to discover the application.
                    string desktopFile = app.DesktopFile;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(desktopFile)) continue;
                    string launchFile = desktopFile;
                    options.Add(new FloatMenuOption(name, () => OpenInApp(path, launchFile, project)));
                }

                if (options.Count == 0)
                    options.Add(new FloatMenuOption("No associated applications", null));
                options.Add(UiMenu.Separator());
                options.Add(new FloatMenuOption("Other", () => OpenInOther(path, project)));
            }, msg =>
            {
                options.Clear();
                UiLayout.Fail("Could not load applications: " + msg);
                options.Add(new FloatMenuOption("Could not load applications", null));
                options.Add(UiMenu.Separator());
                options.Add(new FloatMenuOption("Other", () => OpenInOther(path, project)));
            });
            return options;
        }

        static void OpenInApp(string path, string desktopFile, string project)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(desktopFile)) return;
            string command = PagerCommands.FileActionCommand(
                "gio launch " + Pager.Quote(desktopFile) + " {{ absolute_path }}", path, null);
            HostFileAction(path, command, project);
        }

        static void OpenInOther(string path, string project)
        {
            if (string.IsNullOrEmpty(path)) return;
            HostFileAction(path, NativeAppPicker.Command(path), project);
        }

        internal static Wire.FileActionReq ScopeAction(string scope, string path, string command, bool host = false) =>
            new Wire.FileActionReq { Project = SidebarScopes.ProjectName(scope) ?? "", Worktree = SidebarScopes.Worktree(scope),
                Path = path, Command = command, Host = host };

        static void HostFileAction(string path, string command, string project)
        {
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, ScopeAction(project, path, command, string.IsNullOrEmpty(project)), null, UiLayout.Fail);
        }

        // File actions run on the host. The project still scopes selected paths and supplies
        // the working directory. Private-state paths have no project.
        public static void AddFileActions(List<FloatMenuOption> opts, string project, string path,
            string name, string relative = null)
        {
            var actions = SessionHub.Instance.Library
                .Where(s => s.Kind == LibraryItemKind.FileAction)
                .ToList();
            if (actions.Count == 0) return;
            if (relative == null) relative = ProjectRelative(project, path);

            opts.Add(new UiSubmenu("File actions", () => FileActionOptions(
                project, path, name, relative, actions)));
        }

        static List<FloatMenuOption> FileActionOptions(string project, string path, string name,
            string relative, List<LibraryItemInfo> actions)
        {
            return actions.Select(action => new FloatMenuOption(action.Name, () =>
                RunFileAction(project, path, name, relative, action))).ToList();
        }

        static void RunFileAction(string project, string path, string name, string relative,
            LibraryItemInfo action)
        {
            bool host = string.IsNullOrEmpty(project);
            var command = PagerCommands.FileActionCommand(action.Command, path, relative);
            if (action.Mode == FileActionMode.Nothing)
            {
                RunFileActionSilently(project, path, command, host);
                return;
            }
            if (action.Mode == FileActionMode.ShowResult)
            {
                ShowFileActionResult(project, path, command, host, action.Name);
                return;
            }
            if (action.Mode == FileActionMode.OpenTerminal)
            {
                OpenFileActionTerminal(project, path, name, command, host);
                return;
            }

            TerminalWindow.OpenOverPane(new UiMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Show result", () =>
                    ShowFileActionResult(project, path, command, host, action.Name)),
                new FloatMenuOption("Open terminal", () =>
                    OpenFileActionTerminal(project, path, name, command, host)),
            }));
        }

        static void ShowFileActionResult(string project, string path, string command, bool host,
            string actionName)
        {
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, ScopeAction(project, path, command, host), j =>
                {
                    RefreshAfterFileAction();
                    string output = j.Output;
                    TerminalWindow.OpenOverPane(AlertDialog.Create(
                        "File action: " + actionName, output, "Close", null));
                }, msg =>
                {
                    RefreshAfterFileAction();
                    TerminalWindow.OpenOverPane(AlertDialog.Create(
                        "File action failed", msg, "Close", null,
                        primaryKind: UiTheme.Btn.Danger));
                });
        }

        static void RunFileActionSilently(string project, string path, string command, bool host)
        {
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, ScopeAction(project, path, command, host), _ => RefreshAfterFileAction(), msg =>
                {
                    RefreshAfterFileAction();
                    UiLayout.Fail("File action: " + msg);
                });
        }

        // File actions are available in both trees and can change Git's working-tree result.
        // Invalidate Files as well as Git so the action's source view and the other sidebar view
        // agree on the next draw.
        static void RefreshAfterFileAction()
        {
            Reload();
            GitView.Refresh();
        }

        static void OpenFileActionTerminal(string project, string path, string name,
            string command, bool host)
        {
            // A terminal action can keep running after this callback. Therefore, refresh as soon as
            // its session is launched rather than waiting for a completion that does not exist for
            // an interactive command.
            RefreshAfterFileAction();
            SessionHub.Instance.SessionStore.Run(project, command, "fa-" + name,
                session => TerminalWindow.Open(session), UiLayout.Fail, options: new SessionRunOptions
                {
                    Host = host,
                    Temp = host,
                    Path = path,
                    Hold = true,
                });
        }

        static string ProjectRelative(string project, string path)
        {
            string root = SidebarScopes.Directory(project);
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;
            if (root == "/")
                return path.StartsWith("/", StringComparison.Ordinal) && path.Length > 1 ? path.Substring(1) : null;

            root = root.TrimEnd('/');
            if (path == root) return null;
            return path.StartsWith(root + "/", StringComparison.Ordinal) ? path.Substring(root.Length + 1) : null;
        }

        static bool IsRoot(Node node) => node.Depth == 0;

        static void Rename(Node node) => FileNameDialog.Open(
            "Rename " + node.Name, node.Name, name =>
            {
                DaemonClient.Put(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path, Name = name },
                    _ =>
                    {
                        InvalidateReaders(node.Path);
                        RefreshAfterFileAction();
                    }, UiLayout.Fail);
            });

        static void Remove(Node node)
        {
            string what = node.IsDir ? "folder and everything inside it" : "file";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                $"Remove {what} '{node.Name}'?",
                () => DaemonClient.Delete(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path },
                    _ =>
                    {
                        InvalidateReaders(node.Path);
                        RefreshAfterFileAction();
                    }, UiLayout.Fail),
                destructive: true));
        }

        static void Create(Node node, string kind)
        {
            string fallback = kind == "folder" ? "new-folder" : "untitled";
            FileNameDialog.Open(kind == "folder" ? "New folder" : "New file", fallback, name =>
            {
                DaemonClient.Post(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path, Name = name, Kind = kind },
                    _ => RefreshAfterFileAction(), UiLayout.Fail);
            });
        }

        static void ShellHere(Node node)
        {
            // Host errands take argv: quote once for bash and again for the daemon splitter.
            // "Shell here" opens on the host, even for a sandboxed project.
            string script = "cd -- " + Pager.Quote(node.Path) +
                " && exec \"${SHELL:-bash}\"";
            string command = "bash -lc " + Pager.Quote(script);
            SessionHub.Instance.SessionStore.Run(node.Project ?? "", command,
                "shell-" + node.Name,
                session => TerminalWindow.Open(session), UiLayout.Fail, options: new SessionRunOptions
                {
                    Host = true,
                    Temp = string.IsNullOrEmpty(node.Project),
                    Path = node.Path,
                });
        }

        // Against the project's own directory. Null for the root itself, which has no relative
        // path worth the name, and for anything that somehow sits outside it.
        static string Relative(Node node)
        {
            string root = node.Root;
            if (string.IsNullOrEmpty(root)) return null;
            if (root == "/")
                return node.Path.StartsWith("/", StringComparison.Ordinal) && node.Path.Length > 1
                    ? node.Path.Substring(1) : null;
            root = root.TrimEnd('/');
            if (node.Path.Length <= root.Length + 1) return null;
            return node.Path.StartsWith(root + "/", StringComparison.Ordinal) ? node.Path.Substring(root.Length + 1) : null;
        }

        static void Copy(string text) => DaemonClipboard.Copy(text,
            () => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                false), UiLayout.Fail);

        // Run an independent host command in the project directory. Editors are opened
        // and left alone rather than sharing the viewer's preview lifetime.
        static void Errand(Node node, string cmd, string label)
        {
            // The project this hangs off may have been renamed or deleted since the listing
            // that put the row on screen. The daemon would refuse either way, but the reason
            // is clearer said here.
            if (SidebarScopes.Project(node.Project) == null)
            {
                UiLayout.Fail("This checkout's project is no longer available.");
                return;
            }

            SessionHub.Instance.SessionStore.Run(node.Project, cmd + " " + Pager.Quote(node.Path),
                label + "-" + node.Name,
                session => TerminalWindow.Open(session), UiLayout.Fail, options: new SessionRunOptions { Host = true });
        }

        // ------------------------------------------------------------------ viewer
        // Markdown and JPG/PNG files use native readers. Other text files use the pager. The explicit
        // source action below keeps raw Markdown available without changing the default preview.
        static void View(Node node) => ViewFile(node.Project, node.Path, "view-" + node.Name);

        static void ViewSource(Node node)
        {
            ReleaseNativePreview();
            ViewSourceFile(node.Project, node.Path, "view-" + node.Name);
        }

        // Public for GitView: viewing a changed file is a Files operation, regardless of
        // which tree supplied the click. Opening it preserves the active sidebar tab.
        public static void ViewFile(string project, string path, string label, int line = 0, string previewRoot = null)
        {
            if (!string.IsNullOrEmpty(project)) project = SidebarScopes.Key(project);
            GitView.CancelPendingDiff();
            if (!ReaderScopeAvailable(project)) return;
            label = ReaderLabel(project, label);
            Tree.SelectKey(ContentTreeView.SelectionKey(project, SidebarScopes.Relative(project, path)));
            if (!string.IsNullOrEmpty(project)) AgentSidebar.RememberFile(project, path);
            if (line > 0 && previewRoot == null)
            {
                ReleaseNativePreview();
                if (Viewers.ReuseFile(project, path)) return;
                Viewers.ForPreview().ViewFileAt(project, path, line, label);
                return;
            }
            string name = System.IO.Path.GetFileName(path);
            if (previewRoot != null || IsMarkdown(name) || IsImage(name))
            {
                if (previewRoot == null && NativeViewers.Reopen(project, path)) return;
                Viewers.ReleasePreview();
                OpenNative(project, path, name, previewRoot);
                return;
            }
            ReleaseNativePreview();
            if (Viewers.ReuseFile(project, path)) return;
            Viewers.ForPreview().ViewFile(project, path, label);
        }

        // Storage readers have no scope. Scoped readers must still refer to a ready checkout,
        // even when its parent project survives deletion of the worktree.
        static bool ReaderScopeAvailable(string project)
        {
            if (string.IsNullOrEmpty(project)) return true;
            SidebarScopes.Update();
            var scope = SidebarScopes.Find(SidebarScopes.Key(project));
            if (scope != null && scope.Ready) return true;
            ClearSelection();
            UiLayout.Fail("This reader's checkout is no longer available.");
            return false;
        }

        internal static string ReaderLabel(string scope, string label)
        {
            var origin = SidebarScopes.Find(scope);
            return origin == null ? label : label + " [" + origin.Project + " / " + origin.Label + "]";
        }

        static bool Showing(NativeTab tab) => tab != null &&
            ReferenceEquals(TerminalWindow.Showing, tab.View);

        static void OpenNative(string project, string path, string name, string previewRoot = null)
        {
            var tab = NativeViewers.ForPreview();
            tab.OriginLabel = ReaderLabel(project, "view-" + name);
            tab.OriginProject = SidebarScopes.ProjectName(project);
            tab.Project = project ?? "";
            tab.Path = path;
            tab.View = IsImage(name)
                ? (ContentView)new ImagePreview(path, name, previewRoot)
                : new MarkdownPreview(project, path, name, previewRoot, plainText: !IsMarkdown(name));
            tab.Header = "view-native-" + (++Viewer.NativeHeader);
            TerminalWindow.OpenContent(tab.View);
        }

        // Native previews have no daemon session to appear in the routed list.
        // Therefore, give it the same lightweight header identity as a pager tab.
        public static void AddRoutedPreviews(List<SessionInfo> result)
        {
            foreach (var tab in NativeViewers.All)
                if (AgentSidebar.Passes(tab.HeaderInfo().Project)) result.Add(tab.HeaderInfo());
        }

        public static bool OpenViewerHeader(string session)
        {
            return NativeViewers.ReopenSession(session);
        }

        public static bool IsNativeViewerHeader(string session)
        {
            return NativeViewers.ContainsSession(session);
        }

        static void ViewSourceFile(string project, string path, string label)
        {
            if (!ReaderScopeAvailable(project)) return;
            GitView.CancelPendingDiff();
            Tree.SelectKey(ContentTreeView.SelectionKey(project, SidebarScopes.Relative(project, path)));
            if (!string.IsNullOrEmpty(project)) AgentSidebar.RememberFile(project, path);
            if (Viewers.ReuseFile(project, path)) return;
            Viewers.ForPreview().ViewFile(project, path, ReaderLabel(project, label));
        }

        public static void EditFile(string project, string path, string label, int line = 0)
        {
            if (!string.IsNullOrEmpty(project)) project = SidebarScopes.Key(project);
            if (!ReaderScopeAvailable(project)) return;
            GitView.CancelPendingDiff();
            if (!string.IsNullOrEmpty(project)) AgentSidebar.RememberFile(project, path);
            if (string.IsNullOrEmpty(project))
            {
                SessionHub.Instance.SessionStore.Run("", Pager.EditorCommand(path, line), label,
                    session => TerminalWindow.Open(session), UiLayout.Fail, options: new SessionRunOptions
                    {
                        Host = true,
                        Temp = true,
                        Intent = "edit",
                        Reader = new ReaderLaunchOptions
                        {
                            Path = path,
                        },
                    });
                return;
            }
            if (SidebarScopes.Project(project) == null)
            {
                UiLayout.Fail("This reader's project is no longer available.");
                return;
            }
            SessionHub.Instance.SessionStore.Run(project, Pager.EditorCommand(path, line), label,
                session => TerminalWindow.Open(session), UiLayout.Fail, options: new SessionRunOptions
                {
                    Host = true,
                    Path = path,
                    Intent = "edit",
                    Reader = new ReaderLaunchOptions
                    {
                        Path = path,
                        Scope = project,
                    },
                });
        }

        public static string ViewerPath(string session) => Viewers.FilePath(session);

        static float _nextReaderCheck;
        static bool _checkingReaders;

        // Probe exact paths independently of tree filters, folds and listing caps. Serialize
        // these cheap requests so many pinned readers cannot starve foreground browsing.
        internal static void RefreshReadersIfDue()
        {
            if (_checkingReaders || Time.realtimeSinceStartup < _nextReaderCheck ||
                !SessionHub.Instance.Online) return;
            _nextReaderCheck = Time.realtimeSinceStartup + 5f;
            var readers = new Queue<IPreviewTab>();
            foreach (var tab in Viewers.All)
                if (!string.IsNullOrEmpty(tab.FilePath)) readers.Enqueue(tab);
            foreach (var tab in NativeViewers.All) readers.Enqueue(tab);
            _checkingReaders = true;
            CheckNextReader(readers);
        }

        static void CheckNextReader(Queue<IPreviewTab> readers)
        {
            if (readers.Count == 0) { _checkingReaders = false; return; }
            var probe = new ReaderProbe(readers.Dequeue());
            if (string.IsNullOrEmpty(probe.Path)) { CheckNextReader(readers); return; }
            DaemonClient.Get<Wire.FileStatResult>(WireProtocol.Routes.FileStat +
                "?path=" + System.Uri.EscapeDataString(probe.Path), result =>
                {
                    probe.Apply(result.IsFile, result.Stamp);
                    CheckNextReader(readers);
                }, _ => CheckNextReader(readers));
        }

        static void InvalidateReaders(string path)
        {
            System.Predicate<IPreviewTab> removed = tab => tab.FilePath == path ||
                (tab.FilePath != null && tab.FilePath.StartsWith(path.TrimEnd('/') + "/",
                    System.StringComparison.Ordinal));
            Viewers.Invalidate(tab => removed(tab));
            NativeViewers.Invalidate(tab => removed(tab));
        }

        public static void ReleaseViewer()
        {
            GitView.CancelPendingDiff();
            ClearSelection();
            Viewers.ReleasePreview();
            ReleaseNativePreview();
        }

        internal static void ReleaseNativePreview()
        {
            NativeViewers.ReleasePreview();
        }

        public static bool IsViewerSession(string session)
        {
            if (Viewers.IsSession(session)) return true;
            return NativeViewers.IsSession(session);
        }

        public static bool IsViewerLocked(string session)
        {
            if (Viewers.IsLocked(session)) return true;
            return NativeViewers.IsLocked(session);
        }

        public static bool LockViewer(string session)
        {
            if (Viewers.Lock(session)) return true;
            return NativeViewers.Lock(session);
        }

        public static bool LockViewerFile(string project, string path)
        {
            if (Viewers.LockPreview(project, path)) return true;
            return NativeViewers.LockPreview(project, path);
        }

        public static void CloseViewerIf(string session) => Viewers.CloseIf(session);

        public static bool CloseViewerTab(string session)
        {
            GitView.CancelPendingDiff();
            if (Viewers.CloseTab(session)) return true;
            if (NativeViewers.CloseTab(session)) return true;

            // PagerTabs is UI-lifetime state. A game restart leaves the daemon's ephemeral
            // pager/editor session alive but loses that owner. Therefore, close the restored routed
            // tab directly through the session store. Durable agents are deliberately excluded.
            var info = SessionHub.Instance.Get(session);
            if (info == null || !info.Ephemeral) return false;
            RowAct action = RowActions.Of(info);
            if ((action & (RowAct.View | RowAct.Edit)) == 0) return false;
            SessionHub.Instance.SessionStore.Stop(session);
            return true;
        }

        static void ClearSelection()
        {
            Tree.ClearSelection();
        }

    }
}
