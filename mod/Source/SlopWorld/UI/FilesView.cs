using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The column's other view: every project's directory as one nested, foldable tree.
    //
    // Drawn from AgentSidebar's back pass, which is to say from ColonistBarOnGUI, which is
    // what puts it over a terminal as well as on the map - the same road the portraits take
    // and the reason there is no second call site anywhere.
    //
    // The daemon does the reading. A session runs in its own mount namespace and the game
    // runs outside all of them, so `/api/browse` is the only thing on this machine that can
    // see a project directory the way the project does.
    public static class FilesView
    {
        // Off the font, not written down: every line in this tree is drawn at Tiny, and a
        // figure here is one that fits the face it was eyeballed against and crops the next.
        // TinyRowH also answers for the tier Tiny actually lands on - see SlopWidgets.Real.
        static float RowH => SlopWidgets.TinyRowH;
        const float IconW = 16f;
        const float Indent = 11f;
        const float Pad = 6f;
        const float CellX = 8f;
        const float ArrowW = 11f;

        // A directory is a rung above a file, which is the whole of the distinction this view
        // draws between them; both, and the greys around them, are SlopWidgets'.

        // One directory, once it has been asked about. `Kids` null is "never asked", which is
        // what makes the tree lazy: a project root is a hundred thousand files deep and the
        // column shows thirty rows.
        class Node
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
        }

        // Keyed by project name rather than by directory: two projects on one directory are
        // two headings, and renaming a project is a heading that has gone.
        static readonly Dictionary<string, Node> Roots = new Dictionary<string, Node>();

        // Which project headings are rolled up here. The agents view has its own set in the
        // settings; this one is a tree's shape and lives no longer than the process, the same
        // as every expansion below it.
        static readonly HashSet<string> Shut = new HashSet<string>();

        static readonly SmoothScroll _scroll = new SmoothScroll();

        // The file the reader is looking at, and the ephemeral session running `less` on it.
        // `_selected` is what the tree highlights; `Viewer` is who is showing it. The two move
        // together except when the file is not text - then the tree marks it and nobody is
        // showing it.
        static string _selected;
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

        // Laid out by Draw, read by the click pass, so the two can never disagree about where
        // a row is - the same reason AgentSidebar keeps a Row table.
        struct Line
        {
            public Node Node;
            public string Project;   // set on a heading, null on a file or a directory
            public Rect Rect;
        }

        static readonly List<Line> Lines = new List<Line>();

        // Everything listed was listed under the old answer about dotfiles, so it is dropped;
        // what the reader arranged - which projects are open, and how deep - is kept.
        public static void Reload()
        {
            ReleaseViewer();
            ClearSelection();
            foreach (var root in Roots.Values) Forget(root);
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

        public static void Draw(Rect body)
        {
            Lines.Clear();

            var projects = Projects();
            if (projects.Count == 0)
            {
                Empty(body);
                return;
            }

            float height = Measure(projects);
            var view = new Rect(0f, 0f, body.width - (height > body.height ? 16f : 0f),
                height);

            // GUI rather than GUILayout, so this is safe in a pass that declines Layout
            // events - see AgentSidebar.DrawBack. Closed from a finally for the reason
            // SlopOptions closes its group from a finalizer: a group left open is every
            // window drawn after it drawn somewhere else.
            _scroll.Begin(body, view);
            try
            {
                float y = Pad;
                foreach (var name in projects)
                {
                    var root = Root(name);
                    y = Head(view.width, y, name, root);
                    if (!Shut.Contains(name)) y = Rows(view.width, y, root);
                }
            }
            finally
            {
                _scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        static void Empty(Rect body)
        {
            var r = new Rect(CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            GUI.color = SlopWidgets.Faint;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(r, SessionHub.Instance.Online
                ? "No project has a directory yet."
                : $"daemon {SessionHub.Instance.Status}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        // Every project with somewhere to look, ordered the way the agents view orders its
        // headings, so the two read as the same column with different contents in it.
        static List<string> Projects()
        {
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir)) names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

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

        // The height the whole tree wants, so the scroll view knows before anything is drawn.
        // Cheap: it walks only what is expanded, which is what is on screen.
        static float Measure(List<string> projects)
        {
            float h = Pad * 2f;
            foreach (var name in projects)
            {
                h += RowH;
                if (!Shut.Contains(name)) h += Count(Root(name)) * RowH;
            }
            return h;
        }

        static float Count(Node n)
        {
            if (!n.Expanded) return 0f;
            if (n.Kids == null) return 1f;                  // the "..." while it loads
            float c = n.Kids.Count + (n.More ? 1f : 0f);
            foreach (var kid in n.Kids) c += Count(kid);
            return c;
        }

        // The project band, the same shape the agents view's heading is - one column, one
        // kind of heading, whichever view is under it.
        static float Head(float width, float y, string project, Node root)
        {
            var r = new Rect(0f, y, width, RowH);
            bool shut = Shut.Contains(project);

            SlopWidgets.HoverRow(r);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, shut ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float lx = arrow.xMax + 4f;
            var label = new Rect(lx, r.y, r.width - lx - CellX, RowH);
            SlopWidgets.RowLabel(label, project);

            Widgets.DrawBoxSolid(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                SlopWidgets.Edge);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, $"{root.Path}\n\nClick to fold.");
            Lines.Add(new Line { Node = root, Project = project, Rect = r });
            return y + RowH;
        }

        static float Rows(float width, float y, Node parent)
        {
            // Asked for on the way past rather than on the click, so a listing dropped by the
            // dotfile switch comes back without the reader having to fold and unfold. An
            // error stops that: a directory that refused once refuses sixty times a second,
            // and the retry is the reader closing it and opening it again.
            if (parent.Expanded && parent.Kids == null && !parent.Loading && parent.Error == null)
                Fetch(parent);

            if (!parent.Expanded) return y;

            if (parent.Kids == null)
            {
                y = Note(width, y, parent.Depth + 1,
                    parent.Error ?? "...", parent.Error != null ? SlopWidgets.Bad : SlopWidgets.Faint);
                return y;
            }

            foreach (var node in parent.Kids)
            {
                y = Row(width, y, node);
                y = Rows(width, y, node);
            }

            // What the cap left off is the daemon's business and it does not say how much: it
            // stopped reading, so it never counted the rest either.
            if (parent.More)
                y = Note(width, y, parent.Depth + 1, "... more, not listed", SlopWidgets.Faint);

            return y;
        }

        static float Note(float width, float y, int depth, string text, Color color)
        {
            float x = CellX + depth * Indent;
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            var r = new Rect(x, y, width - x - Pad, RowH);
            SlopWidgets.RowLabel(r, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y + RowH;
        }

        static float Row(float width, float y, Node node)
        {
            var r = new Rect(0f, y, width, RowH);
            bool over = SlopWidgets.HoverRow(r);
            if (node.Path == _selected)
                Slab.Fill(r, SlopWidgets.Hover);

            float x = CellX + node.Depth * Indent;

            if (node.IsDir)
            {
                GUI.color = SlopWidgets.Faint;
                GUI.DrawTexture(new Rect(x, y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                    node.Expanded ? TexButton.Collapse : TexButton.Reveal);
                GUI.color = Color.white;
            }
            x += ArrowW + 3f;

            var icon = FileIcons.Of(node.Name, node.IsDir);
            if (icon != null)
                GUI.DrawTexture(new Rect(x, y + (RowH - IconW) / 2f, IconW, IconW), icon);
            x += IconW + 5f;

            // What this row can be asked to do, drawn only under the mouse and only over the
            // end of the name - a tree of files has nothing else out there to give up.
            float rx = width - Pad;
            var acts = over ? Acts(node) : RowAct.None;
            if (acts != RowAct.None) rx = RowActions.Draw(r, rx, acts) - 4f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = node.IsDir ? SlopWidgets.Lead : SlopWidgets.Name;
            var label = new Rect(x, y, Mathf.Max(0f, rx - x), RowH);
            SlopWidgets.RowLabel(label, node.Name);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Lines.Add(new Line { Node = node, Rect = r });
            return y + RowH;
        }

        // The buttons a row offers. A directory offers none - `less` on one is a listing
        // nobody asked for and `micro` on one is a file browser inside a game, which is the
        // same line the right-click menu draws. A binary file is not offered the pager for the
        // reason a left click on one opens nothing. And the diff is offered where the working
        // tree has something to diff, which is the git view's answer rather than this one's:
        // the two views draw the same files and only one of them has read the repository.
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

        // ------------------------------------------------------------------ clicks
        //
        // Called from AgentSidebar's back pass where Menus is in the other view: after the
        // whole tree is laid out and outside the scroll view's group, so a rect here is in the
        // coordinates ColonistBarStrip.MouseOver reads; after Grip, because a row is the full width of the
        // panel and asked first it would eat every press on the edge; and before Absorb, which
        // takes whatever the panel's contents did not.
        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            foreach (var line in Lines)
            {
                // Shifted by the scroll and clipped by the view, so the rect a row was drawn
                // at is not where it can be clicked; Contains against the scrolled rect is.
                if (!ColonistBarStrip.MouseOver(Screen(line.Rect))) continue;

                if (line.Project != null)
                {
                    if (e.button == 0) Fold(line.Project);
                    else Menu(line.Node, line.Project);
                    // A heading is not a file: the reader's focus has moved off whatever
                    // was showing, so the viewer goes.
                    ClearSelection();
                    ReleaseViewer();
                }
                else if (e.button == 0)
                {
                    // The hover strip first, and the row's own answer only where the press
                    // missed it: a button drawn over the end of a name is a button, and the
                    // row underneath it must not act on the same press.
                    var scr = Screen(line.Rect);
                    var hit = RowActions.Hit(scr, scr.xMax - Pad, Acts(line.Node));
                    if (hit != RowAct.None)
                    {
                        Act(line.Node, hit);
                    }
                    else if (line.Node.IsDir)
                    {
                        line.Node.Expanded = !line.Node.Expanded;
                        // Closing and opening again is the retry: the draw pass declines to
                        // ask a second time while an error stands.
                        line.Node.Error = null;
                        ClearSelection();
                        ReleaseViewer();
                    }
                    else
                    {
                        Open(line.Node);
                    }
                }
                else
                {
                    Menu(line.Node);
                }

                e.Use();
                return;
            }
        }

        // The view's own rect, moved into the panel and up by however far it is scrolled. The
        // scroll view is clipped, so a row scrolled off the top would otherwise still answer a
        // click in the strip above it.
        static Rect Screen(Rect r)
        {
            var body = AgentSidebar.TreeBody(AgentSidebar.Body, AgentSidebar.TabFiles);
            var moved = new Rect(body.x + r.x - _scroll.Position.x, body.y + r.y - _scroll.Position.y,
                r.width, r.height);
            return moved.yMax <= body.y || moved.y >= body.yMax ? Rect.zero : moved;
        }

        static void Fold(string project)
        {
            if (!Shut.Remove(project)) Shut.Add(project);
        }

        // Left on a file: mark it and read it. Text files open in `less` in a pane over the
        // tree; a binary file is marked but nobody is handed it. Clicking the file already
        // being read just brings its pane back - a focus change is a *different* file, and
        // only that replaces the viewer.
        static void Open(Node node)
        {
            bool same = _selected == node.Path && _showing == RowAct.View;
            _selected = node.Path;
            // Marked, and nobody showing it: whatever was in the pane is not about this row.
            if (!IsText(node.Name)) { _showing = RowAct.None; ReleaseViewer(); return; }
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
        static void Menu(Node node, string project = null)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(node.Path)),
            };

            string rel = Relative(node);
            if (rel != null)
                opts.Add(new FloatMenuOption("Copy relative path", () => Copy(rel)));

            if (project != null)
                opts.Add(new FloatMenuOption("Terminal (host)", () =>
                    SessionHub.Instance.RunHostShell(project,
                        session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            // Only files, and only because a directory in `less` is a listing nobody asked
            // for and a directory in `micro` is a file browser inside a game. The left
            // button views a text file; the menu's View routes through the same tracked
            // viewer so it is replaced or closed like any other.
            if (!node.IsDir && IsText(node.Name))
            {
                opts.Add(new FloatMenuOption("View", () => View(node)));
                opts.Add(new FloatMenuOption("Edit", () => EditFile(node.Project, node.Path,
                    "edit-" + node.Name)));
            }

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        // Against the project's own directory. Null for the root itself, which has no relative
        // path worth the name, and for anything that somehow sits outside it.
        static string Relative(Node node)
        {
            string root = node.Root?.TrimEnd('/');
            if (string.IsNullOrEmpty(root)) return null;
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
        //
        // The file manager's other half: a `less` session on the selected file, shown in a
        // pane over the tree. At most one is open - the reader replaces it by clicking
        // another file, and the focus leaving the tree closes it. All of that is `Pager`'s;
        // the command is this view's.
        static void View(Node node) => ViewFile(node.Project, node.Path, "view-" + node.Name);

        // Public for GitView: viewing a changed file is a Files operation, regardless of
        // which tree supplied the click. The shared tab switch is done by the caller.
        public static void ViewFile(string project, string path, string label)
        {
            // A project that has gone takes the mark with it: the tree would otherwise
            // highlight a row nobody is reading.
            if (SessionHub.Instance.Project(project) == null) ClearSelection();
            else
            {
                _selected = path;
                _showing = RowAct.View;
            }
            Viewer.ViewFile(project, path, label);
        }

        public static void EditFile(string project, string path, string label)
        {
            if (SessionHub.Instance.Project(project) == null)
            {
                SlopWidgets.Fail($"project '{project}' has gone");
                return;
            }
            SessionHub.Instance.Run(project, "micro -- " + Pager.Quote(path), label,
                session => TerminalWindow.Open(session), SlopWidgets.Fail);
        }

        public static void ReleaseViewer() => Viewer.Release();

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);

        static void ClearSelection()
        {
            _selected = null;
            _showing = RowAct.None;
        }

    }
}
