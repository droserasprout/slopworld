using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw project-grouped shortcut rows from AgentSidebar's back pass; the shared AddBar owns
    // creation and keeps the view usable over a terminal.
    public static class ShortcutsView
    {
        // Off the font, for the reason the other two trees' are.
        static float RowH => SlopWidgets.TinyRowH;
        static float HeadH => SlopWidgets.TinyRowH;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        const float ArrowW = 11f;

        // The text that drives the rows, snapshotted once per frame so size and draw agree.
        static List<ShortcutInfo> _items = new List<ShortcutInfo>();

        // Grouped by project. Key "" is "no project".
        static readonly Dictionary<string, List<ShortcutInfo>> Groups =
            new Dictionary<string, List<ShortcutInfo>>();
        static readonly List<string> Order = new List<string>();
        const string Loose = "";

        // Which headings are rolled up. Kept in memory only, the way the files view keeps
        // its folds and the agents view keeps its own - a fold is about the view, not the
        // colony, so it does not belong in a save.
        static readonly HashSet<string> Folded = new HashSet<string>();

        public static bool AllFolded => Order.Count > 0 && Order.All(Folded.Contains);

        public static void SetAllFolded(bool folded)
        {
            Folded.Clear();
            if (folded)
                foreach (var key in Order) Folded.Add(key);
        }

        // The selected row, for the RMB menu.
        static ShortcutInfo _selected;

        // The drawn lines, rebuilt each frame so clicks and drawing agree. Rects are in
        // screen space - see Screen - because Clicks runs outside the scroll view.
        struct Line
        {
            public ShortcutInfo Item;
            public bool Head;    // true on a heading, false on a row
            public string Key;   // the group's key on a heading; "" is the loose bucket
            public Rect Rect;
        }
        static readonly List<Line> Lines = new List<Line>();

        // ------------------------------------------------------------------ drawing

        // The body is only the clipped list; shared AddBar supplies the plus button, and Screen
        // converts scrolled rows to hit-test coordinates.
        static Rect Screen(Rect r)
        {
            var list = AgentSidebar.Body;
            var moved = new Rect(list.x + r.x, list.y + r.y - _scroll.Position.y, r.width, r.height);
            return moved.yMax <= list.y || moved.y >= list.yMax ? Rect.zero : moved;
        }

        static readonly SmoothScroll _scroll = new SmoothScroll();

        public static void Draw(Rect body)
        {
            // Builtins are shipped with the daemon and there is nothing to do to one here -
            // no run, no edit, no delete. They are offered where they are attached instead.
            // Filtered here rather than in [Group], so a filter that leaves nothing gets
            // the empty line instead of a blank column.
            _items = SessionHub.Instance.Shortcuts
                .Where(s => !s.Builtin && AgentSidebar.Passes(s.Project)).ToList();
            Lines.Clear();

            if (_items.Count == 0)
            {
                Empty(body);
                return;
            }

            Group();

            var list = body;
            float height = Measure();
            var view = new Rect(0f, 0f,
                list.width - (height > list.height ? SlopWidgets.ScrollbarW : 0f),
                height);

            // GUI rather than GUILayout, so this is safe in a pass that declines Layout
            // events - see AgentSidebar.DrawBack. Closed from a finally the way the files
            // view closes its own: a scroll view left open is every window drawn after it
            // drawn somewhere else.
            _scroll.Begin(list, view);
            try
            {
                float y = Pad;

                foreach (var key in Order)
                    y += DrawGroup(view, y, key, Groups[key]);
            }
            finally
            {
                _scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        static float DrawGroup(Rect view, float y, string key, List<ShortcutInfo> bucket)
        {
            float start = y;
            bool folded = Folded.Contains(key);
            string label = key.Length == 0 ? LooseLabel : key;
            var headRect = new Rect(0f, y, view.width, HeadH);
            y += DrawHeading(view, headRect, key, label, bucket.Count, folded);
            if (!folded)
                foreach (var item in bucket)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    y += DrawShortcutRow(view, row, item);
                }
            return y - start;
        }

        static float DrawHeading(Rect view, Rect headRect, string key, string label,
            int count, bool folded)
        {
            // The key, not the label: the fold set is keyed by the group and the loose
            // bucket's key is "" while its label reads "no project".
            Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

            SlopWidgets.HoverRow(headRect);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                ArrowW, ArrowW);
            GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float lx = arrow.xMax + 4f;
            string tail = folded ? "  " + count : "";
            var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
            SlopWidgets.RowLabel(labelRect, label + tail);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Slab.Hairline(new Rect(CellX, headRect.yMax - 1f,
                view.width - CellX * 2f, 1f), SlopWidgets.Edge);

            TooltipHandler.TipRegion(headRect,
                key.Length == 0
                    ? "Shortcuts that don't belong to any project.\n\nClick to fold."
                    : "Click to fold, right-click for the project.");

            return HeadH;
        }

        static float DrawShortcutRow(Rect view, Rect r, ShortcutInfo item)
        {
            SlopWidgets.HoverRow(r);

            // The kind badge: prompt, shell, or an attached breadcrumb.
            float badgeW = 34f;
            GUI.color = item.Kind == ShortcutKind.Shell
                ? SlopWidgets.Warn
                : SlopWidgets.Info;
            // The whole row is Tiny, the way a row of the other two trees is: the badge was,
            // and the name and the sample beside it were Small in a row laid out for Tiny -
            // which on any face taller than the one it was written against is a line with its
            // descenders cut off.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            SlopWidgets.RowLabel(new Rect(CellX, r.y, badgeW, RowH),
                item.Kind == ShortcutKind.Shell ? "sh" :
                    item.Kind == ShortcutKind.Breadcrumb ? "bc" :
                    item.Kind == ShortcutKind.FileAction ? "fa" : "pt");
            GUI.color = Color.white;

            float tx = CellX + badgeW + 4f;
            // The name comes first, then a sample of the text truncated.
            GUI.color = SlopWidgets.Lead;
            var nameW = SlopWidgets.Wide(item.Name);
            var nameRect = new Rect(tx, r.y, Mathf.Min(nameW + 6f,
                view.width * 0.35f), RowH);
            SlopWidgets.RowLabel(nameRect, item.Name);
            GUI.color = SlopWidgets.Dim;

            float restX = nameRect.xMax + 2f;
            var restW = r.xMax - 6f - restX;
            if (restW > 20f)
                SlopWidgets.RowLabel(new Rect(restX, r.y, restW, RowH),
                    OneLine(item.Text));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Lines.Add(new Line { Item = item, Rect = Screen(r) });
            return RowH;
        }

        // The height the rows want, measured off the same folds the draw reads.
        static float Measure()
        {
            float h = Pad;
            foreach (var key in Order)
            {
                h += HeadH;
                if (!Folded.Contains(key)) h += Groups[key].Count * RowH;
            }
            return h + Pad;
        }

        static void Empty(Rect body)
        {
            var r = new Rect(CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            GUI.color = SlopWidgets.Faint;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(r, !SessionHub.Instance.Online
                ? $"daemon {SessionHub.Instance.Status}"
                : AgentSidebar.Filtering
                    ? $"No shortcuts in {AgentSidebar.FilterLabel}."
                    : "No shortcuts yet. Press + at the foot of the panel.");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        // Group the items by project, the way the agents view groups by project.
        static void Group()
        {
            foreach (var list in Groups.Values) list.Clear();
            Order.Clear();

            foreach (var item in _items)
            {
                string key = string.IsNullOrEmpty(item.Project) ? Loose : item.Project;
                if (!Groups.TryGetValue(key, out var list))
                    Groups[key] = list = new List<ShortcutInfo>();
                list.Add(item);
            }

            foreach (var kv in Groups)
                if (kv.Value.Count > 0) Order.Add(kv.Key);

            // Alphabetical, with the loose ones last - same as the agents view.
            Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            foreach (var list in Groups.Values) list.Sort((a, b) =>
                string.CompareOrdinal(a?.Name ?? "", b?.Name ?? ""));
        }

        static string LooseLabel => "no project";

        // ------------------------------------------------------------------ clicks
        //
        // Called from AgentSidebar's back pass where Menus is in the agents view and
        // Clicks is in the files/git views.

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            // The "+" is the column's, at the foot of the panel below this body, and
            // AgentSidebar answers it before this is ever asked.
            foreach (var line in Lines)
            {
                // Screen() zeroes a line clipped out of the scroll view, and Rect.zero is
                // nowhere the mouse can be.
                if (!ColonistBarStrip.MouseOver(line.Rect)) continue;

                if (line.Head)
                {
                    // Heading: left click folds, right click opens project menu. Both keyed
                    // by the group, not by what the heading reads.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key)) Folded.Add(line.Key);
                    }
                    else if (line.Key.Length > 0)
                    {
                        HeadMenu(line.Key);
                    }
                    e.Use();
                    return;
                }

                if (e.button == 1)
                {
                    _selected = line.Item;
                    RowMenu(line.Item);
                    e.Use();
                    return;
                }

                // Breadcrumbs are attached definitions, not errands. A click edits them;
                // prompt and shell entries still run as before.
                e.Use();
                if (line.Item.Kind == ShortcutKind.Breadcrumb || line.Item.Kind == ShortcutKind.FileAction)
                    TerminalWindow.OpenOverPane(new EditShortcutDialog(line.Item));
                else
                    Run(line.Item);
                return;
            }
        }

        // ------------------------------------------------------------------ menus

        static void HeadMenu(string project)
        {
            var p = SessionHub.Instance.Project(project);
            if (p == null) return;

            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(p))),
            };

            opts.Add(new FloatMenuOption("Terminal (host)", () =>
                SessionHub.Instance.RunHostShell(project,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        static void RowMenu(ShortcutInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary shortcuts exist. Breadcrumbs are definitions only.
            if (s.Kind != ShortcutKind.Breadcrumb && s.Kind != ShortcutKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            var where = Where(s);
            if (s.Link == ShortcutLink.Ask)
                opts.Add(new SlopSubmenu("Run in", () => WhereOptions(s)));

            opts.Add(new FloatMenuOption("Edit...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(s))));

            opts.Add(new FloatMenuOption("Duplicate...", () =>
                TerminalWindow.OpenOverPane(EditShortcutDialog.Copy(s))));

            opts.Add(new FloatMenuOption("Delete", () =>
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail),
                    destructive: true));
            }));

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // Do not reopen AskWhere after resolving a temporary project: `temp` marks the return
        // path where `project == null` is intentional.
        static void Run(ShortcutInfo s, string project = null, bool temp = false)
        {
            // An entry that never said where goes through a menu first.
            if (s.Link == ShortcutLink.Ask && project == null && !temp)
            {
                AskWhere(s);
                return;
            }

            bool scratch = temp || s.Link == ShortcutLink.Temp;
            SessionHub.Instance.RunShortcut(s.Name,
                session => TerminalWindow.Open(session),
                SlopWidgets.Fail,
                // A project named outright wins; a temporary run has none, whichever of
                // the two said so; otherwise the entry's own.
                project ?? (scratch ? null : s.Project),
                scratch, Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular. Hung off the row's own menu where there is one, and
        // opened as a menu of its own where the run was asked for from somewhere else.
        static List<FloatMenuOption> WhereOptions(ShortcutInfo s)
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => Run(s, p.Name)))
                .ToList();

            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(s, null, true)));

            return options;
        }

        static void AskWhere(ShortcutInfo s) =>
            TerminalWindow.OpenOverPane(new SlopMenu(WhereOptions(s)));

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(ShortcutInfo s)
        {
            switch (s.Link)
            {
                case ShortcutLink.Temp: return "a temporary project";
                case ShortcutLink.Ask: return "run in...";
                default:
                    return string.IsNullOrEmpty(s.Project)
                        ? "no project"
                        : s.Project;
            }
        }

        // The first line of a prompt, which is all a row has space for.
        static string OneLine(string text)
        {
            text = (text ?? "").Replace("\r", "");
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl) + " ...";
        }
    }

    // The command box is greyed rather than hidden when it is empty, so the thing
    // that will run is on screen even when nothing here chose it.
    public class EditShortcutDialog : SlopWindow
    {
        sealed class ShortcutKindDescriptor
        {
            public readonly string ButtonLabel;
            public readonly bool ShowWhere;
            public readonly Func<EditShortcutDialog, bool> ShowProject;
            public readonly Func<EditShortcutDialog, string> ProjectLabel;
            public readonly Func<EditShortcutDialog, string> ProjectValue;
            public readonly Action<EditShortcutDialog> PickProject;
            public readonly Func<EditShortcutDialog, ProjectInfo, string> ExplainText;
            public readonly string CommandLabel;
            public readonly Func<EditShortcutDialog, string> CommandPlaceholder;

            public ShortcutKindDescriptor(string buttonLabel, bool showWhere,
                Func<EditShortcutDialog, bool> showProject,
                Func<EditShortcutDialog, string> projectLabel,
                Func<EditShortcutDialog, string> projectValue,
                Action<EditShortcutDialog> pickProject,
                Func<EditShortcutDialog, ProjectInfo, string> explainText,
                string commandLabel, Func<EditShortcutDialog, string> commandPlaceholder)
            {
                ButtonLabel = buttonLabel;
                ShowWhere = showWhere;
                ShowProject = showProject;
                ProjectLabel = projectLabel;
                ProjectValue = projectValue;
                PickProject = pickProject;
                ExplainText = explainText;
                CommandLabel = commandLabel;
                CommandPlaceholder = commandPlaceholder;
            }
        }

        static readonly Dictionary<ShortcutKind, ShortcutKindDescriptor> KindDescriptors =
            new Dictionary<ShortcutKind, ShortcutKindDescriptor>
            {
                {
                    ShortcutKind.Prompt,
                    new ShortcutKindDescriptor(
                        "Prompt - say something to an agent", true,
                        dialog => dialog._s.Link != ShortcutLink.Ask,
                        dialog => dialog._s.Link == ShortcutLink.Temp
                            ? "Sandbox to copy (blank = plain: private network, no presets)"
                            : "Project (the directory and sandbox it runs in)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == ShortcutLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        dialog => dialog.PickProject(),
                        (dialog, project) => dialog.Explain(project),
                        "Agent (blank = the default)", dialog => dialog._agentDefault)
                },
                {
                    ShortcutKind.Shell,
                    new ShortcutKindDescriptor(
                        "Shell - run a command", true,
                        dialog => dialog._s.Link != ShortcutLink.Ask,
                        dialog => dialog._s.Link == ShortcutLink.Temp
                            ? "Sandbox to copy (blank = plain: private network, no presets)"
                            : "Project (the directory and sandbox it runs in)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == ShortcutLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        dialog => dialog.PickProject(),
                        (dialog, project) => dialog.Explain(project),
                        "Shell (blank = the default)", dialog => dialog._shellDefault)
                },
                {
                    ShortcutKind.Breadcrumb,
                    new ShortcutKindDescriptor(
                        "Breadcrumb - append to the first prompt", false,
                        dialog => true,
                        dialog => "Project (attach to every agent in this project)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project) ? "None" : dialog._s.Project,
                        dialog => dialog.PickBreadcrumbProject(),
                        (dialog, project) =>
                            "Attach this text to projects and agents; it is not runnable.",
                        null, null)
                },
                {
                    ShortcutKind.FileAction,
                    new ShortcutKindDescriptor(
                        "File action - run on a Files row", false,
                        dialog => false,
                        null, null, null,
                        (dialog, project) =>
                            "This command is offered by the Files sidebar; use {{ absolute_path }} or {{ relative_path }}.",
                        "Command (path is appended unless substituted)", dialog => dialog._agentDefault)
                },
            };

        readonly bool _isNew;
        readonly ShortcutInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;
        // Set only for a duplicate, so the title can distinguish copying from editing.
        readonly string _copiedFrom;

        // Asked for rather than assumed: `[defaults] shell` is a per-machine answer and
        // this dialog would otherwise print somebody else's.
        string _agentDefault = "claude";
        string _shellDefault = "bash";

        public EditShortcutDialog(ShortcutInfo existing) : this(existing, false) { }

        public static EditShortcutDialog Copy(ShortcutInfo of) =>
            new EditShortcutDialog(of, true);

        EditShortcutDialog(ShortcutInfo existing, bool copy)
        {
            // A duplicate is a new daemon entry: it must POST rather than PUT, and its
            // name is suggested rather than copied so saving it cannot collide by default.
            _isNew = existing == null || copy;
            _origName = copy ? "" : (existing?.Name ?? "");
            _copiedFrom = copy ? existing.Name : null;
            _s = existing?.Copy() ?? new ShortcutInfo();
            if (copy)
                _s.Name = SlopWidgets.FreeName(_s.Name,
                    SessionHub.Instance.Shortcuts.Select(s => s.Name), "shortcut");


            SessionHub.Instance.RefreshProjects();
            SlopClient.Get("/api/config", j =>
            {
                var d = j["values"]["defaults"];
                _agentDefault = d["agent"].AsString("claude");
                _shellDefault = d["shell"].AsString("bash");
            });
        }

        public EditShortcutDialog(ShortcutKind kind) : this(null)
        {
            _s.Kind = kind;
            if (kind == ShortcutKind.Breadcrumb || kind == ShortcutKind.FileAction)
            {
                _s.Link = ShortcutLink.Project;
                _s.Project = "";
            }
        }

        // What is left at the bottom is the prompt box - the one field here somebody
        // writes paragraphs in, and the one that gets squeezed when anything above grows.
        public override Vector2 InitialSize => new Vector2(560f, 660f);

        protected override void DoBody(Rect rect)
        {
            // One column, on the room it has: a Listing_Standard begun on a rect too short
            // for its contents does not overflow, it breaks to a column off the right-hand
            // edge and puts CurHeight back to nearly zero - and the prompt box below is
            // placed and sized from that number. See EditProjectDialog.DoFields.
            SlopWidgets.Title(rect, _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New shortcut" : $"Edit '{_origName}'");

            float head = SlopWidgets.HeaderH + SlopWidgets.GapS;
            float used = DrawFields(new Rect(rect.x, rect.y + head, rect.width, rect.height - head));
            float y = rect.y + head + used + SlopWidgets.GapL;
            DrawTextEditor(rect, y);
            DrawFooter(rect);
        }

        float DrawFields(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            var kind = KindDescriptors[_s.Kind];
            DrawName(l);
            DrawKindAndLink(l, kind);
            DrawProject(l, kind);
            DrawExplanation(l, kind);
            DrawCommand(l, kind);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        void DrawName(Listing_Standard l)
        {
            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = SlopWidgets.Field(l, "shortcut.name", _s.Name);
        }

        void DrawKindAndLink(Listing_Standard l, ShortcutKindDescriptor kind)
        {
            l.Gap(SlopWidgets.GapS);
            l.Label("Kind");
            if (SlopWidgets.Button(l, kind.ButtonLabel))
                PickKind();

            if (!kind.ShowWhere) return;
            l.Gap(SlopWidgets.GapS);
            l.Label("Where it runs");
            if (SlopWidgets.Button(l, LinkLabel(_s.Link)))
                PickLink();
        }

        void DrawProject(Listing_Standard l, ShortcutKindDescriptor kind)
        {
            // The project dropdown stays up for every kind that uses a project. In temp mode
            // it still answers which sandbox the scratch project is given.
            if (!kind.ShowProject(this)) return;
            l.Gap(SlopWidgets.GapS);
            l.Label(kind.ProjectLabel(this));
            if (SlopWidgets.Button(l, kind.ProjectValue(this)))
                kind.PickProject(this);
        }

        void DrawExplanation(Listing_Standard l, ShortcutKindDescriptor kind)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = SlopWidgets.Dim;
            l.Label(kind.ExplainText(this, project));
            GUI.color = Color.white;
        }

        void DrawCommand(Listing_Standard l, ShortcutKindDescriptor kind)
        {
            l.Gap(SlopWidgets.GapS);
            if (kind.CommandLabel == null)
            {
                // Breadcrumbs have one text editor below, just like prompts. Keeping a
                // second Area here caused the lower editor to overwrite this value.
                _s.Command = "";
                return;
            }

            l.Label(kind.CommandLabel);
            var box = l.GetRect(SlopWidgets.FieldH);
            if (!string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                _s.Command = SlopWidgets.Field(box, "shortcut.command", _s.Command);
                return;
            }

            string placeholder = kind.CommandPlaceholder(this);
            GUI.color = SlopWidgets.Faint;
            string shown = SlopWidgets.Field(box, "shortcut.command", placeholder);
            GUI.color = Color.white;
            if (shown != placeholder) _s.Command = shown;
        }

        float DrawTextEditor(Rect rect, float y)
        {
            SlopWidgets.SectionHeading(new Rect(rect.x, y, rect.width, SlopWidgets.RowH),
                _s.Kind == ShortcutKind.Shell || _s.Kind == ShortcutKind.FileAction ? "Command line" :
                _s.Kind == ShortcutKind.Breadcrumb ? "Breadcrumb text" : "Prompt");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            if (_s.Kind != ShortcutKind.FileAction)
            {
                var area = new Rect(rect.x, y, rect.width,
                    rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS - y);
                _s.Text = SlopWidgets.Area(area, "shortcut.text", _s.Text ?? "");
            }
            else
            {
                _s.Text = "";
            }
            return rect.yMax - y;
        }

        void DrawFooter(Rect rect)
        {
            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save();
        }

        // The three answers, in the words the dropdown shows them in.
        public static string LinkLabel(ShortcutLink l)
        {
            switch (l)
            {
                case ShortcutLink.Temp: return "A new temporary project each run";
                case ShortcutLink.Ask: return "Ask me every time";
                default: return "One project, named below";
            }
        }

        // What this errand will actually do with the ground it is given, which is the
        // part the two dropdowns together do not say outright.
        string Explain(ProjectInfo project)
        {
            switch (_s.Link)
            {
                case ShortcutLink.Temp:
                    return $"Each run gets an empty directory under {ProjectInfo.TempRoot}" +
                           (project != null
                               ? $", sandboxed like '{project.Name}'."
                               : ". Nothing deletes it; the machine clears /tmp.");
                case ShortcutLink.Ask:
                    return "Running it opens a list of projects, plus a temporary one.";
                default:
                    return project != null
                        ? $"{project.Dir}  ({ProjectsView.Summary(project)})"
                        : SessionHub.Instance.Projects.Count == 0
                            ? "No projects yet - make one in the Projects window first."
                            : "";
            }
        }

        void PickLink()
        {
            TerminalWindow.OpenOverPane(new SlopMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(LinkLabel(ShortcutLink.Project),
                    () => _s.Link = ShortcutLink.Project),
                new FloatMenuOption(LinkLabel(ShortcutLink.Temp),
                    () => _s.Link = ShortcutLink.Temp),
                new FloatMenuOption(LinkLabel(ShortcutLink.Ask),
                    () => _s.Link = ShortcutLink.Ask),
            }));
        }

        void PickKind()
        {
            TerminalWindow.OpenOverPane(new SlopMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Prompt - say something to an agent",
                    () => _s.Kind = ShortcutKind.Prompt),
                new FloatMenuOption("Shell - run a command",
                    () => _s.Kind = ShortcutKind.Shell),
                new FloatMenuOption("Breadcrumb - append to the first prompt",
                    () => { _s.Kind = ShortcutKind.Breadcrumb; _s.Link = ShortcutLink.Project; _s.Project = ""; }),
                new FloatMenuOption("File action - run on a Files row",
                    () => { _s.Kind = ShortcutKind.FileAction; _s.Link = ShortcutLink.Project; _s.Project = ""; _s.Text = ""; }),
            }));
        }

        void PickBreadcrumbProject()
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name))
                .ToList();
            options.Insert(0, new FloatMenuOption("None", () => _s.Project = ""));
            options.Add(new FloatMenuOption("New project...",
                () => TerminalWindow.OpenOverPane(new EditProjectDialog(null))));
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        void PickProject()
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name))
                .ToList();

            // Only where it means something: in temp mode the project is the sandbox to copy,
            // and copying nobody's is a real answer.
            if (_s.Link == ShortcutLink.Temp)
                options.Insert(0, new FloatMenuOption("None", () => _s.Project = ""));

            options.Add(new FloatMenuOption("New project...",
                () => TerminalWindow.OpenOverPane(new EditProjectDialog(null))));

            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_s.Name ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a shortcut needs a name.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Kind != ShortcutKind.Breadcrumb && _s.Kind != ShortcutKind.FileAction && _s.Link == ShortcutLink.Project &&
                string.IsNullOrEmpty((_s.Project ?? "").Trim()))
            {
                Messages.Message("SlopWorld: pick a project, or a way to choose one.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Kind == ShortcutKind.FileAction && string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a file action needs a command.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Kind != ShortcutKind.FileAction && string.IsNullOrEmpty((_s.Text ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a shortcut needs something to send.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            SessionHub.Instance.SaveShortcut(_s, _isNew, _origName,
                ok: () => Close(),
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }
}
