using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Files view is a lazy directory tree rendered from AgentSidebar's back pass; the daemon
    // owns browsing because the game is outside every session's mount namespace.
    public static class FilesView
    {
        // A directory is a rung above a file, which is the whole of the distinction this view
        // draws between them; both, and the greys around them, are SlopWidgets'.

        // One directory, once it has been asked about. `Kids` null is "never asked", which is
        // what makes the tree lazy: a project root is a hundred thousand files deep and the
        // column shows thirty rows.
        class Node : IContentTreeNode
        {
            public string Path;
            public string Name;
            public bool IsDir;
            public bool Expanded;
            public bool Loading;
            public string Error;
            public bool More;          // the daemon's cap cut the listing short
            public List<Node> Kids;
            public string Root;        // the project dir this hangs off, for a relative path
            public string Project;     // and the project that owns it, for an errand
            public int Depth;
            // Invalidates a listing already in flight when Reload forgets this node.
            public int ListingVersion;

            string IContentTreeNode.Name => Name;
            string IContentTreeNode.Key => Path;
            string IContentTreeNode.Project => Project;
            bool IContentTreeNode.IsDirectory => IsDir;
            int IContentTreeNode.Depth => Depth;
            bool IContentTreeNode.CanExpand => IsDir && (Kids == null || Kids.Count > 0);
            bool IContentTreeNode.Loading => Loading;
            string IContentTreeNode.Error => Error;
            bool IContentTreeNode.More => More;
            IEnumerable<IContentTreeNode> IContentTreeNode.Children => Kids;
        }

        sealed class TreeSource : ContentTreeSource
        {
            public override IList<ContentTreeGroup> Groups()
            {
                if (_focusedRoot != null)
                    return new List<ContentTreeGroup>
                    {
                        new ContentTreeGroup(_focusedKey, _focusedRoot.Name, _focusedRoot.Path,
                            null, _focusedRoot)
                    };

                var groups = new List<ContentTreeGroup>();
                foreach (var project in ViewChrome.Projects())
                {
                    var root = Root(project);
                    groups.Add(new ContentTreeGroup(project, project, root.Path, project, root));
                }
                return groups;
            }

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                Shut.Contains(group.Key);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                if (!Shut.Remove(group.Key)) Shut.Add(group.Key);
            }

            public override string GroupTooltip(ContentTreeGroup group) => group.Path;

            public override void EnsureLoaded(IContentTreeNode node)
            {
                var file = (Node)node;
                if (file.Expanded && file.Kids == null && !file.Loading && file.Error == null)
                    Fetch(file);
            }

            public override bool IsExpanded(IContentTreeNode node) => ((Node)node).Expanded;

            public override void ToggleNode(IContentTreeNode node)
            {
                var file = (Node)node;
                file.Expanded = !file.Expanded;
                // Closing and opening again is the retry when the last request failed.
                file.Error = null;
            }

            public override RowAct Actions(IContentTreeNode node) => Acts((Node)node);
            public override void Open(IContentTreeNode node) => FilesView.Open((Node)node);
            public override void Action(IContentTreeNode node, RowAct action) =>
                FilesView.Act((Node)node, action);

            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                Menu((Node)group.Root, group.Value as string);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                Menu((Node)node);
        }

        // Keyed by project name rather than by directory: two projects on one directory are
        // two headings, and renaming a project is a heading that has gone.
        static readonly Dictionary<string, Node> Roots = new Dictionary<string, Node>();

        // A storage entry is not a project, but it is still a directory the same tree can
        // browse. It temporarily replaces the project roots when Storage hands Files a path.
        static Node _focusedRoot;
        static string _focusedKey;

        // Which project headings are rolled up here. The agents view has its own set in the
        // settings; this one is a tree's shape and lives no longer than the process, the same
        // as every expansion below it.
        static readonly HashSet<string> Shut = new HashSet<string>();

        // The file the reader is looking at, and the ephemeral session running `less` on it.
        // The tree owns the selected row; the pager owns the ephemeral session showing it.
        static readonly Pager Viewer = new Pager();

        // And which of the two things about that file it is showing: the file, or its diff.
        // The row and its buttons open different things about the same path, so "click the one
        // already open and its pane comes back" has to be about the one that was clicked.
        static RowAct _showing;

        // Extensions `less` would rather not be handed: the viewer is for reading, and an
        // image or a zip in a text pager is a listing nobody asked for. Everything else is
        // text enough to try.
        static readonly HashSet<string> BinaryExt = new HashSet<string>
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".tga",
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".opus",
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv",
            ".pdf", ".ps", ".zip", ".gz", ".tar", ".7z", ".rar", ".xz", ".bz2",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".db", ".sqlite", ".sqlite3", ".dll", ".so", ".dylib", ".exe",
            ".bin", ".o", ".a", ".class", ".pyc", ".pyo", ".jar", ".iso", ".img",
        };

        // Public because the git view asks it about the rows it draws: the same question about
        // the same files, and one list of extensions is the point of asking it here.
        public static bool IsText(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot < 0) return true;               // no extension: read it as text
            return !BinaryExt.Contains(name.Substring(dot).ToLowerInvariant());
        }

        public static bool IsMarkdown(string name)
        {
            int dot = (name ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot).ToLowerInvariant();
            return ext == ".md" || ext == ".markdown" || ext == ".mdx";
        }

        static readonly ContentTreeView Tree = new ContentTreeView(new TreeSource());

        // Everything listed was listed under the old answer about dotfiles, so it is dropped;
        // what the reader arranged - which projects are open, and how deep - is kept.
        public static void Reload()
        {
            ReleaseViewer();
            ClearSelection();
            foreach (var root in Roots.Values) Forget(root);
            if (_focusedRoot != null) Forget(_focusedRoot);
        }

        public static void FocusDirectory(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return;

            ReleaseViewer();
            ClearSelection();
            _focusedKey = "storage:" + path;
            _focusedRoot = new Node
            {
                Path = path,
                Name = string.IsNullOrEmpty(label) ? path : label,
                IsDir = true,
                Expanded = true,
                Root = path,
                Project = null,
                Depth = 0,
            };
            Tree.JumpTo(Vector2.zero);
            AgentSidebar.ShowFiles();
        }

        public static void ClearFocus()
        {
            _focusedRoot = null;
            _focusedKey = null;
        }

        static void Forget(Node n)
        {
            if (n.Kids != null)
                foreach (var kid in n.Kids)
                    Forget(kid);
            n.Kids = null;
            n.More = false;
            n.Error = null;
            n.Loading = false;
            n.ListingVersion++;
            // Not Expanded: that is the shape, and it is what asks for the listing again on
            // the next frame this draws.
        }

        // ------------------------------------------------------------------ drawing

        public static void Draw(Rect body) => Tree.Draw(body);

        static Node Root(string project)
        {
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";

            // A project whose directory moved is a different tree under the same heading.
            if (Roots.TryGetValue(project, out var root) && root.Path == dir) return root;

            root = new Node
            {
                Path = dir,
                Name = project,
                IsDir = true,
                // A root is always open; `Shut` is what folds a project, the heading being
                // the agents view's heading rather than a row of this tree. Left false, Rows
                // would return before it ever asked the daemon and every project would draw
                // as an empty one.
                Expanded = true,
                Root = dir,
                Project = project,
                Depth = 0,
            };
            Roots[project] = root;
            return root;
        }

        // Text files offer View/Edit; changed files additionally offer Diff. Directories and
        // binary files offer no row actions.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;

            RowAct acts = RowAct.None;
            if (!IsText(node.Name)) return RowAct.None;
            acts |= RowAct.Edit | RowAct.View;
            if (GitView.Changed(node.Project, node.Path)) acts |= RowAct.Diff;
            return acts;
        }

        // ------------------------------------------------------------------ listing

        static void Fetch(Node node)
        {
            node.Loading = true;
            node.Error = null;

            string path = node.Path;
            int version = node.ListingVersion;
            SlopClient.Get(
                "/api/browse?files=1&path=" + System.Uri.EscapeDataString(path) +
                "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0"),
                j =>
                {
                    // The tree was dropped, or the project moved, while this was in flight.
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;

                    var kids = new List<Node>();
                    foreach (var d in j["dirs"].Items) kids.Add(Kid(node, d.AsString(), true));
                    foreach (var f in j["files"].Items) kids.Add(Kid(node, f.AsString(), false));

                    node.Kids = kids;
                    node.More = j["truncated"].AsBool();
                },
                msg =>
                {
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;
                    node.Error = msg;
                });
        }

        static Node Kid(Node parent, string name, bool dir) => new Node
        {
            Name = name,
            Path = parent.Path.TrimEnd('/') + "/" + name,
            IsDir = dir,
            Root = parent.Root,
            Project = parent.Project,
            Depth = parent.Depth + 1,
        };

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
            if (!IsText(node.Name)) { _showing = RowAct.None; Viewer.Release(); return; }
            if (IsMarkdown(node.Name))
            {
                if (same && MarkdownPreview.IsShowing(node.Path)) return;
                Viewer.Release();
                _showing = RowAct.View;
                MarkdownPreview.Open(node.Project, node.Path, node.Name);
                return;
            }
            if (!same || !Viewer.Reopen()) View(node);
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
                        session => TerminalWindow.Open(session), SlopWidgets.Fail)));

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
                    opts.Add(SlopMenu.Separator());
                    opts.Add(new FloatMenuOption("Rename", () => FilesView.Rename(node)));
                    opts.Add(new FloatMenuOption("Remove", () => FilesView.Remove(node)));
                }

                if (node.IsDir)
                {
                    opts.Add(SlopMenu.Separator());
                    opts.Add(new FloatMenuOption("New file", () => FilesView.Create(node, "file")));
                    opts.Add(new FloatMenuOption("New folder", () => FilesView.Create(node, "folder")));
                    opts.Add(new FloatMenuOption("Terminal here", () => FilesView.TerminalHere(node)));
                }

                return opts;
            }
        }

        // File actions can come from any sidebar tree. Project paths run in that project's
        // sandbox; a focused private-state path has no project and uses a disposable host
        // errand, like its viewer and editor.
        public static void AddFileActions(List<FloatMenuOption> opts, string project, string path,
            string name, string relative = null)
        {
            var actions = SessionHub.Instance.Shortcuts
                .Where(s => s.Kind == ShortcutKind.FileAction)
                .ToList();
            if (actions.Count == 0) return;
            if (relative == null) relative = ProjectRelative(project, path);

            opts.Add(new SlopSubmenu("File actions", () => FileActionOptions(
                project, path, name, relative, actions)));
        }

        static List<FloatMenuOption> FileActionOptions(string project, string path, string name,
            string relative, List<ShortcutInfo> actions)
        {
            return actions.Select(action => new FloatMenuOption(action.Name, () =>
            {
                bool host = string.IsNullOrEmpty(project);
                var command = FileActionCommand(action.Command, path, relative);
                TerminalWindow.OpenOverPane(new SlopMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Show result", () =>
                    {
                        SlopClient.Post("/api/file-action", "{" +
                            $"\"project\":{JVal.Q(project)}," +
                            $"\"path\":{JVal.Q(path)}," +
                            $"\"command\":{JVal.Q(command)}," +
                            $"\"host\":{JVal.B(host)}" +
                            "}", j =>
                            {
                                string output = j["output"].AsString("(no output)");
                                TerminalWindow.OpenOverPane(SlopAlertDialog.Create(
                                    "File action: " + action.Name, output, "Close", null));
                            }, msg =>
                                TerminalWindow.OpenOverPane(SlopAlertDialog.Create(
                                    "File action failed", msg, "Close", null,
                                    primaryKind: SlopWidgets.Btn.Danger))
                            );
                    }),
                    new FloatMenuOption("Open terminal", () =>
                        SessionHub.Instance.Run(project, command, "fa-" + name,
                            session => TerminalWindow.Open(session), SlopWidgets.Fail,
                            host: host, temp: host, path: path, hold: true)),
                }));
            })).ToList();
        }

        // File actions are shell command lines. Substitute quoted values so paths remain one
        // argv even when they contain spaces or shell metacharacters. With no placeholder the
        // historical behavior remains: the absolute path is appended as the final argument.
        static string FileActionCommand(string template, string path, string relativePath)
        {
            string command = (template ?? "").Trim();
            string absolute = Pager.Quote(path);
            string relative = Pager.Quote(relativePath ?? ".");
            bool substituted = false;
            foreach (var marker in new[] { "{{ absolute_path }}", "{{absolute_path}}" })
            {
                if (command.Contains(marker))
                {
                    command = command.Replace(marker, absolute);
                    substituted = true;
                }
            }
            foreach (var marker in new[] { "{{ relative_path }}", "{{relative_path}}" })
            {
                if (command.Contains(marker))
                {
                    command = command.Replace(marker, relative);
                    substituted = true;
                }
            }
            return substituted ? command : command + " " + absolute;
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
                SlopClient.Put("/api/files",
                    "{" + $"\"path\":{JVal.Q(node.Path)},\"name\":{JVal.Q(name)}" + "}",
                    _ => Reload(), SlopWidgets.Fail);
            });

        static void Remove(Node node)
        {
            string what = node.IsDir ? "folder and everything inside it" : "file";
            TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                $"Remove {what} '{node.Name}'?",
                () => SlopClient.Delete("/api/files",
                    "{\"path\":" + JVal.Q(node.Path) + "}",
                    _ => Reload(), SlopWidgets.Fail),
                destructive: true));
        }

        static void Create(Node node, string kind)
        {
            string fallback = kind == "folder" ? "new-folder" : "untitled";
            FileNameDialog.Open(kind == "folder" ? "New folder" : "New file", fallback, name =>
            {
                SlopClient.Post("/api/files",
                    "{" + $"\"path\":{JVal.Q(node.Path)},\"name\":{JVal.Q(name)}," +
                    $"\"kind\":{JVal.Q(kind)}" + "}",
                    _ => Reload(), SlopWidgets.Fail);
            });
        }

        static void TerminalHere(Node node)
        {
            // Errands are argv, not shell command lines. Invoke bash explicitly so the
            // directory change and the final interactive shell happen in one process, while
            // keeping the selected path quoted for both the daemon splitter and bash itself.
            string script = "cd -- " + Pager.Quote(node.Path) +
                " && exec \"${SHELL:-bash}\"";
            string command = "bash -lc " + Pager.Quote(script);
            bool host = string.IsNullOrEmpty(node.Project);
            SessionHub.Instance.Run(host ? "" : node.Project, command,
                "terminal-" + node.Name,
                session => TerminalWindow.Open(session), SlopWidgets.Fail,
                host: host, temp: host);
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

        static void Copy(string text) =>
            SlopClient.Post("/api/clipboard", "{" + $"\"text\":{JVal.Q(text)}" + "}",
                _ => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                    false),
                SlopWidgets.Fail);

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
                SlopWidgets.Fail($"project '{node.Project}' has gone");
                return;
            }

            SessionHub.Instance.Run(node.Project, cmd + " " + Pager.Quote(node.Path),
                label + "-" + node.Name,
                session => TerminalWindow.Open(session), SlopWidgets.Fail);
        }

        // ------------------------------------------------------------------ viewer
        // Markdown files use the native reader; other text files use the pager. The explicit
        // source action below keeps raw Markdown available without changing the default preview.
        static void View(Node node) => ViewFile(node.Project, node.Path, "view-" + node.Name);

        static void ViewSource(Node node)
        {
            MarkdownPreview.CloseIfShowing();
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
                Viewer.Release();
                MarkdownPreview.Open(project, path, System.IO.Path.GetFileName(path));
                return;
            }
            Viewer.ViewFile(project, path, label);
        }

        static void ViewSourceFile(string project, string path, string label)
        {
            Tree.SelectKey(ContentTreeView.SelectionKey(project, path));
            _showing = RowAct.View;
            Viewer.ViewFile(project, path, label);
        }

        public static void EditFile(string project, string path, string label, int line = 0)
        {
            if (string.IsNullOrEmpty(project))
            {
                SessionHub.Instance.Run("", Pager.EditorCommand(path, line), label,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail,
                    host: true, temp: true);
                return;
            }
            if (SessionHub.Instance.Project(project) == null)
            {
                SlopWidgets.Fail($"project '{project}' has gone");
                return;
            }
            SessionHub.Instance.Run(project, Pager.EditorCommand(path, line), label,
                session => TerminalWindow.Open(session), SlopWidgets.Fail);
        }

        public static void ReleaseViewer()
        {
            ClearSelection();
            Viewer.Release();
            MarkdownPreview.CloseIfShowing();
        }

        static void ReleaseViewerForTree()
        {
            _showing = RowAct.None;
            Viewer.Release();
            MarkdownPreview.CloseIfShowing();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);

        static void ClearSelection()
        {
            Tree.ClearSelection();
            _showing = RowAct.None;
        }

    }
}
