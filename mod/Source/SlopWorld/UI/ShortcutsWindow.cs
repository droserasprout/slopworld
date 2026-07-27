using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The errands: things worth saying to an agent more than once, and things
    /// worth running in a project's sandbox more than once.
    ///
    /// Running one lands a *temporary* colonist - an agent that was never in
    /// config.toml and walks off the map when its process exits. A standing agent
    /// is somebody you keep talking to; an errand is a body that turns up, does
    /// the thing and goes. Which is why the window closes on Run and hands you
    /// the terminal instead: the errand is already underway and the only thing
    /// left to do with it is watch.
    /// </summary>
    public class ShortcutsWindow : Window
    {
        const float RowH = 62f;

        Vector2 _scroll;

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<ShortcutsWindow>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.RefreshShortcuts();
            // The rows name a project, and the dialog they open picks one.
            SessionHub.Instance.RefreshProjects();
            Find.WindowStack.Add(new ShortcutsWindow());
        }

        public ShortcutsWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 480f);

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), "Shortcuts");
            Text.Font = GameFont.Small;

            GUI.color = hub.Online ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rect.x + 130f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;

            float top = rect.y + 40f;
            DrawList(new Rect(rect.x, top, rect.width, rect.height - top - 40f), hub);

            var bar = new Rect(rect.x, rect.yMax - 32f, rect.width, 30f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 130f, 30f), "Add shortcut"))
                Find.WindowStack.Add(new EditShortcutDialog(null));

            if (Widgets.ButtonText(new Rect(bar.x + 138f, bar.y, 130f, 30f), "Agents"))
                SessionsWindow.Toggle();

            if (Widgets.ButtonText(new Rect(bar.x + 276f, bar.y, 130f, 30f), "Reload"))
                hub.RefreshShortcuts(Fail);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var view = new Rect(0f, 0f, rect.width - 18f, hub.Shortcuts.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (hub.Shortcuts.Count == 0)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 64f),
                    hub.Online
                        ? "No shortcuts yet. A prompt one hands an agent something you would " +
                          "otherwise retype; a shell one runs a command in a project's sandbox. " +
                          "Either way the colonist that does it is temporary."
                        : "Daemon unreachable. Is slopd running?  systemctl --user status slopd");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var s in hub.Shortcuts.ToList())
            {
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), s);
                y += RowH;
            }

            Widgets.EndScrollView();
        }

        void DrawRow(Rect r, ShortcutInfo s)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawHighlightIfMouseover(r);

            Widgets.Label(new Rect(r.x + 8f, r.y + 4f, 220f, 22f), s.Name);

            // The kind, because it decides what the text even is - a sentence for
            // an agent or a command line for a shell.
            GUI.color = s.Kind == ShortcutKind.Shell
                ? new Color(0.85f, 0.75f, 0.45f)
                : new Color(0.55f, 0.75f, 0.9f);
            Widgets.Label(new Rect(r.x + 232f, r.y + 4f, 70f, 22f),
                s.Kind == ShortcutKind.Shell ? "shell" : "prompt");

            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            string where = string.IsNullOrEmpty(s.Project)
                ? "no project - it will not run"
                : s.Project;
            Widgets.Label(new Rect(r.x + 302f, r.y + 4f, r.width - 480f, 22f), where);

            // What it will say, on one line: the box that edits it is where the
            // rest of it lives, and a row that grew with the text would push the
            // next shortcut off the list.
            var was = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(r.x + 8f, r.y + 26f, r.width - 190f, 22f), OneLine(s.Text));
            Text.WordWrap = was;
            GUI.color = Color.white;

            float right = r.xMax - 6f;

            // Run is the reason this window exists, so it is the widest button and
            // the one on its own line.
            var run = new Rect(right - 174f, r.y + 4f, 96f, 20f);
            TooltipHandler.TipRegion(run, s.Kind == ShortcutKind.Shell
                ? $"Run '{s.Text}' in a temporary shell in {Where(s)}."
                : $"Hand this to a temporary agent in {Where(s)}.");
            if (Widgets.ButtonText(run, "Run"))
                Run(s.Name);

            if (Widgets.ButtonText(new Rect(right - 74f, r.y + 4f, 74f, 20f), "Edit"))
                Find.WindowStack.Add(new EditShortcutDialog(s));

            if (Widgets.ButtonText(new Rect(right - 74f, r.y + 26f, 74f, 20f), "Del"))
            {
                var name = s.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, Fail),
                    destructive: true));
            }
        }

        void Run(string name)
        {
            SessionHub.Instance.RunShortcut(name,
                session =>
                {
                    // Closed only once something is actually running, so a
                    // refused errand leaves the list up with the message over it.
                    Close();
                    TerminalWindow.Open(session);
                },
                Fail);
        }

        static string Where(ShortcutInfo s) =>
            string.IsNullOrEmpty(s.Project) ? "no project" : s.Project;

        /// <summary>The first line of a prompt, which is all a row has space for.</summary>
        static string OneLine(string text)
        {
            text = (text ?? "").Replace("\r", "");
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl) + " ...";
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }

    /// <summary>
    /// Add or edit one errand. Writes straight through to config.toml on the
    /// daemon, the same as every other window here.
    ///
    /// The command box is greyed rather than hidden when it is empty, so the
    /// thing that will run is on screen even when nothing here chose it - the
    /// same reasoning as the agent dialog's.
    /// </summary>
    public class EditShortcutDialog : Window
    {
        readonly bool _isNew;
        readonly ShortcutInfo _s;
        /// The name the daemon still knows this shortcut by: the edit is
        /// addressed to it, and a changed name in the field is a rename.
        readonly string _origName;

        /// What each kind runs when the command box is left empty. Asked for
        /// rather than assumed: `[defaults] shell` is a per-machine answer and
        /// this dialog would otherwise print somebody else's.
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

            SessionHub.Instance.RefreshProjects();
            SlopClient.Get("/api/config", j =>
            {
                var d = j["values"]["defaults"];
                _agentDefault = d["agent"].AsString("claude");
                _shellDefault = d["shell"].AsString("bash");
            });
        }

        public override Vector2 InitialSize => new Vector2(560f, 520f);

        public override void DoWindowContents(Rect rect)
        {
            var l = new Listing_Standard();
            l.Begin(new Rect(rect.x, rect.y, rect.width, 300f));

            Text.Font = GameFont.Medium;
            l.Label(_isNew ? "New shortcut" : $"Edit '{_origName}'");
            Text.Font = GameFont.Small;
            l.Gap(6f);

            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = l.TextEntry(_s.Name);

            l.Gap(4f);
            l.Label("Kind");
            if (l.ButtonText(_s.Kind == ShortcutKind.Shell
                    ? "Shell - run a command"
                    : "Prompt - say something to an agent"))
                PickKind();

            l.Gap(4f);
            l.Label("Project (the directory and sandbox it runs in)");
            if (l.ButtonText(string.IsNullOrEmpty(_s.Project) ? "Pick a project..." : _s.Project))
                PickProject();

            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label(project != null
                ? $"{project.Dir}  ({ProjectsWindow.Summary(project)})"
                : SessionHub.Instance.Projects.Count == 0
                    ? "No projects yet - make one in the Projects window first."
                    : "");
            GUI.color = Color.white;

            l.Gap(4f);
            l.Label(_s.Kind == ShortcutKind.Shell ? "Shell (blank = the default)"
                                                  : "Agent (blank = the default)");
            var box = l.GetRect(28f);
            if (string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                // Empty is the normal answer, and what it means is worth reading
                // off the field rather than out of the daemon's config.
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                string shown = Widgets.TextField(box,
                    _s.Kind == ShortcutKind.Shell ? _shellDefault : _agentDefault);
                GUI.color = Color.white;
                // A field the player typed into stops being the placeholder.
                if (shown != (_s.Kind == ShortcutKind.Shell ? _shellDefault : _agentDefault))
                    _s.Command = shown;
            }
            else
            {
                _s.Command = Widgets.TextField(box, _s.Command);
            }

            float used = l.CurHeight;
            l.End();

            float y = rect.y + used + 8f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
                _s.Kind == ShortcutKind.Shell ? "Command line" : "Prompt");
            y += 24f;

            var area = new Rect(rect.x, y, rect.width, rect.yMax - y - 40f);
            Widgets.DrawBoxSolid(area, new Color(0f, 0f, 0f, 0.25f));
            _s.Text = Widgets.TextArea(area.ContractedBy(4f), _s.Text ?? "");

            var bar = new Rect(rect.x, rect.yMax - 36f, rect.width, 32f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 120f, 32f), "Cancel"))
                Close();
            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 32f), "Save"))
                Save();
        }

        void PickKind()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
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

            options.Add(new FloatMenuOption("New project...",
                () => Find.WindowStack.Add(new EditProjectDialog(null))));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_s.Name ?? "").Trim()) ||
                string.IsNullOrEmpty((_s.Project ?? "").Trim()))
            {
                Messages.Message("SlopWorld: name and project are required.",
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
