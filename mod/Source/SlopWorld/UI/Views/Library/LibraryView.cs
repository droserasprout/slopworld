using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw type-grouped library rows from AgentSidebar's back pass; the shared AddBar owns
    // creation and keeps the view usable over a terminal.
    public static class LibraryView
    {
        // Off the font, for the reason the other two trees' are.
        static float RowH => UiTheme.TinyRowH;
        static float HeadH => UiTheme.TinyRowH;
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;
        const float ArrowW = UiTheme.DisclosureW;

        // The text that drives the rows, snapshotted once per frame so size and draw agree.
        static List<LibraryItemInfo> _items = new List<LibraryItemInfo>();

        static readonly Dictionary<LibraryItemInfo, AgentTemplateInfo> Templates =
            new Dictionary<LibraryItemInfo, AgentTemplateInfo>();

        public static void Refresh(Action<string> fail = null)
        {
            SessionHub.Instance.Catalog.RefreshLibrary(fail);
            SessionHub.Instance.Catalog.RefreshTemplates(fail);
        }

        // Grouped by catalog kind; scope remains a separate sidebar filter.
        static readonly Dictionary<string, List<LibraryItemInfo>> Groups =
            new Dictionary<string, List<LibraryItemInfo>>();
        static readonly List<string> Order = new List<string>();
        static readonly string[] Kinds = { "Agent templates", "Prompts", "Shell commands", "Breadcrumbs", "File actions" };
        static string _query = "";
        static string _kind = "";
        static string _selection;
        static bool _revealSelection;
        static Rect _list;
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static string GroupKey(LibraryItemInfo item) => Templates.ContainsKey(item)
            ? Kinds[0] : item.Kind == LibraryItemKind.Shell ? Kinds[2]
            : item.Kind == LibraryItemKind.Breadcrumb ? Kinds[3]
            : item.Kind == LibraryItemKind.FileAction ? Kinds[4] : Kinds[1];

        static string Identity(LibraryItemInfo item) =>
            (Templates.ContainsKey(item) ? "template:" : "item:") + item.Name;

        public static void Closed()
        {
            _fieldLifetime.Cancel();
            _fieldLifetime = new FieldLifetime();
            if (GUI.GetNameOfFocusedControl() == "library.query") GUI.FocusControl(null);
        }

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

        // Resolve selection by catalog identity after each refresh.
        static LibraryItemInfo _selected;

        // The drawn lines, rebuilt each frame so clicks and drawing agree. Rects are in
        // screen space - see Screen - because Clicks runs outside the scroll view.
        struct Line
        {
            public LibraryItemInfo Item;
            public bool Head;    // true on a heading, false on a row
            public string Key;   // kind heading
            public Rect Rect;
        }
        static readonly List<Line> Lines = new List<Line>();

        // ------------------------------------------------------------------ drawing

        // Only the list scrolls. Clip hit targets to its viewport so partially visible
        // rows cannot intercept search or detail actions. The shared AddBar owns creation.
        static Rect Screen(Rect r)
        {
            var list = _list;
            var moved = new Rect(list.x + r.x, list.y + r.y - _scroll.Position.y, r.width, r.height);
            float top = Mathf.Max(moved.y, list.y);
            float bottom = Mathf.Min(moved.yMax, list.yMax);
            return bottom <= top ? Rect.zero : new Rect(moved.x, top, moved.width, bottom - top);
        }

        static readonly SmoothScroll _scroll = new SmoothScroll();
        static float _contentHeight;

        public static void Draw(Rect body)
        {
            Lines.Clear();
            if (_scroll.HandleWheel(_list, _contentHeight)) return;
            using (FieldLifetimeScope.Push(_fieldLifetime))
            using (WidgetState.Save())
            {
                // Global definitions stay available while project-specific entries follow
                // the shared filter. Builtins remain in their attached menus.
                _items = SessionHub.Instance.Library
                    .Where(s => !s.Builtin && (string.IsNullOrEmpty(s.Project) || AgentSidebar.Passes(s.Project))).ToList();
                Templates.Clear();
                foreach (var template in SessionHub.Instance.Templates)
                {
                    var row = new LibraryItemInfo
                    {
                        Name = template.Name,
                        Project = "",
                        Text = template.Description
                    };
                    Templates[row] = template;
                    _items.Add(row);
                }
                DrawTools(body);
                _items = _items.Where(item => (_kind.Length == 0 || GroupKey(item) == _kind) &&
                    (item.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (item.Text ?? "").IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                _selected = _items.FirstOrDefault(item => Identity(item) == _selection);
                float top = body.y + 2f * (UiTheme.FieldH + Pad) + Pad;
                float available = Mathf.Max(0f, body.yMax - top);
                float detailHeight = _selected == null ? 0f :
                    Mathf.Min(RowH * 5f + UiTheme.FieldH * 2f + Pad * 4f,
                        Mathf.Max(0f, available - RowH * 2f));
                _list = new Rect(body.x, top, body.width, Mathf.Max(0f, available - detailHeight));
                if (_selected != null && detailHeight > 0f)
                    DrawDetails(new Rect(body.x + CellX, _list.yMax,
                        body.width - CellX * 2f, detailHeight), _selected);
                Group();
                if (_items.Count == 0)
                {
                    _contentHeight = 0f;
                    Empty(_list);
                    return;
                }
                var list = _list;
                if (_revealSelection && _selected != null)
                {
                    float y = Pad;
                    foreach (var key in Order)
                    {
                        y += HeadH;
                        if (Folded.Contains(key)) continue;
                        foreach (var item in Groups[key])
                        {
                            if (Identity(item) == _selection)
                                _scroll.Reveal(y, RowH, list.height);
                            y += RowH;
                        }
                    }
                    _revealSelection = false;
                }
                float height = Measure();
                _contentHeight = height;
                var geometry = UiScrollBody.Measure(list, height,
                    UiScrollbarReservation.WhenNeeded);
                var view = geometry.View;

                // AgentSidebar.DrawBack skips Layout events, so use GUI rather than GUILayout.
                using (_scroll.Scope(list, view))
                {
                    float y = Pad;

                    foreach (var key in Order)
                        y += DrawGroup(view, y, key, Groups[key]);
                }
            }
        }

        static void DrawTools(Rect body)
        {
            var field = new Rect(body.x + CellX, body.y + Pad,
                Mathf.Max(0f, body.width - CellX * 2f), UiTheme.FieldH);
            var e = Event.current;
            if ((e.type == EventType.MouseDown && !field.Contains(e.mousePosition)) ||
                (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
                Closed();
            string before = _query;
            _query = UiText.Field(field, "library.query", _query);
            if (before != _query) _scroll.JumpTo(Vector2.zero);
            if (_query.Length == 0 && GUI.GetNameOfFocusedControl() != "library.query")
                UiText.StatusLabel(field.ContractedBy(Pad, 0f), "Search library…", UiTheme.Faint, GameFont.Tiny);
            TooltipHandler.TipRegion(field, "Search library names and content");
            var filter = new Rect(field.x, field.yMax + Pad, field.width, UiTheme.FieldH);
            if (UiButtons.Button(filter, _kind.Length == 0 ? "All types ▾" : _kind + " ▾"))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("All types", () => SetKind(""))
                };
                foreach (var kind in Kinds)
                {
                    string value = kind;
                    options.Add(new FloatMenuOption(value, () => SetKind(value)));
                }
                TerminalWindow.OpenOverPane(new UiMenu(options));
            }
        }

        static void SetKind(string kind)
        {
            _kind = kind;
            _scroll.JumpTo(Vector2.zero);
        }

        static bool Runnable(LibraryItemInfo item) =>
            !Templates.ContainsKey(item) &&
            (item.Kind == LibraryItemKind.Prompt || item.Kind == LibraryItemKind.Shell);

        public static void OpenQuickAccessMenu(Rect button)
        {
            var options = new List<FloatMenuOption>
            {
                new UiSubmenu("Projects", () => ProjectOptions(false)),
                new UiSubmenu("Worktrees", () => ProjectOptions(true)),
                new FloatMenuOption("Sandbox presets...", () =>
                    ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Sandbox))),
                new FloatMenuOption("App presets...", () =>
                    ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.AppPresets))),
            };

            TerminalWindow.OpenOverPane(new UiMenu(options,
                new Vector2(button.x, button.yMax)));
        }

        static List<FloatMenuOption> ProjectOptions(bool worktrees)
        {
            var allProjects = SessionHub.Instance.Projects;
            var projects = allProjects
                .Where(project => AgentSidebar.Passes(project.Name))
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var options = new List<FloatMenuOption>();

            if (!worktrees)
                options.Add(new FloatMenuOption("New project/workspace...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(null))));

            if (projects.Count > 0)
            {
                if (options.Count > 0) options.Add(UiMenu.Separator());
                foreach (var project in projects)
                {
                    var captured = project;
                    options.Add(new FloatMenuOption($"{captured.Name}  -  {captured.Dir}", () =>
                        TerminalWindow.OpenOverPane(worktrees
                            ? EditProjectDialog.ForWorktrees(captured)
                            : new EditProjectDialog(captured))));
                }
            }
            else
            {
                options.Add(new FloatMenuOption(
                    allProjects.Count > 0 && AgentSidebar.Filtering
                        ? "(no projects in this filter)" : "(no projects)", null));
            }

            return options;
        }

        static void Edit(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template))
                TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template));
            else
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(item));
        }

        static void Menu(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template)) TemplateMenu(template);
            else RowMenu(item);
        }

        static void DrawDetails(Rect r, LibraryItemInfo item)
        {
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);
            bool template = Templates.TryGetValue(item, out var definition);
            bool runnable = Runnable(item);
            // Two stacked action rows also fit the sidebar's minimum width.
            float actionsH = UiTheme.FieldH * 2f + Pad;
            float y = r.y + Pad;
            float textBottom = r.yMax - actionsH - Pad;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string scope = string.IsNullOrEmpty(item.Project) ? "Global" : item.Project;
                var lines = new List<string>
                {
                    item.Name,
                    (template ? "Agent template" : KindName(item.Kind)) + " · " + scope
                };
                if (runnable)
                {
                    lines.Add("In: " + Where(item));
                    lines.Add("Runs with: " + (item.Host ? "Host" :
                        string.IsNullOrEmpty(item.AgentTemplate) ? "Not configured" : item.AgentTemplate));
                }
                else if (item.Kind == LibraryItemKind.FileAction && !template)
                    lines.Add("Host · " + FileActionModeText.Name(item.Mode));
                lines.Add(OneLine(item.Text));
                foreach (var line in lines)
                {
                    if (y + RowH > textBottom) break;
                    UiText.RowLabel(new Rect(r.x, y, r.width, RowH), line);
                    y += RowH;
                }
                TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width,
                    Mathf.Max(0f, textBottom - r.y)), string.Join("\n", lines) + "\n\n" + item.Text);
            }
            if (r.height < actionsH + Pad * 2f) return;
            var primary = new Rect(r.x, r.yMax - actionsH - Pad, r.width, UiTheme.FieldH);
            string label = template ? "Create agent…" : runnable
                ? (item.Link == LibraryItemLink.Ask ? "Run in…" : "Run") : "Edit";
            if (UiButtons.Button(primary, label, UiTheme.Btn.Primary))
            {
                if (template) TerminalWindow.OpenOverPane(EditSessionDialog.FromTemplate(definition));
                else if (runnable) Run(item);
                else Edit(item);
            }
            float moreW = UiTheme.FieldH;
            var secondary = new Rect(r.x, primary.yMax + Pad,
                Mathf.Max(0f, r.width - moreW - Pad), UiTheme.FieldH);
            if (template || runnable)
            {
                if (UiButtons.Button(secondary, "Edit")) Edit(item);
            }
            else if (item.Kind == LibraryItemKind.Breadcrumb)
            {
                if (UiButtons.Button(secondary, "Copy text")) GUIUtility.systemCopyBuffer = item.Text ?? "";
            }
            if (UiButtons.Button(new Rect(r.xMax - moreW, secondary.y, moreW, secondary.height), "…"))
                Menu(item);
        }

        static float DrawGroup(Rect view, float y, string key, List<LibraryItemInfo> bucket)
        {
            float start = y;
            bool folded = Folded.Contains(key);
            string label = key;
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
            Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

            RowChrome.Hover(headRect, false, true, RowHoverPolicy.OverlayAware);

            Rect arrow;
            using (WidgetState.Save())
            {
                GUI.color = UiTheme.Faint;
                arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                    ArrowW, ArrowW);
                GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float lx = arrow.xMax + UiTheme.GapXS;
                string tail = "  " + count;
                var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
                UiText.RowLabel(labelRect, label + tail);
            }

            Slab.Hairline(new Rect(CellX, headRect.yMax - 1f,
                view.width - CellX * 2f, 1f), UiTheme.Edge);

            TooltipHandler.TipRegion(headRect, "Click to fold.");

            return HeadH;
        }

        static float DrawLibraryRow(Rect view, Rect r, LibraryItemInfo item)
        {
            RowChrome.Hover(r, Identity(item) == _selection, true, RowHoverPolicy.OverlayAware);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = UiTheme.Dim;
                var icon = Templates.ContainsKey(item) ? Icons.Agents :
                    item.Kind == LibraryItemKind.Shell ? Icons.Terminal :
                    item.Kind == LibraryItemKind.FileAction ? Icons.Files :
                    item.Kind == LibraryItemKind.Breadcrumb ? Icons.Keyboard : Icons.Library;
                GUI.DrawTexture(new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW), icon);
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(new Rect(CellX + ArrowW + Pad, r.y,
                    Mathf.Max(0f, r.width - CellX * 2f - ArrowW - Pad), RowH), item.Name);
            }
            TooltipHandler.TipRegion(r, item.Name + "\n" +
                (string.IsNullOrEmpty(item.Project) ? "Global" : item.Project) +
                "\n" + OneLine(item.Text));

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
            var r = new Rect(body.x + CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            UiText.StatusLabel(r, !SessionHub.Instance.Online
                ? $"daemon {SessionHub.Instance.Status}"
                : _query.Length > 0 || _kind.Length > 0
                    ? "No matching entries."
                    : AgentSidebar.Filtering
                    ? $"No library entries in {AgentSidebar.FilterLabel}."
                    : "No library entries yet. Press + at the foot of the panel.",
                UiTheme.Faint, GameFont.Tiny);
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

        // Type order is stable across filtering and catalog refreshes.
        static void Group()
        {
            foreach (var list in Groups.Values) list.Clear();
            Order.Clear();

            foreach (var item in _items)
            {
                string key = GroupKey(item);
                if (!Groups.TryGetValue(key, out var list))
                    Groups[key] = list = new List<LibraryItemInfo>();
                list.Add(item);
            }

            foreach (var kv in Groups)
                if (kv.Value.Count > 0) Order.Add(kv.Key);

            Order.Sort((a, b) => Array.IndexOf(Kinds, a).CompareTo(Array.IndexOf(Kinds, b)));

            foreach (var list in Groups.Values) list.Sort((a, b) =>
                string.CompareOrdinal(a?.Name ?? "", b?.Name ?? ""));
        }


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
                    // Headings only fold; item actions belong to their own rows.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key)) Folded.Add(line.Key);
                    }
                    e.Use();
                    return;
                }

                _selected = line.Item;
                _selection = Identity(line.Item);
                _revealSelection = true;
                AgentSidebar.RememberLibrary(line.Item.Name, Templates.ContainsKey(line.Item));
                if (e.button == 1) Menu(line.Item);
                e.Use();
                return;
            }
        }

        // ------------------------------------------------------------------ menus

        static void TemplateMenu(AgentTemplateInfo template)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Create agent...", () => TerminalWindow.OpenOverPane(
                    EditSessionDialog.FromTemplate(template))),
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template))),
                new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template, true))),
                new FloatMenuOption("Delete", () => TerminalWindow.OpenOverPane(
                    ConfirmDialog.Create("Remove template '" + template.Name + "'? Existing agents keep their snapshots.",
                        () => SessionHub.Instance.Catalog.RemoveAgentTemplate(template, template.Name,
                            null, UiLayout.Fail), destructive: true))),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void RowMenu(LibraryItemInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary runnable items exist. Breadcrumbs are definitions only.
            if (s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            if (Runnable(s) && s.Link == LibraryItemLink.Ask)
                opts.Add(new UiSubmenu("Run in", () => WhereOptions(s)));

            var edit = new FloatMenuOption("Edit...", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(s)));
            opts.Add(edit);

            var duplicate = new FloatMenuOption("Duplicate...", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(s)));
            opts.Add(duplicate);

            var name = s.Name;
            opts.Add(new FloatMenuOption("Delete", () =>
                TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                    $"Remove library entry '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiLayout.Fail),
                    destructive: true))));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // Do not reopen AskWhere after resolving a temporary project: `temp` marks the return
        // path where `project == null` is intentional.
        static void Run(LibraryItemInfo s, string project = null, bool temp = false)
        {
            AgentSidebar.RememberLibrary(s?.Name);
            // An entry that never said where goes through a menu first.
            if (s.Link == LibraryItemLink.Ask && project == null && !temp)
            {
                AskWhere(s);
                return;
            }

            bool scratch = temp || s.Link == LibraryItemLink.Temp;
            SessionHub.Instance.SessionStore.RunLibraryItem(s.Name,
                session => TerminalWindow.Open(session),
                UiLayout.Fail,
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

        public static bool FocusLocation(string name, bool template = false)
        {
            bool exists = template
                ? SessionHub.Instance.Templates.Any(candidate => candidate.Name == name)
                : SessionHub.Instance.Library.Any(candidate => candidate.Name == name && !candidate.Builtin);
            if (!exists) return false;
            _selection = (template ? "template:" : "item:") + name;
            _revealSelection = true;
            _query = "";
            _kind = "";
            Folded.Clear();
            return true;
        }
    }
}
