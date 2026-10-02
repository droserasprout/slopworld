using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SlopWorld
{
    // Builds path menus and runs host/file mutations. Successful mutations invalidate readers
    // and both directory/status views; drawing and reader lifetimes stay with their owners.
    sealed class FilesActions
    {
        readonly FilesStore _store;
        readonly FilesViewerController _viewer;

        public FilesActions(FilesStore store, FilesViewerController viewer)
        {
            _store = store;
            _viewer = viewer;
        }

        // Project headings also offer a host terminal.
        public List<FloatMenuOption> Menu(FileNode node, string project = null)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(node.Path)),
            };

            string rel = node.Relative;
            if (rel != null)
                opts.Add(new FloatMenuOption("Copy relative path", () => Copy(rel)));

            if (project != null && SidebarScopes.Project(project) != null)
                opts.Add(new FloatMenuOption("Terminal (host)", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(project,
                    session => TerminalWindow.Open(session), UiLayout.Fail)));

            AddFileActions(opts, node.Project, node.Path, node.Name, rel);

            if (!node.IsDir && (FileTypes.IsText(node.Name) || FileTypes.IsImage(node.Name)))
            {
                opts.Add(new FloatMenuOption("View", () => _viewer.View(node)));
                if (FileTypes.IsMarkdown(node.Name))
                    opts.Add(new FloatMenuOption("View in pager", () => _viewer.ViewSource(node)));
                if (FileTypes.IsText(node.Name))
                    opts.Add(new FloatMenuOption("Edit", () => _viewer.EditFile(node.Project, node.Path,
                        "edit-" + node.Name)));
            }

            if (node.Depth != 0)
            {
                opts.Add(UiMenu.Separator());
                opts.Add(new FloatMenuOption("Rename", () => Rename(node)));
                opts.Add(new FloatMenuOption("Remove", () => Remove(node)));
            }

            // The desktop MIME database resolves directory paths to file managers.
            AddOpenIn(opts, node.Path, node.Project);

            if (node.IsDir)
            {
                opts.Add(UiMenu.Separator());
                opts.Add(new FloatMenuOption("New file", () => Create(node, "file")));
                opts.Add(new FloatMenuOption("New folder", () => Create(node, "folder")));
                opts.Add(new FloatMenuOption("Shell here", () => ShellHere(node)));
            }

            return opts;
        }

        // Host application selection is shared by every sidebar tree that names a path.
        // Keep the submenu here so Files and Git use the same asynchronous MIME lookup and
        // portal fallback.
        public void AddOpenIn(List<FloatMenuOption> opts, string path, string project = null)
        {
            if (!SessionHub.Instance.Capabilities.DesktopOpen) return;
            opts.Add(new UiSubmenu("Open in", () => OpenInOptions(path, project)));
        }

        List<FloatMenuOption> OpenInOptions(string path, string project)
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

        void OpenInApp(string path, string desktopFile, string project)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(desktopFile)) return;
            string command = PagerCommands.FileActionCommand(
                "gio launch " + Pager.Quote(desktopFile) + " {{ absolute_path }}", path, null);
            HostFileAction(path, command, project);
        }

        void OpenInOther(string path, string project)
        {
            if (string.IsNullOrEmpty(path)) return;
            HostFileAction(path, NativeAppPicker.Command(path), project);
        }

        internal static Wire.FileActionReq ScopeAction(string scope, string path, string command, bool host = false) =>
            new Wire.FileActionReq { Project = SidebarScopes.ProjectName(scope) ?? "", Worktree = SidebarScopes.Worktree(scope),
                Path = path, Command = command, Host = host };

        void HostFileAction(string path, string command, string project)
        {
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, ScopeAction(project, path, command, string.IsNullOrEmpty(project)), null, UiLayout.Fail);
        }

        // File actions run on the host. The project still scopes selected paths and supplies
        // the working directory. Private-state paths have no project.
        public void AddFileActions(List<FloatMenuOption> opts, string project, string path,
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

        List<FloatMenuOption> FileActionOptions(string project, string path, string name,
            string relative, List<LibraryItemInfo> actions)
        {
            return actions.Select(action => new FloatMenuOption(action.Name, () =>
                RunFileAction(project, path, name, relative, action))).ToList();
        }

        void RunFileAction(string project, string path, string name, string relative,
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

        void ShowFileActionResult(string project, string path, string command, bool host,
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

        void RunFileActionSilently(string project, string path, string command, bool host)
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
        void RefreshAfterFileAction()
        {
            _store.Reload();
            GitView.Refresh();
        }

        void OpenFileActionTerminal(string project, string path, string name,
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

        string ProjectRelative(string project, string path)
        {
            string root = SidebarScopes.Directory(project);
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;
            if (root == "/")
                return path.StartsWith("/", StringComparison.Ordinal) && path.Length > 1 ? path.Substring(1) : null;

            root = root.TrimEnd('/');
            if (path == root) return null;
            return path.StartsWith(root + "/", StringComparison.Ordinal) ? path.Substring(root.Length + 1) : null;
        }

        void Rename(FileNode node) => FileNameDialog.Open(
            "Rename " + node.Name, node.Name, name =>
            {
                DaemonClient.Put(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path, Name = name },
                    _ =>
                    {
                        _viewer.InvalidateReaders(node.Path);
                        RefreshAfterFileAction();
                    }, UiLayout.Fail);
            });

        void Remove(FileNode node)
        {
            string what = node.IsDir ? "folder and everything inside it" : "file";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                $"Remove {what} '{node.Name}'?",
                () => DaemonClient.Delete(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path },
                    _ =>
                    {
                        _viewer.InvalidateReaders(node.Path);
                        RefreshAfterFileAction();
                    }, UiLayout.Fail),
                destructive: true));
        }

        void Create(FileNode node, string kind)
        {
            string fallback = kind == "folder" ? "new-folder" : "untitled";
            FileNameDialog.Open(kind == "folder" ? "New folder" : "New file", fallback, name =>
            {
                DaemonClient.Post(WireProtocol.Routes.Files,
                    new Wire.FileReq { Path = node.Path, Name = name, Kind = kind },
                    _ => RefreshAfterFileAction(), UiLayout.Fail);
            });
        }

        void ShellHere(FileNode node)
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

        void Copy(string text) => DaemonClipboard.Copy(text,
            () => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                false), UiLayout.Fail);

    }
}
