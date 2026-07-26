using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Add or edit one agent. Writes straight through to config.toml on the
    /// daemon.
    ///
    /// What is left here is the two things that are about the agent: its name
    /// and what it runs. Where it works and what it can reach moved to the
    /// project, which is why picking one is mandatory and why there is no
    /// directory field any more.
    /// </summary>
    public class EditSessionDialog : Window
    {
        readonly bool _isNew;
        readonly SessionInfo _s;
        /// The name the daemon still knows this session by: the edit is addressed
        /// to it, and a changed name in the field is a rename.
        readonly string _origName;
        /// What a Claude session will actually run, shown greyed in the command
        /// box so the field is never blank and never a lie.
        string _default = "claude";

        public EditSessionDialog(SessionInfo existing) : this(existing, null) { }

        /// <param name="project">
        /// Preselected project, for "add an agent here" from the projects list.
        /// </param>
        public EditSessionDialog(SessionInfo existing, string project)
        {
            _isNew = existing == null;
            _origName = existing?.Name ?? "";
            _s = existing == null
                ? new SessionInfo { Name = "", Project = project ?? "", Kind = AgentKind.Claude }
                : new SessionInfo
                {
                    Name = existing.Name,
                    Project = existing.Project,
                    Kind = existing.Kind,
                    Agent = existing.Agent,
                    Autostart = existing.Autostart,
                };

            // Every Claude session resolves to the same command, so any of them
            // will do as the placeholder - and with none about, the daemon's
            // stock answer is right anyway.
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
            l.Label(_isNew ? "New agent" : $"Edit '{_origName}'");
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

            // Greyed rather than hidden: a Claude session runs something, and
            // this is what, even though nothing here can change it.
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
                    // The daemon took the rename, so carry the colonist over before
                    // the next reconcile sees a name it doesn't know and retires it.
                    if (!_isNew && from != to) AgentColony.Current?.Rename(from, to);
                    Close();
                },
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }

    /// <summary>
    /// Directory picker backed by the daemon's /api/browse. The game is inside Wine
    /// and cannot see the host filesystem, so the daemon does the listing.
    /// </summary>
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
