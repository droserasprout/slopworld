using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Running one lands a *temporary* colonist - never in config.toml, and it walks
    // off the map when its process exits. Which is why the window closes on Run and
    // hands you the terminal: the errand is already underway.
    public class ShortcutsView : SlopListView<ShortcutInfo>
    {
        public static void Toggle() => TerminalWindow.ToggleContent(() => new ShortcutsView());

        public override void Opened()
        {
            SessionHub.Instance.RefreshShortcuts();
            // The rows name a project, and the dialog they open picks one.
            SessionHub.Instance.RefreshProjects();
        }

        public override string Title => "Shortcuts";

        protected override float RowH => 62f;

        protected override string EmptyNote =>
            "No shortcuts yet. A prompt one hands an agent something you would " +
            "otherwise retype; a shell one runs a command in a project's sandbox. " +
            "Either way the colonist that does it is temporary.";

        protected override IEnumerable<ShortcutInfo> Rows => SessionHub.Instance.Shortcuts;

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new SlopWidgets.Bar(bar);

            if (row.Left("Add shortcut", SlopWidgets.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditShortcutDialog(null));

            if (row.Left("Agents"))
                SessionsView.Toggle();

            if (row.Right("Reload", SlopWidgets.Btn.Ghost))
                hub.RefreshShortcuts(SlopWidgets.Fail);
        }

        protected override void DrawRow(Rect r, ShortcutInfo s)
        {
            SlopWidgets.RowChrome(r);

            float l1 = r.y + SlopWidgets.GapXS, l2 = l1 + SlopWidgets.LineH;

            GUI.color = SlopWidgets.Lead;
            Widgets.Label(new Rect(r.x + SlopWidgets.GapS, l1, 220f, SlopWidgets.LineH), s.Name);

            // The kind decides what the text even is - a sentence for an agent or a command
            // line for a shell.
            GUI.color = s.Kind == ShortcutKind.Shell
                ? SlopWidgets.Warn
                : new Color(0.55f, 0.75f, 0.9f);
            Widgets.Label(new Rect(r.x + 232f, l1, 70f, SlopWidgets.LineH),
                s.Kind == ShortcutKind.Shell ? "shell" : "prompt");

            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(r.x + 302f, l1, r.width - 480f, SlopWidgets.LineH),
                Where(s));

            // One line: the box that edits it is where the rest lives, and a row that grew
            // with the text would push the next shortcut off the list.
            var was = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(r.x + SlopWidgets.GapS, l2, r.width - 190f,
                SlopWidgets.LineH), OneLine(s.Text));
            Text.WordWrap = was;
            GUI.color = Color.white;

            float right = r.xMax - 6f;

            // Run is the reason this window exists, so it is the widest button and the one on
            // its own line.
            var run = new Rect(right - 174f, r.y + 1f, 96f, SlopWidgets.RowBtnH);
            TooltipHandler.TipRegion(run, s.Kind == ShortcutKind.Shell
                ? $"Run '{s.Text}' in a temporary shell in {Where(s)}."
                : $"Hand this to a temporary agent in {Where(s)}.");
            // An entry that never said where goes through a menu first; the button is the
            // same either way, because "run it" is what is being asked for in both cases.
            if (SlopWidgets.Button(run, s.Link == ShortcutLink.Ask ? "Run..." : "Run",
                    SlopWidgets.Btn.Primary))
            {
                if (s.Link == ShortcutLink.Ask) AskWhere(s);
                else Run(s.Name);
            }

            if (SlopWidgets.Button(new Rect(right - 74f, r.y + 1f, 74f, SlopWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditShortcutDialog(s));

            if (SlopWidgets.Button(new Rect(right - 74f, l2, 74f, SlopWidgets.RowBtnH), "Del",
                    SlopWidgets.Btn.Danger))
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail),
                    destructive: true));
            }
        }

        void Run(string name, string project = null, bool temp = false)
        {
            SessionHub.Instance.RunShortcut(name,
                session =>
                    // The list is left only once something is actually running, so a refused
                    // errand leaves it up with the message over it. Opening the pane is what
                    // leaves it: the body is one view at a time.
                    TerminalWindow.Open(session),
                SlopWidgets.Fail, project, temp);
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular.
        void AskWhere(ShortcutInfo s)
        {
            var name = s.Name;
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => Run(name, p.Name)))
                .ToList();

            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(name, null, true)));

            TerminalWindow.OpenOverPane(new FloatMenu(options));
        }

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(ShortcutInfo s)
        {
            switch (s.Link)
            {
                case ShortcutLink.Temp: return "a temporary project";
                case ShortcutLink.Ask: return "wherever you say";
                default:
                    return string.IsNullOrEmpty(s.Project)
                        ? "no project - it will not run"
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
