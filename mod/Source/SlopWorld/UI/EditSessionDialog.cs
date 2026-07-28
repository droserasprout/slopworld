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
        // Shown greyed in the command box, so the field is never blank and never a lie.
        string _default = "claude";

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
                ? new SessionInfo { Name = "", Project = project ?? "", Kind = AgentKind.Claude }
                : new SessionInfo
                {
                    Name = copy ? FreeName(existing.Name) : existing.Name,
                    Project = existing.Project,
                    Kind = existing.Kind,
                    Agent = existing.Agent,
                    Autostart = existing.Autostart,
                };

            // Every Claude session resolves to the same command, so any will do as the
            // placeholder.
            var claude = SessionHub.Instance.Sessions
                .FirstOrDefault(s => s.Kind == AgentKind.Claude && !string.IsNullOrEmpty(s.Agent));
            if (claude != null) _default = claude.Agent;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            SessionHub.Instance.RefreshProjects();
        }

        public override Vector2 InitialSize => new Vector2(560f, 400f);

        public override void DoWindowContents(Rect rect)
        {
            var l = new Listing_Standard();
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

            l.Gap(4f);
            l.Label("Command");
            if (l.ButtonText(_s.Kind == AgentKind.Custom ? "Custom" : "Claude Code"))
                PickKind();

            // Greyed rather than hidden: a Claude session runs something, and this is what.
            bool custom = _s.Kind == AgentKind.Custom;
            var box = l.GetRect(28f);
            if (custom)
            {
                _s.Agent = Widgets.TextField(box, _s.Agent ?? "");
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.TextField(box, _default);
                GUI.color = Color.white;
            }

            l.Gap(6f);
            l.CheckboxLabeled("Start with the daemon", ref _s.Autostart);

            l.End();

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

        void PickKind()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Claude Code", () => _s.Kind = AgentKind.Claude),
                new FloatMenuOption("Custom", () => _s.Kind = AgentKind.Custom),
            }));
        }

        void Save()
        {
            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Project))
            {
                Messages.Message("SlopWorld: name and project are required.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (_s.Kind == AgentKind.Custom && string.IsNullOrEmpty((_s.Agent ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a custom agent needs a command.",
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
