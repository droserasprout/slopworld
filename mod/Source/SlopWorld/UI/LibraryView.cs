using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw project-grouped library rows from AgentSidebar's back pass; the shared AddBar owns
    // creation and keeps the view usable over a terminal.
    public static class LibraryView
    {
        // Off the font, for the reason the other two trees' are.
        static float RowH => UiWidgets.TinyRowH;
        static float HeadH => UiWidgets.TinyRowH;
        const float Pad = UiWidgets.GapS;
        const float CellX = UiWidgets.GapS;
        const float ArrowW = UiWidgets.DisclosureW;

        // These are identity colors, not status colors: every kind stays recognizable without
        // borrowing the green/yellow/red language used for agent health and actions.
        static readonly Color PromptBadge = new Color(0.30f, 0.61f, 0.90f);
        static readonly Color ShellBadge = new Color(0.64f, 0.47f, 0.83f);
        static readonly Color BreadcrumbBadge = new Color(0.22f, 0.71f, 0.64f);
        static readonly Color FileActionBadge = new Color(0.85f, 0.42f, 0.66f);

        // The text that drives the rows, snapshotted once per frame so size and draw agree.
        static List<LibraryItemInfo> _items = new List<LibraryItemInfo>();

        // Grouped by project. Key "" is "no project".
        static readonly Dictionary<string, List<LibraryItemInfo>> Groups =
            new Dictionary<string, List<LibraryItemInfo>>();
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
        static LibraryItemInfo _selected;

        // The drawn lines, rebuilt each frame so clicks and drawing agree. Rects are in
        // screen space - see Screen - because Clicks runs outside the scroll view.
        struct Line
        {
            public LibraryItemInfo Item;
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
            using (WidgetState.Save())
            {
                // Builtins are daemon-owned and appear in their attached groups, so this list
                // contains only editable library items.
                // Filtered here rather than in [Group], so a filter that leaves nothing gets
                // the empty line instead of a blank column.
                _items = SessionHub.Instance.Library
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
                    UiScrollBody.ContentWidth(list, height),
                    height);

                // GUI rather than GUILayout, so this is safe in a pass that declines Layout
                // events - see AgentSidebar.DrawBack. Closed from a finally the way the files
                // view closes its own: a scroll view left open is every window drawn after it
                // drawn somewhere else.
                using (_scroll.Scope(list, view))
                {
                    float y = Pad;

                    foreach (var key in Order)
                        y += DrawGroup(view, y, key, Groups[key]);
                }
            }
        }

        static float DrawGroup(Rect view, float y, string key, List<LibraryItemInfo> bucket)
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
                    y += DrawLibraryRow(view, row, item);
                }
            return y - start;
        }

        static float DrawHeading(Rect view, Rect headRect, string key, string label,
            int count, bool folded)
        {
            // The key, not the label: the fold set is keyed by the group and the loose
            // bucket's key is "" while its label reads "no project".
            Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

            RowChrome.Hover(headRect, false, true, RowHoverPolicy.OverlayAware);

            Rect arrow;
            using (WidgetState.Save())
            {
                GUI.color = UiWidgets.Faint;
                arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                    ArrowW, ArrowW);
                GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float lx = arrow.xMax + UiWidgets.GapXS;
                string tail = folded ? "  " + count : "";
                var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
                UiWidgets.RowLabel(labelRect, label + tail);
            }

            Slab.Hairline(new Rect(CellX, headRect.yMax - 1f,
                view.width - CellX * 2f, 1f), UiWidgets.Edge);

            TooltipHandler.TipRegion(headRect,
                key.Length == 0
                    ? "Library entries that don't belong to any project.\n\nClick to fold."
                    : "Click to fold, right-click for the project.");

            return HeadH;
        }

        static float DrawLibraryRow(Rect view, Rect r, LibraryItemInfo item)
        {
            RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);

            // The kind badge: prompt, shell, or an attached breadcrumb.
            float badgeW = 34f;
            var badge = new Rect(CellX, r.y, badgeW, RowH);
            Rect nameRect;
            using (WidgetState.Save())
            {
                GUI.color = KindColor(item.Kind);
                // The whole row is Tiny, the way a row of the other two trees is: the badge was,
                // and the name and the sample beside it were Small in a row laid out for Tiny -
                // which on any face taller than the one it was written against is a line with its
                // descenders cut off.
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                UiWidgets.RowLabel(badge, KindCode(item.Kind));
                TooltipHandler.TipRegion(badge, KindName(item.Kind));

                float tx = CellX + badgeW + UiWidgets.GapXS;
                // The name comes first, then a sample of the text truncated.
                GUI.color = UiWidgets.Lead;
                var nameW = UiWidgets.Wide(item.Name);
                nameRect = new Rect(tx, r.y, Mathf.Min(nameW + 6f,
                    view.width * 0.35f), RowH);
                UiWidgets.RowLabel(nameRect, item.Name);
                GUI.color = UiWidgets.Dim;

                float restX = nameRect.xMax + 2f;
                var restW = r.xMax - 6f - restX;
                if (restW > 20f)
                    UiWidgets.RowLabel(new Rect(restX, r.y, restW, RowH),
                        OneLine(item.Text));
            }

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
            UiWidgets.StatusLabel(r, !SessionHub.Instance.Online
                ? $"daemon {SessionHub.Instance.Status}"
                : AgentSidebar.Filtering
                    ? $"No library entries in {AgentSidebar.FilterLabel}."
                    : "No library entries yet. Press + at the foot of the panel.",
                UiWidgets.Faint, GameFont.Tiny);
        }

        static Color KindColor(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Shell: return ShellBadge;
                case LibraryItemKind.Breadcrumb: return BreadcrumbBadge;
                case LibraryItemKind.FileAction: return FileActionBadge;
                default: return PromptBadge;
            }
        }

        static string KindCode(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Shell: return "sh";
                case LibraryItemKind.Breadcrumb: return "bc";
                case LibraryItemKind.FileAction: return WireContract.LibraryKind.Fa;
                default: return "pt";
            }
        }

        static string KindName(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Shell: return "Shell";
                case LibraryItemKind.Breadcrumb: return "Breadcrumb";
                case LibraryItemKind.FileAction: return "File Action";
                default: return "Prompt";
            }
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
                    Groups[key] = list = new List<LibraryItemInfo>();
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
                if (line.Item.Kind == LibraryItemKind.Breadcrumb || line.Item.Kind == LibraryItemKind.FileAction)
                    TerminalWindow.OpenOverPane(new EditLibraryItemDialog(line.Item));
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
                SessionHub.Instance.SessionStore.RunHostShell(project,
                    session => TerminalWindow.Open(session), UiWidgets.Fail)));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        static void RowMenu(LibraryItemInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary runnable items exist. Breadcrumbs are definitions only.
            if (s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            var where = Where(s);
            if (s.Link == LibraryItemLink.Ask)
                opts.Add(new UiSubmenu("Run in", () => WhereOptions(s)));

            opts.Add(new FloatMenuOption("Edit...", () =>
                TerminalWindow.OpenOverPane(new EditLibraryItemDialog(s))));

            opts.Add(new FloatMenuOption("Duplicate...", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(s))));

            opts.Add(new FloatMenuOption("Delete", () =>
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                    $"Remove library entry '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiWidgets.Fail),
                    destructive: true));
            }));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // Do not reopen AskWhere after resolving a temporary project: `temp` marks the return
        // path where `project == null` is intentional.
        static void Run(LibraryItemInfo s, string project = null, bool temp = false)
        {
            // An entry that never said where goes through a menu first.
            if (s.Link == LibraryItemLink.Ask && project == null && !temp)
            {
                AskWhere(s);
                return;
            }

            bool scratch = temp || s.Link == LibraryItemLink.Temp;
            SessionHub.Instance.SessionStore.RunLibraryItem(s.Name,
                session => TerminalWindow.Open(session),
                UiWidgets.Fail,
                // A project named outright wins; a temporary run has none, whichever of
                // the two said so; otherwise the entry's own.
                project ?? (scratch ? null : s.Project),
                scratch, Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular. Hung off the row's own menu where there is one, and
        // opened as a menu of its own where the run was asked for from somewhere else.
        static List<FloatMenuOption> WhereOptions(LibraryItemInfo s)
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

        static void AskWhere(LibraryItemInfo s) =>
            TerminalWindow.OpenOverPane(new UiMenu(WhereOptions(s)));

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(LibraryItemInfo s)
        {
            switch (s.Link)
            {
                case LibraryItemLink.Temp: return "a temporary project";
                case LibraryItemLink.Ask: return "run in...";
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

}
