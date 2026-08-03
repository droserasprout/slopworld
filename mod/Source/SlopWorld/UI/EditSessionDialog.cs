using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What is left here is the two things about the agent: its name and what it runs.
    // Where it works and what it can reach moved to the project, which is why picking
    // one is mandatory and there is no directory field.
    public class EditSessionDialog : Window
    {
        readonly bool _isNew;
        readonly SessionInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;
        string _env;
        Vector2 _presetScroll;
        const float PresetsH = 132f;

        // For the title. Null unless it is a duplicate: an edit already has `_origName`.
        readonly string _copiedFrom;

        public EditSessionDialog(SessionInfo existing) : this(existing, null) { }

        // Preselected project, for "add an agent here" from the projects list.
        public EditSessionDialog(SessionInfo existing, string project) : this(existing, project, false) { }

        // Everything the dialog can edit comes over - the project above all, since a
        // second agent in the same repo is what this is for and picking that project
        // again by hand is the step that gets it wrong. The name cannot, so it is the one
        // field that is suggested rather than copied.
        public static EditSessionDialog Copy(SessionInfo of) => new EditSessionDialog(of, null, true);

        EditSessionDialog(SessionInfo existing, string project, bool copy)
        {
            // A copy is a new agent in every way that matters here: nothing on the daemon
            // knows about it, so Save posts rather than puts and there is no rename to carry
            // a colonist across.
            _isNew = existing == null || copy;
            _origName = copy ? "" : (existing?.Name ?? "");
            _copiedFrom = copy ? existing.Name : null;
            _s = existing == null
                ? new SessionInfo { Name = "", Project = project ?? "" }
                : new SessionInfo
                {
                    Name = copy ? FreeName(existing.Name) : existing.Name,
                    Project = existing.Project,
                    Command = existing.Command,
                    Cmd = existing.Cmd,
                    Sandbox = new List<string>(existing.Sandbox),
                    Agent = existing.Agent,
                    Autostart = existing.Autostart,
                    Env = new List<string>(existing.Env),
                };

            _env = string.Join("\n", _s.Env.ToArray());

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;

            SessionHub.Instance.RefreshProjects();
            // Both tables are files the daemon reads, so they are asked for on every open
            // rather than once per process.
            SessionHub.Instance.LoadPresets();
        }

        public override Vector2 InitialSize => new Vector2(560f, 720f);

        public override void DoWindowContents(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            Text.Font = GameFont.Medium;
            l.Label(_copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New agent" : $"Edit '{_origName}'");
            Text.Font = GameFont.Small;
            l.Gap(6f);

            l.Label("Name (also the colonist's name)");
            _s.Name = l.TextEntry(_s.Name);

            l.Gap(4f);
            l.Label("Project (the directory and sandbox it works in)");
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

            var preset = SessionHub.Instance.Command(_s.Command);

            l.Gap(4f);
            l.Label("Command");
            if (l.ButtonText(CommandLabel(preset)))
                PickCommand();

            // Editable whichever it is: a preset says what an agent is, and this box says
            // what this one runs, which is the same field either way.
            _s.Cmd = l.TextEntry(_s.Cmd ?? "");
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label(CommandNote(preset));
            GUI.color = Color.white;

            float used = l.CurHeight;
            l.End();

            float y = rect.y + used + 8f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 22f), "Extra sandbox presets");
            y += 24f;

            // Its command's are ticked and refused here; its project's are the project's to
            // edit. What is left is what this one agent adds.
            PresetList.Draw(new Rect(rect.x, y, rect.width, PresetsH), _s.Sandbox,
                ref _presetScroll, preset != null ? preset.Sandbox : null);
            y += PresetsH + 8f;

            var rest = new Listing_Standard { maxOneColumn = true };
            rest.Begin(new Rect(rect.x, y, rect.width, rect.yMax - y - 40f));

            rest.Label("Environment variables (overrides)");
            var env = rest.GetRect(96f);
            Widgets.DrawBoxSolid(env, new Color(0f, 0f, 0f, 0.25f));
            _env = Widgets.TextArea(env.ContractedBy(4f), _env ?? "");
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            rest.Label("One KEY=VALUE a line. Set last of all, so these beat the project's " +
                       "passed variables and any preset's own.");
            GUI.color = Color.white;

            rest.Gap(6f);
            rest.CheckboxLabeled("Start with the daemon", ref _s.Autostart);
            rest.End();

            var bar = new Rect(rect.x, rect.yMax - 36f, rect.width, 32f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 120f, 32f), "Cancel"))
                Close();

            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 32f), "Save"))
                Save();
        }

        // "claude" -> "claude-2", and a copy of that -> "claude-3" rather than
        // "claude-2-2". Suggested and not enforced - the daemon still refuses a
        // collision, which is why the search gives up rather than looping.
        static string FreeName(string name)
        {
            string stem = name ?? "";
            while (stem.Length > 0 && char.IsDigit(stem[stem.Length - 1]))
                stem = stem.Substring(0, stem.Length - 1);
            stem = stem.TrimEnd(' ', '-', '_');
            if (stem.Length == 0) stem = name ?? "agent";

            var taken = SessionHub.Instance.Sessions.Select(s => s.Name).ToList();
            for (int n = 2; n <= 99; n++)
            {
                string candidate = stem + "-" + n;
                if (!taken.Contains(candidate)) return candidate;
            }
            return stem;
        }

        void PickProject()
        {
            var hub = SessionHub.Instance;
            var options = hub.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name))
                .ToList();

            options.Add(new FloatMenuOption("New project...",
                () => Find.WindowStack.Add(new EditProjectDialog(null))));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // The three states this pair of fields can be in: a command preset, a command line
        // of its own, or neither, which is whatever the daemon's `[defaults] agent` names.
        string CommandLabel(CommandInfo preset)
        {
            if (preset != null) return preset.Name;
            if (!string.IsNullOrEmpty(_s.Command)) return _s.Command + " (unknown here)";
            return string.IsNullOrEmpty((_s.Cmd ?? "").Trim()) ? "Default" : "Command line";
        }

        string CommandNote(CommandInfo preset)
        {
            if (preset != null)
            {
                string sandbox = preset.Sandbox.Count > 0
                    ? "  Sandbox: " + string.Join(", ", preset.Sandbox.ToArray()) + "."
                    : "";
                return $"Blank runs '{preset.Cmd}'.{sandbox}";
            }
            if (!string.IsNullOrEmpty((_s.Cmd ?? "").Trim()))
                return "A command line of its own, so no agent's state directory comes with it.";
            return !string.IsNullOrEmpty(_s.Agent)
                ? $"Blank runs the daemon's default, which is '{_s.Agent}'."
                : "Blank runs the daemon's default agent.";
        }

        void PickCommand()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Default", () => { _s.Command = ""; _s.Cmd = ""; }),
            };

            // Named by the daemon rather than listed here, so a command file dropped in its
            // preset directory is an entry in this menu and nothing to rebuild.
            foreach (var c in SessionHub.Instance.Commands)
            {
                var pick = c;
                options.Add(new FloatMenuOption($"{pick.Name}  -  {pick.Cmd}",
                    () => _s.Command = pick.Name));
            }

            options.Add(new FloatMenuOption("Command line...", () => _s.Command = ""));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        void Save()
        {
            _s.Env = (_env ?? "").Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            var bad = _s.Env.FirstOrDefault(
                x => !x.StartsWith("#") && (x.IndexOf('=') <= 0));
            if (bad != null)
            {
                Messages.Message($"SlopWorld: '{bad}' is not KEY=VALUE.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Project))
            {
                Messages.Message("SlopWorld: name and project are required.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            string from = _origName, to = _s.Name;
            SessionHub.Instance.Save(_s, _isNew, _origName,
                ok: () =>
                {
                    // The daemon took the rename, so carry the colonist over before the next
                    // reconcile sees a name it doesn't know and retires it.
                    if (!_isNew && from != to) AgentColony.Current?.Rename(from, to);
                    Close();
                },
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }

    // The game is inside Wine and cannot see the host filesystem, so the daemon does
    // the listing.
    public class BrowseDialog : Window
    {
        readonly System.Action<string> _pick;
        string _path;
        string _parent;
        string[] _dirs = new string[0];
        Vector2 _scroll;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
            doCloseX = true;
            absorbInputAroundWindow = true;
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        void Load(string path)
        {
            SlopClient.Get($"/api/browse?path={System.Uri.EscapeDataString(path)}",
                j =>
                {
                    _path = j["path"].AsString();
                    _parent = j["parent"].IsNull ? null : j["parent"].AsString();
                    _dirs = j["dirs"].Items.Select(d => d.AsString()).ToArray();
                },
                msg => Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false));
        }

        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), _path ?? "loading...");

            var list = new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 76f);
            int count = _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - 18f, count * 28f);

            Widgets.BeginScrollView(list, ref _scroll, view);
            float y = 0f;

            if (_parent != null)
            {
                if (Widgets.ButtonText(new Rect(0f, y, view.width, 26f), ".."))
                    Load(_parent);
                y += 28f;
            }

            foreach (var d in _dirs)
            {
                if (Widgets.ButtonText(new Rect(0f, y, view.width, 26f), d))
                {
                    Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                    break; // _dirs is about to be replaced under us
                }
                y += 28f;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(rect.x, rect.yMax - 36f, rect.width, 32f),
                                   $"Use this directory"))
            {
                _pick(_path);
                Close();
            }
        }
    }
}
