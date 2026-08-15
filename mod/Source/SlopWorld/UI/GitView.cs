using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Git view draws the daemon's whole changed-file list as a tree. It shares the Files view's
    // back pass, while the daemon runs `git` outside the sessions' mount namespaces.
    public static class GitView
    {
        // One column uses one font-derived row height.
        static float RowH => SlopWidgets.TinyRowH;
        const float IconW = 16f;
        const float Indent = 11f;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        const float ArrowW = 11f;

        // A directory holds children; a file holds the daemon's row.
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
            public bool Truncated;      // the summary is whole, but the drawn tree hit the daemon cap
            public int Changed, Added, Deleted;
            public Node Tree;
            public HashSet<string> Shut = new HashSet<string>();  // folded directories, by Rel

            // The same changes as the tree, flat and by relative path, with the porcelain pair
            // for each. The tree is for drawing; this is for answering the files view, which
            // asks about one absolute path at a time and would otherwise walk a tree per row
            // per frame.
            public Dictionary<string, string> Changes = new Dictionary<string, string>();
        }

        static readonly Dictionary<string, Repo> Repos = new Dictionary<string, Repo>();

        // Which project headings are rolled up. The files view keeps its own set for the same
        // reason: a tree's shape lives no longer than the process, and the agents view's folds
        // are about agents.
        static readonly HashSet<string> Shut = new HashSet<string>();

        static readonly SmoothScroll _scroll = new SmoothScroll();

        // The change the reader is looking at, and the ephemeral session paging its diff.
        // `_selected` is what the tree highlights; `Viewer` is who is showing it.
        static string _selected;
        static readonly Pager Viewer = new Pager();

        // And which of the two things about that change it is showing: the diff, or the file
        // the diff is about. The row and its buttons open different things about the same
        // path, so "click the one already open and its pane comes back" has to be about the
        // one that was clicked.
        static RowAct _showing;

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
                    repo.Changes.Clear();
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
                    repo.Truncated = j["truncated"].AsBool(false);
                    repo.Tree = Fold(repo, j["files"]);
                },
                msg =>
                {
                    repo.Loading = false;
                    repo.Asked = true;
                    if (repo.Dir != dir) return;
                    repo.Error = msg;
                    // The tree goes with it: what is drawn is the error alone, and a stale
                    // tree under a message about why it could not be read is two answers. The
                    // flat table goes for the same reason - the files view would otherwise
                    // offer a diff off a reading that failed.
                    repo.Tree = null;
                    repo.Changes.Clear();
                });
        }

        // The flat list of changed paths, folded into the tree it describes. The daemon sends
        // them sorted, so a directory's rows arrive together and the walk down never has to
        // look back; every interior node is a directory because something under it changed,
        // which is the whole difference between this tree and the files view's.
        static Node Fold(Repo repo, JVal files)
        {
            var root = new Node { IsDir = true, Depth = -1, Kids = new List<Node>(), Rel = "" };

            foreach (var f in files.Items)
            {
                string rel = f["path"].AsString();
                if (string.IsNullOrEmpty(rel)) continue;
                repo.Changes[rel] = f["status"].AsString();

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
            var view = new Rect(0f, 0f,
                body.width - (height > body.height ? SlopWidgets.ScrollbarW : 0f),
                height);

            // GUI rather than GUILayout, so this is safe in a pass that declines Layout
            // events - see AgentSidebar.DrawBack - and closed from a finally for the reason
            // the files view closes its own: a group left open is every window drawn after
            // it drawn somewhere else.
            _scroll.Begin(body, view);
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

        // Every project with somewhere to look that the strip's filter lets through,
        // ordered the way the other two views order their headings, so all three read as
        // the same column with different contents in it.
        static List<string> Projects()
        {
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir) && AgentSidebar.Passes(p.Name))
                    names.Add(p.Name);
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

            SlopWidgets.HoverRow(r);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, shut ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;

            // The count from the right, so the figures line up down the column the way the
            // agents view's times do. Only where the heading is folded: with the tree open
            // the rows are the count, and the state line under it says the rest.
            float rx = r.width - CellX;
            if (shut && repo.IsRepo && repo.Changed > 0)
            {
                GUI.color = SlopWidgets.Dim;
                var count = new Rect(r.width * 0.5f, r.y, rx - r.width * 0.5f, RowH);
                SlopWidgets.RowLabel(count, repo.Changed.ToString(), TextAnchor.MiddleRight);
                rx = count.x - 4f;
                GUI.color = SlopWidgets.Faint;
            }

            float lx = arrow.xMax + 4f;
            var label = new Rect(lx, r.y, rx - lx, RowH);
            SlopWidgets.RowLabel(label, project);

            Slab.Hairline(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                SlopWidgets.Edge);

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
                rx = Tail(rx, y, repo.Changed + (repo.Truncated ? "*" : ""), SlopWidgets.Dim);
            }

            GUI.color = SlopWidgets.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            var branch = new Rect(x, y, Mathf.Max(0f, rx - x - 4f), RowH);
            SlopWidgets.RowLabel(branch, repo.Branch ?? "");

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, repo.Changed == 0
                ? $"{repo.Root}\n\nNothing changed."
                : $"{repo.Root}\n\n{repo.Changed} changed, " +
                  $"{repo.Added} insertions(+), {repo.Deleted} deletions(-)" +
                  (repo.Truncated ? "\n\nThe tree shows the first 2,000 changes." : ""));
            return y + RowH;
        }

        // One figure laid out from the right, and the x the next one ends at. The anchor does
        // the measuring, so nothing here has to know how wide a number is.
        static float Tail(float right, float y, string text, Color color)
        {
            // A pixel either side of the measurement: a rect exactly as wide as its own
            // CalcSize clips the last glyph's overhang on a face whose advance is narrower
            // than its ink, which on a number is the whole of what there was to read.
            float w = SlopWidgets.Wide(text) + 2f;
            GUI.color = color;
            SlopWidgets.RowLabel(new Rect(right - w, y, w, RowH), text,
                TextAnchor.MiddleRight);
            return right - w - 5f;
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
            bool over = SlopWidgets.HoverRow(r);
            if (!node.IsDir && node.Rel == _selected)
                Slab.Fill(r, SlopWidgets.Hover);

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

            // Right-align counts; row actions temporarily occupy the same tail space.
            float rx = width - Pad;
            var acts = over ? Acts(node) : RowAct.None;
            if (acts != RowAct.None)
            {
                rx = RowActions.Draw(r, rx, acts) - 4f;
            }
            else if (!node.IsDir)
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
            SlopWidgets.RowLabel(label, node.Name);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Not while the mouse is on one of the buttons: that one states what it does, and
            // two tooltips over one row are drawn one under the other.
            if (!node.IsDir && RowActions.Hit(r, width - Pad, acts) == RowAct.None)
                TooltipHandler.TipRegion(r, $"{node.Rel}\n\n{Says(node)}");

            Lines.Add(new Line { Node = node, Repo = repo, Rect = r });
            return y + RowH;
        }

        // Every changed file offers Diff; existing files additionally offer Edit and text files
        // offer View.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;

            var acts = RowAct.Diff;
            if (!Present(node.Status)) return acts;

            acts |= RowAct.Edit;
            if (FilesView.IsText(node.Name)) acts |= RowAct.View;
            return acts;
        }

        // A path is absent when either worktree deletion or staged deletion is authoritative.
        static bool Present(string status)
        {
            if (string.IsNullOrEmpty(status) || status == "??") return true;
            char staged = status[0], worktree = status.Length > 1 ? status[1] : ' ';
            if (worktree == 'D') return false;
            return !(staged == 'D' && worktree == ' ');
        }

        // The porcelain pair said in one character, because one is what fits: the staged
        // letter where there is one, the unstaged letter otherwise. Which of the two it was
        // is the color's job below.
        static string Mark(string status)
        {
            if (string.IsNullOrEmpty(status)) return "?";
            if (status == "??") return "?";
            char staged = status[0];
            char worktree = status.Length > 1 ? status[1] : ' ';
            return (staged != ' ' && staged != '?' ? staged : worktree).ToString();
        }

        // Staged is the color of a thing that is going somewhere; everything else is the
        // color of a thing that is not. Untracked is neither, and is dimmer than both.
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
        // Called after layout and Grip; row rects are screen-adjusted before hit-testing.
        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            foreach (var line in Lines)
            {
                // Hit-test the scrolled, clipped row rect rather than its layout rect.
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
                else
                {
                    // Row actions take precedence over the row; directories have none.
                    var scr = Screen(line.Rect);
                    var hit = RowActions.Hit(scr, scr.xMax - Pad, Acts(line.Node));

                    if (hit != RowAct.None)
                    {
                        Act(line.Node, line.Repo, hit);
                    }
                    else if (line.Node.IsDir)
                    {
                        if (!line.Repo.Shut.Remove(line.Node.Rel))
                            line.Repo.Shut.Add(line.Node.Rel);
                        // A directory is not a diff selection.
                        ClearSelection();
                        Viewer.Release();
                    }
                    else
                    {
                        Open(line.Node, line.Repo);
                    }
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
            var body = AgentSidebar.TreeBody(AgentSidebar.Body, AgentSidebar.TabGit);
            var moved = new Rect(body.x + r.x - _scroll.Position.x, body.y + r.y - _scroll.Position.y,
                r.width, r.height);
            return moved.yMax <= body.y || moved.y >= body.yMax ? Rect.zero : moved;
        }

        static void Fold(string project)
        {
            if (!Shut.Remove(project)) Shut.Add(project);
        }

        // Left on a change: mark it and read its diff. Clicking the one already open just
        // brings its pane back - a focus change is a *different* row, and only that replaces
        // the pager.
        static void Open(Node node, Repo repo)
        {
            bool same = _selected == node.Rel && _showing == RowAct.Diff;
            _selected = node.Rel;
            if (!same || !Viewer.Reopen()) Diff(node, repo);
        }

        // One of the hover strip's three, done. The diff is what the row itself does; the
        // other two are the file the change is about, opened in the same errands the files
        // view opens them in - a reader in this view should not have to cross to the other one
        // to read the file a diff is about.
        static void Act(Node node, Repo repo, RowAct act)
        {
            string abs = Abs(repo, node);

            switch (act)
            {
                case RowAct.View:
                    // The file reader is owned by Files, not by this tree.
                    AgentSidebar.ShowFiles();
                    FilesView.ViewFile(repo.Project, abs, "view-" + node.Name);
                    break;

                case RowAct.Edit:
                    AgentSidebar.ShowFiles();
                    FilesView.EditFile(repo.Project, abs, "edit-" + node.Name);
                    break;

                case RowAct.Diff:
                    Open(node, repo);
                    break;
            }
        }

        // A row's path on disk. Git states everything relative to the repository root, which
        // is not always the project's directory, and every errand takes a path.
        static string Abs(Repo repo, Node node) =>
            (repo.Root ?? "").TrimEnd('/') + "/" + node.Rel;

        // ------------------------------------------------------------------ what the files
        //                                                                     view asks
        //
        // FilesView queries only the already-read repository cache; this path never fetches.

        // The repository as it stands for a project, or null where there is none to speak of:
        // never read, read and refused, not a repository, or read about a directory this
        // project no longer points at.
        static Repo Known(string project)
        {
            if (string.IsNullOrEmpty(project)) return null;
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";
            if (!Repos.TryGetValue(project, out var repo)) return null;
            return repo.Dir == dir && repo.IsRepo && repo.Error == null ? repo : null;
        }

        // The path relative to the repository root, or null for anything outside it.
        static string RelOf(Repo repo, string abs)
        {
            string root = repo.Root ?? "";
            if (root.Length == 0 || string.IsNullOrEmpty(abs)) return null;
            if (root == "/")
                return abs.StartsWith("/") && abs.Length > 1 ? abs.Substring(1) : null;
            root = root.TrimEnd('/');
            return abs.StartsWith(root + "/") ? abs.Substring(root.Length + 1) : null;
        }

        public static bool Changed(string project, string abs)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            return rel != null && repo.Changes.ContainsKey(rel);
        }

        // Returns a diff command for the cached path, without opening a pager.
        public static string DiffFor(string project, string abs)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            if (rel == null || !repo.Changes.TryGetValue(rel, out var status)) return null;
            return DiffCmd(repo, rel, status);
        }

        // ------------------------------------------------------------------ menus

        static void HeadMenu(string project, Repo repo)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Refresh", () => Fetch(project)),
            };

            if (repo.IsRepo && repo.Error == null && !string.IsNullOrEmpty(repo.Root))
            {
                opts.Add(new FloatMenuOption("Copy repository path", () => Copy(repo.Root)));
                // The whole tree's diff, in the same pager one file's opens in. Tracked as
                // the viewer, so the next row clicked replaces it.
                if (repo.Changed > 0)
                    opts.Add(new FloatMenuOption("Diff all", () =>
                    {
                        ClearSelection();
                        Viewer.Open(project, DiffCmd(repo, null, null), "diff-" + project);
                    }));
            }

            FilesView.AddFileActions(opts, project, repo.Dir, project);

            opts.Add(new FloatMenuOption("Terminal (host)", () =>
                SessionHub.Instance.RunHostShell(project,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        static void RowMenu(Node node, Repo repo)
        {
            string project = repo.Project;
            string abs = Abs(repo, node);
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(abs)),
                new FloatMenuOption("Copy relative path", () => Copy(node.Rel)),
            };

            FilesView.AddFileActions(opts, project, abs, node.Name);

            if (!node.IsDir && Present(node.Status))
                opts.Add(new FloatMenuOption("Edit", () =>
                {
                    AgentSidebar.ShowFiles();
                    FilesView.EditFile(project, abs, "edit-" + node.Name);
                }));

            opts.Add(new FloatMenuOption("Diff", () =>
            {
                // A directory's diff is every change under it and no one row's, so nothing
                // is marked; a file's is the row itself.
                if (node.IsDir) ClearSelection();
                else _selected = node.Rel;
                Diff(node, repo);
            }));

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        static void Copy(string text) =>
            SlopClient.Post("/api/clipboard", "{" + $"\"text\":{JVal.Q(text)}" + "}",
                _ => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                    false),
                SlopWidgets.Fail);

        // ------------------------------------------------------------------ the diff
        //
        // Diffs run in one Pager session through an ephemeral agent in the project sandbox.

        // Public for FilesView: a diff opened from the files tree still belongs to Git and
        // must therefore use this pager, so the resulting ghost appears in the Git tab.
        public static void OpenDiff(string project, string abs, string label)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            if (repo == null || rel == null || !repo.Changes.TryGetValue(rel, out var status))
            {
                SlopWidgets.Fail($"nothing to diff in {System.IO.Path.GetFileName(abs)}");
                return;
            }
            _selected = rel;
            _showing = RowAct.Diff;
            Viewer.Open(project, DiffCmd(repo, rel, status), label);
        }

        static void Diff(Node node, Repo repo)
        {
            _selected = node.Rel;
            _showing = RowAct.Diff;
            Viewer.Open(repo.Project, DiffCmd(repo, node.Rel, node.Status), "diff-" + node.Name);
        }

        // Use git's pager because daemon errands are argv, not shell pipelines. Force color
        // and LESS=R: git's default X avoids the alternate screen, preventing the pane from
        // sending wheel input to less; F would quit on short diffs. `-C` anchors paths when a
        // project points below the repository root.
        static string DiffCmd(Repo repo, string rel, string status)
        {
            string git = "git -C " + Pager.Quote(repo.Root) +
                " -c " + Pager.Quote("core.pager=LESS=R " + Pager.PipePager) + " --paginate";

            // An untracked file has no blob to diff against, and `git diff` says nothing about
            // one. `--no-index` against the empty file is how git itself shows it: the whole
            // file as added, colored and paged like any other diff.
            if (status == "??")
                return git + " diff --color=always --no-index -- /dev/null " + Pager.Quote(rel);

            // Against HEAD rather than the index or the worktree alone: what a reader means by
            // "what changed here" is both halves at once, which is also what the counts beside
            // the row are.
            string cmd = git + " diff --color=always HEAD";
            return rel == null ? cmd : cmd + " -- " + Pager.Quote(rel);
        }

        // ------------------------------------------------------------------ lifecycle

        public static void ReleaseViewer()
        {
            ClearSelection();
            Viewer.Release();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);

        static void ClearSelection()
        {
            _selected = null;
            _showing = RowAct.None;
        }
    }
}
