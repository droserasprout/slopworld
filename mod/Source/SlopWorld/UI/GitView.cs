using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The column's third view: what every project's working tree has that its last commit
    // does not, as the same nested tree the files view draws.
    //
    // Drawn from AgentSidebar's back pass, which is to say from ColonistBarOnGUI, the same
    // road the portraits and the file tree take and the reason there is no second call site.
    //
    // The daemon runs the git. A session is in its own mount namespace and the game is outside
    // all of them, so `/api/git` is here for the reason `/api/browse` is.
    //
    // Where the files view is lazy - one directory a click, because a project root is a
    // hundred thousand files deep - this one is not. A working tree's changes are a list
    // short enough to ask for whole, and the tree is folded out of that list rather than
    // walked: a directory here exists because something under it changed.
    public static class GitView
    {
        const float RowH = 20f;
        const float IconW = 16f;
        const float Indent = 11f;
        const float Pad = 6f;
        const float CellX = 8f;
        const float ArrowW = 11f;

        // One node of the folded-out tree. A directory holds kids and nothing else; a file
        // holds the row the daemon sent.
        class Node
        {
            public string Name;
            public string Rel;          // relative to the repository root, which is what git takes back
            public bool IsDir;
            public bool Expanded = true; // a change tree is small, so it opens showing everything
            public List<Node> Kids;
            public string Status;       // the porcelain pair, files only
            public int Added = -1;      // -1 is "git counted none": binary, or untracked
            public int Deleted = -1;
            public int Depth;
        }

        // One project's answer, as it stands. Keyed by project name for the reason the files
        // view keys its roots that way: two projects on one directory are two headings, and
        // renaming a project is a heading that has gone.
        class Repo
        {
            public string Project;      // whose heading this is, so a row can name it in an errand
            public string Dir;          // the project's directory, which is what was asked about
            public string Root;         // the repository's, which may be above it
            public string Branch;
            public bool IsRepo = true;
            public bool Loading;
            public string Error;
            public bool Asked;          // whether an answer has ever landed
            public int Changed, Added, Deleted;
            public Node Tree;
            public HashSet<string> Shut = new HashSet<string>();  // folded directories, by Rel
        }

        static readonly Dictionary<string, Repo> Repos = new Dictionary<string, Repo>();

        // Which project headings are rolled up. The files view keeps its own set for the same
        // reason: a tree's shape lives no longer than the process, and the agents view's folds
        // are about agents.
        static readonly HashSet<string> Shut = new HashSet<string>();

        static Vector2 _scroll;

        // The change the reader is looking at, and the ephemeral session paging its diff.
        // `_selected` is what the tree highlights; `Viewer` is who is showing it.
        static string _selected;
        static readonly Pager Viewer = new Pager();

        // ------------------------------------------------------------------ asking

        // Every project's tree read again. This is what the refresh button does, and what
        // arriving in the view does - a working tree changes under this column all day and
        // nothing tells it so, the agents being the ones doing the changing.
        public static void Refresh()
        {
            foreach (var name in Projects()) Fetch(name);
        }

        // Arriving in the view. Separate from Refresh only in what it does not do: a tree
        // already asked about is left alone until the reader asks again, so switching back
        // and forth across a slow repository does not restart the read every time.
        public static void Entered()
        {
            foreach (var name in Projects())
                if (!Get(name).Asked) Fetch(name);
        }

        static Repo Get(string project)
        {
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";

            // A project whose directory moved is a different repository under the same
            // heading, and what was known about the old one is not about this one.
            if (Repos.TryGetValue(project, out var repo) && repo.Dir == dir) return repo;

            repo = new Repo { Project = project, Dir = dir };
            Repos[project] = repo;
            return repo;
        }

        static void Fetch(string project)
        {
            var repo = Get(project);
            if (repo.Loading || string.IsNullOrEmpty(repo.Dir)) return;

            repo.Loading = true;
            repo.Error = null;

            string dir = repo.Dir;
            SlopClient.Get("/api/git?path=" + System.Uri.EscapeDataString(dir),
                j =>
                {
                    repo.Loading = false;
                    repo.Asked = true;
                    // The project moved, or was pointed somewhere else, while this was in
                    // flight; the answer is about a directory nothing is showing.
                    if (repo.Dir != dir) return;

                    repo.IsRepo = j["repo"].AsBool();
                    if (!repo.IsRepo)
                    {
                        repo.Tree = null;
                        repo.Changed = repo.Added = repo.Deleted = 0;
                        return;
                    }

                    repo.Root = j["root"].AsString();
                    repo.Branch = j["branch"].AsString();
                    repo.Changed = j["changed"].AsInt();
                    repo.Added = j["added"].AsInt();
                    repo.Deleted = j["deleted"].AsInt();
                    repo.Tree = Fold(j["files"]);
                },
                msg =>
                {
                    repo.Loading = false;
                    repo.Asked = true;
                    if (repo.Dir != dir) return;
                    repo.Error = msg;
                    // The tree goes with it: what is drawn is the error alone, and a stale
                    // tree under a message about why it could not be read is two answers.
                    repo.Tree = null;
                });
        }

        // The flat list of changed paths, folded into the tree it describes. The daemon sends
        // them sorted, so a directory's rows arrive together and the walk down never has to
        // look back; every interior node is a directory because something under it changed,
        // which is the whole difference between this tree and the files view's.
        static Node Fold(JVal files)
        {
            var root = new Node { IsDir = true, Depth = -1, Kids = new List<Node>(), Rel = "" };

            foreach (var f in files.Items)
            {
                string rel = f["path"].AsString();
                if (string.IsNullOrEmpty(rel)) continue;

                var parts = rel.Split('/');
                var at = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string seg = parts[i];
                    // The last kid is the one to grow: the list is sorted, so anything else
                    // under this directory has already been added.
                    var last = at.Kids.Count > 0 ? at.Kids[at.Kids.Count - 1] : null;
                    if (last == null || !last.IsDir || last.Name != seg)
                    {
                        last = new Node
                        {
                            Name = seg,
                            IsDir = true,
                            Kids = new List<Node>(),
                            Depth = at.Depth + 1,
                            Rel = at.Rel.Length == 0 ? seg : at.Rel + "/" + seg,
                        };
                        at.Kids.Add(last);
                    }
                    at = last;
                }

                at.Kids.Add(new Node
                {
                    Name = parts[parts.Length - 1],
                    Rel = rel,
                    IsDir = false,
                    Depth = at.Depth + 1,
                    Status = f["status"].AsString(),
                    // Null where git counted nothing - a binary file, or one it has no blob
                    // for. Kept apart from zero, which is a real count and a different row.
                    Added = f["added"].IsNull ? -1 : f["added"].AsInt(),
                    Deleted = f["deleted"].IsNull ? -1 : f["deleted"].AsInt(),
                });
            }

            // A directory holding one directory holding one file is three rows saying one
            // thing. Squashed the way every git client squashes it: `slopd/src` is one row.
            // Depth is written afterwards rather than kept in step through the squash, a
            // collapsed chain moving everything under it up by however long the chain was.
            Squash(root);
            Depths(root, -1);
            return root;
        }

        // A directory with exactly one child, itself a directory, becomes one row named for
        // both. Bottom-up, so a chain of any length collapses in one pass.
        static void Squash(Node dir)
        {
            foreach (var kid in dir.Kids)
                if (kid.IsDir) Squash(kid);

            for (int i = 0; i < dir.Kids.Count; i++)
            {
                var kid = dir.Kids[i];
                while (kid.IsDir && kid.Kids.Count == 1 && kid.Kids[0].IsDir)
                {
                    var only = kid.Kids[0];
                    only.Name = kid.Name + "/" + only.Name;
                    kid = only;
                }
                dir.Kids[i] = kid;
            }
        }

        static void Depths(Node dir, int depth)
        {
            dir.Depth = depth;
            if (dir.Kids == null) return;
            foreach (var kid in dir.Kids) Depths(kid, depth + 1);
        }

        // ------------------------------------------------------------------ drawing

        // Laid out by Draw, read by the click pass, so the two can never disagree about where
        // a row is - the same reason the files view keeps a Lines table.
        struct Line
        {
            public Node Node;
            public Repo Repo;
            public string Project;   // set on a heading, null on a row of the tree
            public Rect Rect;
        }

        static readonly List<Line> Lines = new List<Line>();

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
            // events - see AgentSidebar.DrawBack - and closed from a finally for the reason
            // the files view closes its own: a group left open is every window drawn after
            // it drawn somewhere else.
            Widgets.BeginScrollView(body, ref _scroll, view);
            try
            {
                float y = Pad;
                foreach (var name in projects)
                {
                    var repo = Get(name);
                    y = Head(view.width, y, name, repo);
                    if (!Shut.Contains(name)) y = Body(view.width, y, name, repo);
                }
            }
            finally
            {
                Widgets.EndScrollView();
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

        // Every project with somewhere to look, ordered the way the other two views order
        // their headings, so all three read as the same column with different contents in it.
        static List<string> Projects()
        {
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir)) names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        // The height the whole thing wants, so the scroll view knows before anything is
        // drawn. It walks only what is expanded, which is what is on screen.
        static float Measure(List<string> projects)
        {
            float h = Pad * 2f;
            foreach (var name in projects)
            {
                h += RowH;
                if (Shut.Contains(name)) continue;

                var repo = Get(name);
                // The state line: the branch and the shortstat, or what went wrong, or the
                // news that this is not a repository at all. Always one row.
                h += RowH;
                if (repo.Tree != null) h += Count(repo, repo.Tree) * RowH;
            }
            return h;
        }

        static float Count(Repo repo, Node dir)
        {
            float c = 0f;
            foreach (var kid in dir.Kids)
            {
                c += 1f;
                if (kid.IsDir && !repo.Shut.Contains(kid.Rel)) c += Count(repo, kid);
            }
            return c;
        }

        // The project band, the same shape the other two views' headings are - one column,
        // one kind of heading, whichever body is under it.
        static float Head(float width, float y, string project, Repo repo)
        {
            var r = new Rect(0f, y, width, RowH);
            bool shut = Shut.Contains(project);

            if (ColonistBarStrip.MouseOver(r)) Widgets.DrawHighlight(r);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, shut ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            // The count from the right, so the figures line up down the column the way the
            // agents view's times do. Only where the heading is folded: with the tree open
            // the rows are the count, and the state line under it says the rest.
            float rx = r.width - CellX;
            if (shut && repo.IsRepo && repo.Changed > 0)
            {
                GUI.color = SlopWidgets.Dim;
                Text.Anchor = TextAnchor.MiddleRight;
                var count = new Rect(r.width * 0.5f, r.y, rx - r.width * 0.5f, RowH);
                Widgets.Label(count, repo.Changed.ToString());
                rx = count.x - 4f;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = SlopWidgets.Faint;
            }

            float lx = arrow.xMax + 4f;
            var label = new Rect(lx, r.y, rx - lx, RowH);
            Widgets.Label(label, project.Truncate(label.width));

            Widgets.DrawBoxSolid(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                new Color(1f, 1f, 1f, 0.08f));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, $"{repo.Dir}\n\nClick to fold.");
            Lines.Add(new Line { Repo = repo, Project = project, Rect = r });
            return y + RowH;
        }

        // The state line and then the tree, or the state line alone where there is no tree to
        // draw. One row either way, which is what Measure counts.
        static float Body(float width, float y, string project, Repo repo)
        {
            if (repo.Loading && repo.Tree == null)
                return Note(width, y, 0, "...", SlopWidgets.Faint);
            if (repo.Error != null)
                return Note(width, y, 0, repo.Error, SlopWidgets.Bad);
            if (!repo.Asked)
                return Note(width, y, 0, "not read yet", SlopWidgets.Faint);
            if (!repo.IsRepo)
                return Note(width, y, 0, "not a git repository", SlopWidgets.Faint);

            y = State(width, y, repo);
            return Rows(width, y, repo, repo.Tree);
        }

        // The branch, and the shortstat beside it: `git diff --shortstat` said in the room a
        // column this narrow has, which is a figure each rather than a sentence.
        static float State(float width, float y, Repo repo)
        {
            var r = new Rect(0f, y, width, RowH);
            float x = CellX;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            // From the right, so the branch takes whatever is left rather than pushing the
            // figures off the edge - a branch name is the long half of this line.
            float rx = width - Pad;
            if (repo.Changed == 0)
            {
                rx = Tail(rx, y, "clean", SlopWidgets.Faint);
            }
            else
            {
                if (repo.Deleted > 0) rx = Tail(rx, y, "-" + repo.Deleted, SlopWidgets.Bad);
                if (repo.Added > 0) rx = Tail(rx, y, "+" + repo.Added, SlopWidgets.Yes);
                rx = Tail(rx, y, repo.Changed.ToString(), SlopWidgets.Dim);
            }

            GUI.color = SlopWidgets.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            var branch = new Rect(x, y, Mathf.Max(0f, rx - x - 4f), RowH);
            Widgets.Label(branch, (repo.Branch ?? "").Truncate(branch.width));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, repo.Changed == 0
                ? $"{repo.Root}\n\nNothing changed."
                : $"{repo.Root}\n\n{repo.Changed} changed, " +
                  $"{repo.Added} insertions(+), {repo.Deleted} deletions(-)");
            return y + RowH;
        }

        // One figure laid out from the right, and the x the next one ends at. The anchor does
        // the measuring, so nothing here has to know how wide a number is.
        static float Tail(float right, float y, string text, Color color)
        {
            float w = Text.CalcSize(text).x;
            GUI.color = color;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(right - w, y, w, RowH), text);
            return right - w - 5f;
        }

        static float Note(float width, float y, int depth, string text, Color color)
        {
            float x = CellX + depth * Indent;
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            var r = new Rect(x, y, width - x - Pad, RowH);
            Widgets.Label(r, text.Truncate(r.width));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y + RowH;
        }

        static float Rows(float width, float y, Repo repo, Node dir)
        {
            foreach (var node in dir.Kids)
            {
                y = Row(width, y, repo, node);
                if (node.IsDir && !repo.Shut.Contains(node.Rel))
                    y = Rows(width, y, repo, node);
            }
            return y;
        }

        static float Row(float width, float y, Repo repo, Node node)
        {
            var r = new Rect(0f, y, width, RowH);
            if (ColonistBarStrip.MouseOver(r)) Widgets.DrawHighlight(r);
            if (!node.IsDir && node.Rel == _selected)
                Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.08f));

            float x = CellX + node.Depth * Indent;

            if (node.IsDir)
            {
                GUI.color = SlopWidgets.Faint;
                GUI.DrawTexture(new Rect(x, y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                    repo.Shut.Contains(node.Rel) ? TexButton.Reveal : TexButton.Collapse);
                GUI.color = Color.white;
            }
            x += ArrowW + 3f;

            var icon = FileIcons.Of(node.Name, node.IsDir);
            if (icon != null)
                GUI.DrawTexture(new Rect(x, y + (RowH - IconW) / 2f, IconW, IconW), icon);
            x += IconW + 5f;

            Text.Font = GameFont.Tiny;

            // The counts from the right, so they line up down the column whatever the names
            // do. Directories carry none: a folder's total is a figure nobody diffs.
            float rx = width - Pad;
            if (!node.IsDir)
            {
                if (node.Added < 0)
                    rx = Tail(rx, y, "bin", SlopWidgets.Faint);
                else
                {
                    if (node.Deleted > 0) rx = Tail(rx, y, "-" + node.Deleted, SlopWidgets.Bad);
                    if (node.Added > 0) rx = Tail(rx, y, "+" + node.Added, SlopWidgets.Yes);
                }
                rx = Tail(rx, y, Mark(node.Status), MarkColor(node.Status));
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = node.IsDir ? SlopWidgets.Lead : SlopWidgets.Name;
            var label = new Rect(x, y, Mathf.Max(0f, rx - x - 2f), RowH);
            Widgets.Label(label, node.Name.Truncate(label.width));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (!node.IsDir) TooltipHandler.TipRegion(r, $"{node.Rel}\n\n{Says(node)}");

            Lines.Add(new Line { Node = node, Repo = repo, Rect = r });
            return y + RowH;
        }

        // The porcelain pair said in one character, because one is what fits: the staged
        // letter where there is one, the unstaged letter otherwise. Which of the two it was
        // is the colour's job below.
        static string Mark(string status)
        {
            if (string.IsNullOrEmpty(status)) return "?";
            if (status == "??") return "?";
            char staged = status[0];
            char worktree = status.Length > 1 ? status[1] : ' ';
            return (staged != ' ' && staged != '?' ? staged : worktree).ToString();
        }

        // Staged is the colour of a thing that is going somewhere; everything else is the
        // colour of a thing that is not. Untracked is neither, and is dimmer than both.
        static Color MarkColor(string status)
        {
            if (string.IsNullOrEmpty(status) || status == "??") return SlopWidgets.Faint;
            if (status[0] == 'U' || (status.Length > 1 && status[1] == 'U')) return SlopWidgets.Bad;
            return status[0] != ' ' ? SlopWidgets.Yes : SlopWidgets.Warn;
        }

        static string Says(Node node)
        {
            string s = node.Status ?? "";
            if (s == "??") return "untracked";

            char staged = s.Length > 0 ? s[0] : ' ';
            char worktree = s.Length > 1 ? s[1] : ' ';
            var parts = new List<string>();
            if (staged != ' ') parts.Add(Word(staged) + ", staged");
            if (worktree != ' ') parts.Add(Word(worktree) + " since");
            return parts.Count == 0 ? s : string.Join("; ", parts.ToArray());
        }

        static string Word(char c)
        {
            switch (c)
            {
                case 'M': return "modified";
                case 'A': return "added";
                case 'D': return "deleted";
                case 'R': return "renamed";
                case 'C': return "copied";
                case 'T': return "type changed";
                case 'U': return "unmerged";
                default: return c.ToString();
            }
        }

        // ------------------------------------------------------------------ clicks
        //
        // Called from AgentSidebar's back pass where Menus is in the agents view: after the
        // whole tree is laid out and outside the scroll view's group, so a rect here is in the
        // coordinates ColonistBarStrip.MouseOver reads; after Grip, because a row is the full
        // width of the panel and asked first it would eat every press on the edge.
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
                    else HeadMenu(line.Project, line.Repo);
                    // A heading is not a change: the reader's focus has moved off whatever
                    // diff was showing, so it goes.
                    ClearSelection();
                    Viewer.Release();
                }
                else if (e.button == 1)
                {
                    RowMenu(line.Node, line.Repo);
                }
                else if (line.Node.IsDir)
                {
                    if (!line.Repo.Shut.Remove(line.Node.Rel))
                        line.Repo.Shut.Add(line.Node.Rel);
                    // A directory is not a change: whatever diff was up is no longer what
                    // the reader is looking at.
                    ClearSelection();
                    Viewer.Release();
                }
                else
                {
                    // Left on a change: mark it and read its diff. Clicking the one already
                    // open just brings its pane back - a focus change is a *different* row,
                    // and only that replaces the pager.
                    bool same = _selected == line.Node.Rel;
                    _selected = line.Node.Rel;
                    if (!same || !Viewer.Reopen()) Diff(line.Node, line.Repo);
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
            var body = AgentSidebar.Body;
            var moved = new Rect(body.x + r.x - _scroll.x, body.y + r.y - _scroll.y,
                r.width, r.height);
            return moved.yMax <= body.y || moved.y >= body.yMax ? Rect.zero : moved;
        }

        static void Fold(string project)
        {
            if (!Shut.Remove(project)) Shut.Add(project);
        }

        // ------------------------------------------------------------------ menus

        static void HeadMenu(string project, Repo repo)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Refresh", () => Fetch(project)),
            };

            if (repo.IsRepo && !string.IsNullOrEmpty(repo.Root))
            {
                opts.Add(new FloatMenuOption("Copy repository path", () => Copy(repo.Root)));
                // The whole tree's diff, in the same pager one file's opens in. Tracked as
                // the viewer, so the next row clicked replaces it.
                opts.Add(new FloatMenuOption("Diff all", () =>
                {
                    ClearSelection();
                    Viewer.Open(project, DiffCmd(repo, null, null), "diff-" + project);
                }));
            }

            opts.Add(new FloatMenuOption("Terminal (host)", () =>
                SessionHub.Instance.RunHostShell(project,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }

        static void RowMenu(Node node, Repo repo)
        {
            string project = repo.Project;
            string abs = (repo.Root ?? "").TrimEnd('/') + "/" + node.Rel;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(abs)),
                new FloatMenuOption("Copy relative path", () => Copy(node.Rel)),
            };

            if (!node.IsDir)
                opts.Add(new FloatMenuOption("Edit", () =>
                    SessionHub.Instance.Run(project, "micro -- " + Pager.Quote(abs),
                        "edit-" + node.Name,
                        session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            opts.Add(new FloatMenuOption("Diff", () =>
            {
                // A directory's diff is every change under it and no one row's, so nothing
                // is marked; a file's is the row itself.
                _selected = node.IsDir ? null : node.Rel;
                Diff(node, repo);
            }));

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }

        static void Copy(string text) =>
            SlopClient.Post("/api/clipboard", "{" + $"\"text\":{JVal.Q(text)}" + "}",
                _ => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                    false),
                SlopWidgets.Fail);

        // ------------------------------------------------------------------ the diff
        //
        // A coloured diff in a pager, in a pane over the tree, from an ephemeral agent in the
        // project's own sandbox - which is what makes git see the working tree the way the
        // agents changing it do. At most one is open; `Pager` is the rest of that.

        static void Diff(Node node, Repo repo) =>
            Viewer.Open(repo.Project, DiffCmd(repo, node.Rel, node.Status), "diff-" + node.Name);

        // What the errand runs. The daemon builds an argv rather than running a shell, so
        // there is no pipe to be had here and the pager is git's own: `--paginate` with
        // `core.pager` set, which git *does* run through a shell, is how `less` is reached
        // from an argv that cannot contain one. `--color=always` because git decides colour by
        // whether its own stdout is a terminal, and behind a pager it is not.
        //
        // `LESS` is set on that command line rather than left to git, which fills it with
        // `FRX` when it is unset - and the `X` there is what keeps `less` off the alternate
        // screen. The pane reads `AltScreen` to decide whether the wheel is the app's or its
        // own scrollback, so under git's own default the mouse never reaches the pager and a
        // diff cannot be scrolled. `R` alone is what the files view's `less -R` amounts to,
        // and `F` is left off with it: a diff shorter than the pane would quit before it was
        // read. The shell git runs this through is what makes the assignment an assignment.
        //
        // `-C` rather than trusting the working directory: a project may be pointed at a
        // subdirectory of the repository, and a path relative to the root only means what it
        // says from the root.
        static string DiffCmd(Repo repo, string rel, string status)
        {
            string git = "git -C " + Pager.Quote(repo.Root) +
                " -c " + Pager.Quote("core.pager=LESS=R less") + " --paginate";

            // An untracked file has no blob to diff against, and `git diff` says nothing about
            // one. `--no-index` against the empty file is how git itself shows it: the whole
            // file as added, coloured and paged like any other diff.
            if (status == "??")
                return git + " diff --color=always --no-index -- /dev/null " + Pager.Quote(rel);

            // Against HEAD rather than the index or the worktree alone: what a reader means by
            // "what changed here" is both halves at once, which is also what the counts beside
            // the row are.
            string cmd = git + " diff --color=always HEAD";
            return rel == null ? cmd : cmd + " -- " + Pager.Quote(rel);
        }

        // ------------------------------------------------------------------ lifecycle

        public static void ReleaseViewer() => Viewer.Release();

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);

        static void ClearSelection() => _selected = null;
    }
}
