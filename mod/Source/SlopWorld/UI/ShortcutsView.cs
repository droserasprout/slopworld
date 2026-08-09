using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The column's shortcuts body: every [[shortcut]] entry, grouped by project, drawn
    // as thin text rows under foldable headings - the same shape the agents view draws
    // its ghost rows in. No portraits, no titles, no footer bar and no "+" of its own:
    // just the list. Adding a shortcut is one of the three answers the column's own add
    // strip offers, at the foot of the panel below this body (AgentSidebar.AddBar).
    //
    // Drawn from AgentSidebar's back pass, which is what puts it over a terminal as well
    // as on the map - the same road the other three views take.
    public static class ShortcutsView
    {
        // Off the font, for the reason the other two trees' are.
        static float RowH => SlopWidgets.TinyRowH;
        static float HeadH => SlopWidgets.TinyRowH;
        const float Pad = 6f;
        const float CellX = 8f;
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

        // The whole body is the list: the "+" at the foot of the panel is the column's own
        // now, shared by all four views, and AgentSidebar.Body already has its strip taken
        // off - so this view reserves nothing and draws no button of its own.
        //
        // The view's own rect, moved into the panel and up by however far it is scrolled.
        // The scroll view is clipped, so a row scrolled out of sight would otherwise still
        // answer a click where it used to be. Same helper the files view carries.
        static Rect Screen(Rect r)
        {
            var list = AgentSidebar.Body;
            var moved = new Rect(list.x + r.x, list.y + r.y - _scroll.y, r.width, r.height);
            return moved.yMax <= list.y || moved.y >= list.yMax ? Rect.zero : moved;
        }

        static Vector2 _scroll;

        public static void Draw(Rect body)
        {
            _items = SessionHub.Instance.Shortcuts.ToList();
            Lines.Clear();

            if (_items.Count == 0)
            {
                Empty(body);
                return;
            }

            Group();

            var list = body;
            float height = Measure();
            var view = new Rect(0f, 0f, list.width - (height > list.height ? 16f : 0f),
                height);

            // GUI rather than GUILayout, so this is safe in a pass that declines Layout
            // events - see AgentSidebar.DrawBack. Closed from a finally the way the files
            // view closes its own: a scroll view left open is every window drawn after it
            // drawn somewhere else.
            Widgets.BeginScrollView(list, ref _scroll, view);
            try
            {
                float y = Pad;

                foreach (var key in Order)
                {
                    var bucket = Groups[key];
                    bool folded = Folded.Contains(key);

                    string label = key.Length == 0 ? LooseLabel : key;
                    var headRect = new Rect(0f, y, view.width, HeadH);
                    // The key, not the label: the fold set is keyed by the group and the
                    // loose bucket's key is "" while its label reads "no project".
                    Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

                    // Heading
                    if (ColonistBarStrip.MouseOver(Screen(headRect)))
                        Widgets.DrawHighlight(headRect);

                    GUI.color = SlopWidgets.Faint;
                    var arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                        ArrowW, ArrowW);
                    GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    float lx = arrow.xMax + 4f;
                    string tail = folded ? "  " + bucket.Count : "";
                    var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
                    SlopWidgets.RowLabel(labelRect, label + tail);

                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;

                    Widgets.DrawBoxSolid(new Rect(CellX, headRect.yMax - 1f,
                        view.width - CellX * 2f, 1f), new Color(1f, 1f, 1f, 0.08f));

                    TooltipHandler.TipRegion(headRect,
                        key.Length == 0
                            ? "Shortcuts that don't belong to any project.\n\nClick to fold."
                            : $"Click to fold, right-click for the project.");

                    y += HeadH;
                    if (folded) continue;

                    foreach (var item in bucket)
                    {
                        var r = new Rect(0f, y, view.width, RowH);
                        if (ColonistBarStrip.MouseOver(Screen(r))) Widgets.DrawHighlight(r);

                        // The kind badge: "prompt" or "shell"
                        float badgeW = 34f;
                        GUI.color = item.Kind == ShortcutKind.Shell
                            ? SlopWidgets.Warn
                            : new Color(0.55f, 0.75f, 0.9f);
                        // The whole row is Tiny, the way a row of the other two trees is: the
                        // badge was, and the name and the sample beside it were Small in a row
                        // laid out for Tiny - which on any face taller than the one it was
                        // written against is a line with its descenders cut off.
                        Text.Font = GameFont.Tiny;
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(new Rect(CellX, r.y, badgeW, RowH),
                            item.Kind == ShortcutKind.Shell ? "sh" : "→");
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
                        y += RowH;
                    }
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
            Widgets.Label(r, SessionHub.Instance.Online
                ? "No shortcuts yet. Press + at the foot of the panel."
                : $"daemon {SessionHub.Instance.Status}");
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

                // Left click: run the shortcut.
                e.Use();
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

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }

        static void RowMenu(ShortcutInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason this exists, so it is first.
            opts.Add(new FloatMenuOption("Run", () => Run(s)));

            var where = Where(s);
            if (s.Link == ShortcutLink.Ask)
                opts.Add(new FloatMenuOption("Run in...", () => AskWhere(s)));

            opts.Add(new FloatMenuOption("Edit...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(s))));

            opts.Add(new FloatMenuOption("Delete", () =>
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail),
                    destructive: true));
            }));

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // `temp` is the answer to the menu as well as a caller's own, which is why the
        // menu is only opened when neither has been given: asking again on the way back
        // from "a temporary project under ..." - which comes back with a null project by
        // design - would put the same menu up forever and the temporary run would be the
        // one option in it that could never be taken.
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
                scratch);
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular.
        static void AskWhere(ShortcutInfo s)
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => Run(s, p.Name)))
                .ToList();

            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(s, null, true)));

            TerminalWindow.OpenOverPane(new FloatMenu(options));
        }

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
    public class EditShortcutDialog : Window
    {
        readonly bool _isNew;
        readonly ShortcutInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;

        // Asked for rather than assumed: `[defaults] shell` is a per-machine answer and
        // this dialog would otherwise print somebody else's.
        string _agentDefault = "claude";
        string _shellDefault = "bash";

        public EditShortcutDialog(ShortcutInfo existing)
        {
            _isNew = existing == null;
            _origName = existing?.Name ?? "";
            _s = existing?.Copy() ?? new ShortcutInfo();

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;

            SessionHub.Instance.RefreshProjects();
            SlopClient.Get("/api/config", j =>
            {
                var d = j["values"]["defaults"];
                _agentDefault = d["agent"].AsString("claude");
                _shellDefault = d["shell"].AsString("bash");
            });
        }

        // What is left at the bottom is the prompt box - the one field here somebody
        // writes paragraphs in, and the one that gets squeezed when anything above grows.
        public override Vector2 InitialSize => new Vector2(560f, 660f);

        public override void DoWindowContents(Rect rect)
        {
            // One column, on the room it has: a Listing_Standard begun on a rect too short
            // for its contents does not overflow, it breaks to a column off the right-hand
            // edge and puts CurHeight back to nearly zero - and the prompt box below is
            // placed and sized from that number. See EditProjectDialog.DoFields.
            SlopWidgets.Title(rect, _isNew ? "New shortcut" : $"Edit '{_origName}'");

            float head = SlopWidgets.HeaderH + SlopWidgets.GapS;
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(rect.x, rect.y + head, rect.width, rect.height - head));

            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = SlopWidgets.Field(l, "shortcut.name", _s.Name);

            l.Gap(SlopWidgets.GapS);
            l.Label("Kind");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    _s.Kind == ShortcutKind.Shell
                        ? "Shell - run a command"
                        : "Prompt - say something to an agent"))
                PickKind();

            l.Gap(SlopWidgets.GapS);
            l.Label("Where it runs");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), LinkLabel(_s.Link)))
                PickLink();

            // The project dropdown stays up for two of the three, because in temp mode it
            // still answers something - which sandbox the scratch project is given - and a
            // field that vanished would read as a setting that does not exist.
            if (_s.Link != ShortcutLink.Ask)
            {
                l.Gap(SlopWidgets.GapS);
                l.Label(_s.Link == ShortcutLink.Temp
                    ? "Sandbox to copy (blank = plain: network on, no presets)"
                    : "Project (the directory and sandbox it runs in)");
                if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                        string.IsNullOrEmpty(_s.Project)
                            ? (_s.Link == ShortcutLink.Temp ? "None" : "Pick a project...")
                            : _s.Project))
                    PickProject();
            }

            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = SlopWidgets.Dim;
            l.Label(Explain(project));
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapS);
            l.Label(_s.Kind == ShortcutKind.Shell ? "Shell (blank = the default)"
                                                  : "Agent (blank = the default)");
            var box = l.GetRect(SlopWidgets.FieldH);
            if (string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                // Empty is the normal answer, and what it means is worth reading off the field.
                // In `Faint`, which is the rung a placeholder is: glanced at, not read.
                string placeholder =
                    _s.Kind == ShortcutKind.Shell ? _shellDefault : _agentDefault;
                GUI.color = SlopWidgets.Faint;
                string shown = SlopWidgets.Field(box, "shortcut.command", placeholder);
                GUI.color = Color.white;
                // A field the player typed into stops being the placeholder.
                if (shown != placeholder) _s.Command = shown;
            }
            else
            {
                _s.Command = SlopWidgets.Field(box, "shortcut.command", _s.Command);
            }

            float used = l.CurHeight;
            l.End();

            float y = rect.y + head + used + SlopWidgets.GapL;
            SlopWidgets.SectionHeading(new Rect(rect.x, y, rect.width, SlopWidgets.RowH),
                _s.Kind == ShortcutKind.Shell ? "Command line" : "Prompt");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            var area = new Rect(rect.x, y, rect.width,
                rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS - y);
            _s.Text = SlopWidgets.Area(area, "shortcut.text", _s.Text ?? "");

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
            TerminalWindow.OpenOverPane(new FloatMenu(new List<FloatMenuOption>
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
            TerminalWindow.OpenOverPane(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Prompt - say something to an agent",
                    () => _s.Kind = ShortcutKind.Prompt),
                new FloatMenuOption("Shell - run a command",
                    () => _s.Kind = ShortcutKind.Shell),
            }));
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

            TerminalWindow.OpenOverPane(new FloatMenu(options));
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_s.Name ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a shortcut needs a name.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Link == ShortcutLink.Project &&
                string.IsNullOrEmpty((_s.Project ?? "").Trim()))
            {
                Messages.Message("SlopWorld: pick a project, or a way to choose one.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (string.IsNullOrEmpty((_s.Text ?? "").Trim()))
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
