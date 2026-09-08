using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // FilesView rendering, interaction, actions, and viewer lifecycle.
    public static partial class FilesView
    {
        // ------------------------------------------------------------------ drawing

        public static void Draw(Rect body)
        {
            RefreshIfDue();
            Tree.Draw(body);
        }


        public static void Clicks() => Tree.Clicks(ReleaseViewerForTree);

        // Left on a file: mark it and read it. Text files open in `less` in a pane over the
        // tree; a binary file is marked but nobody is handed it. Clicking the file already
        // being read just brings its pane back - a focus change is a *different* file, and
        // only that replaces the viewer.
        static void Open(Node node)
        {
            bool same = Tree.IsSelected(node) && _showing == RowAct.View;
            Tree.Select(node);
            // Marked, and nobody showing it: whatever was in the pane is not about this row.
            if (!IsText(node.Name))
            {
                _showing = RowAct.None;
                Viewers.ReleasePreview();
                ReleaseMarkdownPreview();
                return;
            }
            if (IsMarkdown(node.Name))
            {
                if (same && ReopenMarkdown(node.Project, node.Path)) return;
                Viewers.ReleasePreview();
                _showing = RowAct.View;
                OpenMarkdown(node.Project, node.Path, node.Name);
                return;
            }
            if (!same || !Viewers.Reopen(node.Project, node.Path)) View(node);
        }

        // One of the hover strip's three, done. Nothing here is new: the same three errands
        // the right-click menu has always offered, plus the git view's diff for a file that
        // has one - and that one is opened in *this* view's pager, because the reader is
        // standing in this view and Show would release the other's the moment it opened.
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
                    // A diff belongs to Git, even when its button was clicked in Files. Move
                    // first so Show does not release the pager we are about to open, then let
                    // GitView own the session and its lifecycle.
                    AgentSidebar.ShowGit();
                    GitView.OpenDiff(node.Project, node.Path, "diff-" + node.Name);
                    break;
            }
        }

        // ------------------------------------------------------------------ menu

        // `project` is set on a heading and null on everything below it: a project's own menu
        // is this one plus what the agents view's heading offers, since the two headings name
        // the same thing and a reader who found the option in one view will look for it in
        // the other.
        static List<FloatMenuOption> Menu(Node node, string project = null)
        {
            return MenuBuilder.Build(node, project);
        }

        // Menu construction is a separate concern from tree state. It consumes a node
        // snapshot and points actions back at FilesView for the lifecycle-sensitive work.
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

                if (project != null && SessionHub.Instance.Project(project) != null)
                    opts.Add(new FloatMenuOption("Terminal (host)", () =>
                        SessionHub.Instance.RunHostShell(project,
                        session => TerminalWindow.Open(session), UiWidgets.Fail)));

                FilesView.AddFileActions(opts, node.Project, node.Path, node.Name,
                    FilesView.Relative(node));

                // Only files, and only because a directory in `less` is a listing nobody asked
                // for and a directory in `micro` is a file browser inside a game. The left
                // button views a text file; the menu's View routes through the same tracked
                // viewer so it is replaced or closed like any other.
                if (!node.IsDir && FilesView.IsText(node.Name))
                {
                    opts.Add(new FloatMenuOption("View", () => FilesView.View(node)));
                    if (FilesView.IsMarkdown(node.Name))
                        opts.Add(new FloatMenuOption("View in pager", () => FilesView.ViewSource(node)));
                    opts.Add(new FloatMenuOption("Edit", () => FilesView.EditFile(node.Project, node.Path,
                        "edit-" + node.Name)));
                }

                if (!FilesView.IsRoot(node))
                {
                    opts.Add(UiMenu.Separator());
                    opts.Add(new FloatMenuOption("Rename", () => FilesView.Rename(node)));
                    opts.Add(new FloatMenuOption("Remove", () => FilesView.Remove(node)));
                }

                // Files and directories use the same host application picker. The desktop MIME
                // database knows that a directory is an inode/directory and returns file
                // managers, while a regular file returns its associated editors/viewers.
                FilesView.AddOpenIn(opts, node.Path);

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
        public static void AddOpenIn(List<FloatMenuOption> opts, string path)
        {
            if (!SessionHub.Instance.Capabilities.DesktopOpen) return;
            opts.Add(new UiSubmenu("Open in...", () => OpenInOptions(path)));
        }

        static List<FloatMenuOption> OpenInOptions(string path)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Loading applications...", null),
            };

            DaemonClient.Get(WireContract.Routes.OpenApps + "?path=" + System.Uri.EscapeDataString(path), j =>
            {
                options.Clear();
                foreach (var app in j["apps"].Items)
                {
                    string id = app["id"].AsString();
                    string name = app["name"].AsString(id);
                    // `gio launch` accepts a desktop-file path, not the ID printed by
                    // `gio mime`. The daemon resolves that path in the same XDG search roots
                    // it used to discover the application.
                    string desktopFile = app["desktop_file"].AsString();
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(desktopFile)) continue;
                    string launchFile = desktopFile;
                    options.Add(new FloatMenuOption(name, () => OpenInApp(path, launchFile)));
                }

                if (options.Count == 0)
                    options.Add(new FloatMenuOption("No associated applications", null));
                options.Add(UiMenu.Separator());
                options.Add(new FloatMenuOption("Other...", () => OpenInOther(path)));
            }, msg =>
            {
                options.Clear();
                UiWidgets.Fail("Open in: " + msg);
                options.Add(new FloatMenuOption("Could not load applications", null));
                options.Add(UiMenu.Separator());
                options.Add(new FloatMenuOption("Other...", () => OpenInOther(path)));
            });
            return options;
        }

        static void OpenInApp(string path, string desktopFile)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(desktopFile)) return;
            string command = PagerCommands.FileActionCommand(
                "gio launch " + Pager.Quote(desktopFile) + " {{ absolute_path }}", path, null);
            HostFileAction(path, command);
        }

        static void OpenInOther(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            // OpenFile with ask=true is the desktop portal's native "choose an application"
            // gdbus' `3<FILE` form is shell redirection that opens fd 3; the daemon normally
            // launches argv directly, so put only this command behind bash. The nested quotes
            // keep spaces and shell characters in the selected path intact.
            string script =
                "gdbus call --session --dest org.freedesktop.portal.Desktop " +
                "--object-path /org/freedesktop/portal/desktop " +
                "--method org.freedesktop.portal.OpenURI.OpenFile " +
                Pager.Quote("") + " 3 " + Pager.Quote("{'ask': <true>}") +
                " 3<" + Pager.Quote(path);
            string command = "bash -lc " + Pager.Quote(script);
            HostFileAction(path, command);
        }

        static void HostFileAction(string path, string command)
        {
            DaemonClient.Post(WireContract.Routes.FileAction, "{" +
                $"\"path\":{JVal.Q(path)}," +
                $"\"command\":{JVal.Q(command)}," +
                "\"host\":true" +
                "}", null, UiWidgets.Fail);
        }

        // File actions can come from any sidebar tree. Project paths run in that project's
        // sandbox; a focused private-state path has no project and uses a disposable host
        // errand, like its viewer and editor.
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
            DaemonClient.Post(WireContract.Routes.FileAction, "{" +
                $"\"project\":{JVal.Q(project)}," +
                $"\"path\":{JVal.Q(path)}," +
                $"\"command\":{JVal.Q(command)}," +
                $"\"host\":{JVal.B(host)}" +
                "}", j =>
                {
                    RefreshAfterFileAction();
                    string output = j["output"].AsString("(no output)");
                    TerminalWindow.OpenOverPane(AlertDialog.Create(
                        "File action: " + actionName, output, "Close", null));
                }, msg =>
                {
                    RefreshAfterFileAction();
                    TerminalWindow.OpenOverPane(AlertDialog.Create(
                        "File action failed", msg, "Close", null,
                        primaryKind: UiWidgets.Btn.Danger));
                });
        }

        static void RunFileActionSilently(string project, string path, string command, bool host)
        {
            DaemonClient.Post(WireContract.Routes.FileAction, "{" +
                $"\"project\":{JVal.Q(project)}," +
                $"\"path\":{JVal.Q(path)}," +
                $"\"command\":{JVal.Q(command)}," +
                $"\"host\":{JVal.B(host)}" +
                "}", _ => RefreshAfterFileAction(), msg =>
                {
                    RefreshAfterFileAction();
                    UiWidgets.Fail("File action: " + msg);
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
            // A terminal action can keep running after this callback, so refresh as soon as
            // its session is launched rather than waiting for a completion that does not
            // exist for an interactive command.
            RefreshAfterFileAction();
            SessionHub.Instance.Run(project, command, "fa-" + name,
                session => TerminalWindow.Open(session), UiWidgets.Fail,
                host: host, temp: host, path: path, hold: true);
        }

        static string ProjectRelative(string project, string path)
        {
            string root = SessionHub.Instance.Project(project)?.Dir;
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;
            if (root == "/")
                return path.StartsWith("/") && path.Length > 1 ? path.Substring(1) : null;

            root = root.TrimEnd('/');
            if (path == root) return null;
            return path.StartsWith(root + "/") ? path.Substring(root.Length + 1) : null;
        }

        static bool IsRoot(Node node) => node.Depth == 0;

        static void Rename(Node node) => FileNameDialog.Open(
            "Rename " + node.Name, node.Name, name =>
            {
                DaemonClient.Put(WireContract.Routes.Files,
                    "{" + $"\"path\":{JVal.Q(node.Path)},\"name\":{JVal.Q(name)}" + "}",
                    _ => Reload(), UiWidgets.Fail);
            });

        static void Remove(Node node)
        {
            string what = node.IsDir ? "folder and everything inside it" : "file";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                $"Remove {what} '{node.Name}'?",
                () => DaemonClient.Delete(WireContract.Routes.Files,
                    "{\"path\":" + JVal.Q(node.Path) + "}",
                    _ => Reload(), UiWidgets.Fail),
                destructive: true));
        }

        static void Create(Node node, string kind)
        {
            string fallback = kind == "folder" ? "new-folder" : "untitled";
            FileNameDialog.Open(kind == "folder" ? "New folder" : "New file", fallback, name =>
            {
                DaemonClient.Post(WireContract.Routes.Files,
                    "{" + $"\"path\":{JVal.Q(node.Path)},\"name\":{JVal.Q(name)}," +
                    $"\"kind\":{JVal.Q(kind)}" + "}",
                    _ => Reload(), UiWidgets.Fail);
            });
        }

        static void ShellHere(Node node)
        {
            // Errands are argv, not shell command lines. Invoke bash explicitly so the
            // directory change and the final interactive shell happen in one process, while
            // keeping the selected path quoted for both the daemon splitter and bash itself.
            // This is intentionally a host errand: the Files tree can show a project path,
            // but "Shell here" is for the host filesystem rather than that project's sandbox.
            string script = "cd -- " + Pager.Quote(node.Path) +
                " && exec \"${SHELL:-bash}\"";
            string command = "bash -lc " + Pager.Quote(script);
            SessionHub.Instance.Run("", command,
                "shell-" + node.Name,
                session => TerminalWindow.Open(session), UiWidgets.Fail,
                host: true, temp: true);
        }

        // Against the project's own directory. Null for the root itself, which has no relative
        // path worth the name, and for anything that somehow sits outside it.
        static string Relative(Node node)
        {
            string root = node.Root;
            if (string.IsNullOrEmpty(root)) return null;
            if (root == "/")
                return node.Path.StartsWith("/") && node.Path.Length > 1
                    ? node.Path.Substring(1) : null;
            root = root.TrimEnd('/');
            if (node.Path.Length <= root.Length + 1) return null;
            return node.Path.StartsWith(root + "/") ? node.Path.Substring(root.Length + 1) : null;
        }

        static void Copy(string text) => DaemonClipboard.Copy(text,
            () => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                false), UiWidgets.Fail);

        // A temporary agent running one command in the project's own sandbox, which is what
        // makes `micro` see the file the way the agents working on it do. Untracked, unlike
        // the viewer below: an editor is opened and left alone.
        static void Errand(Node node, string cmd, string label)
        {
            // The project this hangs off may have been renamed or deleted since the listing
            // that put the row on screen; the daemon would refuse either way, but the reason
            // is clearer said here.
            if (SessionHub.Instance.Project(node.Project) == null)
            {
                UiWidgets.Fail($"project '{node.Project}' has gone");
                return;
            }

            SessionHub.Instance.Run(node.Project, cmd + " " + Pager.Quote(node.Path),
                label + "-" + node.Name,
                session => TerminalWindow.Open(session), UiWidgets.Fail);
        }

        // ------------------------------------------------------------------ viewer
        // Markdown files use the native reader; other text files use the pager. The explicit
        // source action below keeps raw Markdown available without changing the default preview.
        static void View(Node node) => ViewFile(node.Project, node.Path, "view-" + node.Name);

        static void ViewSource(Node node)
        {
            ReleaseMarkdownPreview();
            ViewSourceFile(node.Project, node.Path, "view-" + node.Name);
        }

        // Public for GitView: viewing a changed file is a Files operation, regardless of
        // which tree supplied the click. The shared tab switch is done by the caller.
        public static void ViewFile(string project, string path, string label)
        {
            // A project that has gone takes the mark with it: the tree would otherwise
            // highlight a row nobody is reading.
            if (!string.IsNullOrEmpty(project) && SessionHub.Instance.Project(project) == null)
                ClearSelection();
            else
            {
                Tree.SelectKey(ContentTreeView.SelectionKey(project, path));
                _showing = RowAct.View;
            }
            if (IsMarkdown(System.IO.Path.GetFileName(path)))
            {
                if (ReopenMarkdown(project, path)) return;
                Viewers.ReleasePreview();
                OpenMarkdown(project, path, System.IO.Path.GetFileName(path));
                return;
            }
            ReleaseMarkdownPreview();
            Viewers.ForPreview().ViewFile(project, path, label);
        }

        static bool Showing(MarkdownTab tab) => tab != null &&
            ReferenceEquals(TerminalWindow.ShowingAs<MarkdownPreview>(), tab.View);

        static void OpenMarkdown(string project, string path, string name)
        {
            ReleaseMarkdownPreview();
            _markdownPreview = new MarkdownTab
            {
                View = new MarkdownPreview(project, path, name),
                Header = "view-markdown-" + (++_markdownHeader),
            };
            ShowMarkdown(_markdownPreview);
        }

        static void ShowMarkdown(MarkdownTab tab)
        {
            _activeMarkdown = tab;
            TerminalWindow.OpenContent(tab.View);
        }

        static bool ReopenMarkdown(string project, string path)
        {
            if (_markdownPreview != null && _markdownPreview.View.Project == (project ?? "") &&
                _markdownPreview.View.Path == path &&
                (_markdownPreview.Locked || Showing(_markdownPreview)))
            {
                ShowMarkdown(_markdownPreview);
                return true;
            }

            for (int i = 0; i < LockedMarkdown.Count; i++)
            {
                var tab = LockedMarkdown[i];
                if (tab.View.Project != (project ?? "") || tab.View.Path != path) continue;
                ShowMarkdown(tab);
                return true;
            }
            return false;
        }

        // The native Markdown preview has no daemon session to appear in the routed list, so
        // give it the same lightweight header identity as a pager tab.
        public static void AddRoutedPreviews(List<SessionInfo> result)
        {
            if (_markdownPreview != null &&
                (_markdownPreview.Locked || Showing(_markdownPreview)) &&
                AgentSidebar.Passes(_markdownPreview.View.Project))
                result.Add(_markdownPreview.HeaderInfo());

            foreach (var tab in LockedMarkdown)
                if (AgentSidebar.Passes(tab.View.Project)) result.Add(tab.HeaderInfo());
        }

        public static bool OpenViewerHeader(string session)
        {
            if (_markdownPreview != null && _markdownPreview.Header == session)
            {
                ShowMarkdown(_markdownPreview);
                return true;
            }

            for (int i = 0; i < LockedMarkdown.Count; i++)
            {
                var tab = LockedMarkdown[i];
                if (tab.Header != session) continue;
                ShowMarkdown(tab);
                return true;
            }
            return false;
        }

        public static bool IsNativeViewerHeader(string session)
        {
            if (_markdownPreview != null && _markdownPreview.Header == session) return true;
            foreach (var tab in LockedMarkdown)
                if (tab.Header == session) return true;
            return false;
        }

        static void ViewSourceFile(string project, string path, string label)
        {
            Tree.SelectKey(ContentTreeView.SelectionKey(project, path));
            _showing = RowAct.View;
            Viewers.ForPreview().ViewFile(project, path, label);
        }

        public static void EditFile(string project, string path, string label, int line = 0)
        {
            if (string.IsNullOrEmpty(project))
            {
                SessionHub.Instance.Run("", Pager.EditorCommand(path, line), label,
                    session => TerminalWindow.Open(session), UiWidgets.Fail,
                    host: true, temp: true);
                return;
            }
            if (SessionHub.Instance.Project(project) == null)
            {
                UiWidgets.Fail($"project '{project}' has gone");
                return;
            }
            SessionHub.Instance.Run(project, Pager.EditorCommand(path, line), label,
                session => TerminalWindow.Open(session), UiWidgets.Fail);
        }

        public static void ReleaseViewer()
        {
            ClearSelection();
            Viewers.ReleasePreview();
            ReleaseMarkdownPreview();
        }

        static void ReleaseViewerForTree()
        {
            _showing = RowAct.None;
            Viewers.ReleasePreview();
            ReleaseMarkdownPreview();
        }

        static void ReleaseMarkdownPreview()
        {
            if (_activeMarkdown != null && Showing(_activeMarkdown))
            {
                if (_activeMarkdown.Locked)
                    AddLockedMarkdown(_activeMarkdown);
                else
                {
                    Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                    _activeMarkdown = null;
                }
            }
            else _activeMarkdown = null;

            if (_markdownPreview == null) return;
            if (_markdownPreview.Locked) AddLockedMarkdown(_markdownPreview);
            _markdownPreview = null;
        }

        static void AddLockedMarkdown(MarkdownTab tab)
        {
            if (tab != null && !LockedMarkdown.Contains(tab)) LockedMarkdown.Add(tab);
        }

        public static bool IsViewerSession(string session)
        {
            if (Viewers.IsSession(session)) return true;
            if (_markdownPreview != null && _markdownPreview.Header == session &&
                (_markdownPreview.Locked || Showing(_markdownPreview))) return true;
            foreach (var tab in LockedMarkdown)
                if (tab.Header == session) return true;
            return false;
        }

        public static bool IsViewerLocked(string session)
        {
            if (Viewers.IsLocked(session)) return true;
            if (_markdownPreview != null && _markdownPreview.Header == session)
                return _markdownPreview.Locked;
            foreach (var tab in LockedMarkdown)
                if (tab.Header == session) return tab.Locked;
            return false;
        }

        public static bool LockViewer(string session)
        {
            if (Viewers.Lock(session)) return true;
            if (_markdownPreview != null && _markdownPreview.Header == session)
            {
                _markdownPreview.Locked = true;
                return true;
            }
            foreach (var tab in LockedMarkdown)
                if (tab.Header == session)
                {
                    tab.Locked = true;
                    return true;
                }
            return false;
        }

        public static bool LockViewerFile(string project, string path)
        {
            if (Viewers.LockPreview(project, path)) return true;
            if (_markdownPreview != null && _markdownPreview.View.Project == (project ?? "") &&
                _markdownPreview.View.Path == path &&
                (_markdownPreview.Locked || Showing(_markdownPreview)))
            {
                _markdownPreview.Locked = true;
                return true;
            }
            foreach (var tab in LockedMarkdown)
                if (tab.View.Project == (project ?? "") && tab.View.Path == path) return true;
            return false;
        }

        public static void CloseViewerIf(string session) => Viewers.CloseIf(session);

        static void ClearSelection()
        {
            Tree.ClearSelection();
            _showing = RowAct.None;
        }

    }
}
