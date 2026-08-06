using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A directory plus the sandbox every agent in it gets. First button in the bottom
    // bar, because nothing can be added on the agents window until there is somewhere
    // to add it.
    public class ProjectsWindow : SlopListWindow<ProjectInfo>
    {
        public static void Toggle() => SlopWidgets.ToggleWindow(() =>
        {
            SessionHub.Instance.RefreshProjects();
            SessionHub.Instance.LoadPresets();
            return new ProjectsWindow();
        });

        protected override string Title => "Projects";

        protected override float RowH => 52f;

        protected override string EmptyNote =>
            "No projects yet. Add one, then put an agent in it.";

        protected override IEnumerable<ProjectInfo> Rows => SessionHub.Instance.Projects;

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new SlopWidgets.Bar(bar);

            if (row.Left("Add project", SlopWidgets.Btn.Primary))
                Find.WindowStack.Add(new EditProjectDialog(null));

            if (row.Left("Agents"))
                SessionsWindow.Toggle();

            if (row.Right("Reload", SlopWidgets.Btn.Ghost))
                hub.RefreshProjects(SlopWidgets.Fail);
        }

        protected override void DrawRow(Rect r, ProjectInfo p)
        {
            SlopWidgets.RowChrome(r);

            Widgets.Label(new Rect(r.x + 8f, r.y + 4f, 200f, 22f), p.Name);

            // The number that decides whether this project can be deleted at all.
            int agents = SessionHub.Instance.Sessions.Count(s => s.Project == p.Name);
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(r.x + 214f, r.y + 4f, 120f, 22f),
                agents == 1 ? "1 agent" : $"{agents} agents");

            Widgets.Label(new Rect(r.x + 8f, r.y + 24f, r.width - 150f, 20f),
                $"{p.Dir}  ({Summary(p)})");
            GUI.color = Color.white;

            float right = r.xMax - 6f;

            if (SlopWidgets.Button(new Rect(right - 120f, r.y + 1f, 120f, SlopWidgets.RowBtnH), "Edit"))
                Find.WindowStack.Add(new EditProjectDialog(p));

            if (SlopWidgets.Button(new Rect(right - 120f, r.y + 25f, 120f, SlopWidgets.RowBtnH), "Delete",
                    SlopWidgets.Btn.Danger))
            {
                var name = p.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove project '{name}'? The directory is left alone; only the entry " +
                    "in config.toml goes.",
                    () => SessionHub.Instance.RemoveProject(name, SlopWidgets.Fail),
                    destructive: true));
            }
        }

        // The sandbox in one line, the way the agent rows read it.
        public static string Summary(ProjectInfo p)
        {
            var bits = new List<string>();
            // First, because it is the one thing here about the ground rather than about the
            // sandbox around it.
            if (p.Temp) bits.Add("temporary");
            if (!p.Net) bits.Add("no net");
            bits.AddRange(p.Sandbox);
            int extra = p.RoPaths.Count + p.RwPaths.Count;
            if (extra > 0) bits.Add(extra == 1 ? "+1 bind" : $"+{extra} binds");
            return string.Join(", ", bits.ToArray());
        }
    }

    // Presets are checkboxes drawn from whatever the daemon says it knows, so this
    // never has to be kept in step with sandbox.rs by hand.
    public class EditProjectDialog : Window
    {
        readonly bool _isNew;
        readonly ProjectInfo _p;
        // A changed name in the field is a rename, and the daemon carries its sessions
        // over.
        readonly string _origName;

        // For the title. Null unless it is a duplicate: an edit already has `_origName`.
        readonly string _copiedFrom;

        string _roPaths, _rwPaths, _passEnv;
        Vector2 _scroll;
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        const float PresetsH = 152f;

        // The base every project builds on, off `[sandbox]`. Fetched per dialog rather
        // than cached on the hub, because it is one small request and a stale answer
        // here would be a readout quietly describing the wrong sandbox.
        List<string> _baseRo = new List<string>();
        List<string> _baseRw = new List<string>();
        List<string> _baseEnv = new List<string>();

        // Last frame's laid-out height, so the scroll view is sized by what the form
        // actually drew rather than by a number that drifts as fields are added.
        float _contentH = 690f;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        // A second project built on the first: the binds and the presets are what took the
        // work to get right, and ticking all of them again by hand is the step that gets one
        // wrong. The directory comes over with them - the same repo under a tighter sandbox
        // is what this is for, and nothing refuses two projects on one directory. Only the
        // name cannot, so it is the one field suggested rather than copied.
        public static EditProjectDialog Copy(ProjectInfo of) => new EditProjectDialog(of, true);

        EditProjectDialog(ProjectInfo existing, bool copy)
        {
            // A copy is a new project in every way that matters here: nothing on the daemon
            // knows it, so Save posts rather than puts and there is no rename to carry any
            // agents across.
            _isNew = existing == null || copy;
            _origName = copy ? "" : (existing?.Name ?? "");
            _copiedFrom = copy ? existing.Name : null;
            _p = existing?.Copy() ?? new ProjectInfo();
            if (copy)
            {
                _p.Name = SlopWidgets.FreeName(_p.Name,
                    SessionHub.Instance.Projects.Select(p => p.Name), "project");
                // A temporary project's ground is named after the project, so the copy's is
                // named after the copy rather than pointing back at what it came from.
                if (_p.Temp) _p.Dir = ProjectInfo.TempDir(_p.Name);
            }

            _roPaths = Lines(_p.RoPaths);
            _rwPaths = Lines(_p.RwPaths);
            _passEnv = Lines(_p.PassEnv);

            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;

            SessionHub.Instance.LoadPresets();
            SlopClient.Get("/api/config", j =>
            {
                var c = SlopConfig.FromJson(j["values"]);
                _baseRo = c.RoPaths;
                _baseRw = c.RwPaths;
                _baseEnv = c.PassEnv;
            });
        }

        public override Vector2 InitialSize => new Vector2(680f, 680f);

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f),
                _copiedFrom != null
                    ? $"Copy of '{_copiedFrom}'"
                    : _isNew ? "New project" : $"Edit '{_origName}'");
            Text.Font = GameFont.Small;

            var body = new Rect(rect.x, rect.y + 38f, rect.width, rect.height - 38f - 40f);
            var view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(_contentH, body.height));

            Widgets.BeginScrollView(body, ref _scroll, view);
            DoFields(view);
            Widgets.EndScrollView();

            var bar = new Rect(rect.x, rect.yMax - 34f, rect.width, 32f);
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save();
        }

        void DoFields(Rect r)
        {
            // Begun on the room it has and pinned to one column. Listing_Standard breaks to a
            // second column the moment a control would cross the bottom of the rect it was
            // begun on - curX past the whole width, so everything after is clipped away by
            // the group, and CurHeight back to nearly nothing. Everything below is laid out
            // from that number, so the path boxes land on top of the fields.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(r.x, r.y, r.width, r.height));

            l.Label("Name");
            _p.Name = SlopWidgets.Field(l, "project.name", _p.Name);

            l.Gap(4f);
            _p.Temp = SlopWidgets.Checkbox(l, "Temporary - scratch space under /tmp", _p.Temp,
                "The directory is made for you under " + ProjectInfo.TempRoot + ", named after " +
                "this project, and it is there the first time an agent starts. Nothing " +
                "deletes it; the machine clears /tmp.");

            l.Gap(4f);
            l.Label("Directory");
            if (_p.Temp)
            {
                // Stated rather than hidden: the path is the daemon's to coin and this is what
                // it will coin. Browse goes with it - there is nothing to find yet.
                SlopWidgets.Field(l, "project.dir", ProjectInfo.TempDir(_p.Name), false);
            }
            else
            {
                _p.Dir = SlopWidgets.Field(l, "project.dir", _p.Dir);
                if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Browse..."))
                    Find.WindowStack.Add(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

            l.Gap(6f);
            _p.Net = SlopWidgets.Checkbox(l, "Allow network", _p.Net);

            float used = l.CurHeight;
            l.End();

            float y = r.y + used + 10f;
            Widgets.Label(new Rect(r.x, y, r.width, 22f), "Sandbox presets");
            y += 24f;

            PresetList.Draw(new Rect(r.x, y, r.width, PresetsH), _p.Sandbox, _presetScroll);
            y += PresetsH + 6f;

            // An agent gets whatever its command preset asks for whether or not it is ticked
            // here, and saying so is cheaper than the player wondering why ~/.claude is bound.
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(r.x, y, r.width, 22f),
                "An agent also gets the presets its command asks for, and any of its own.");
            GUI.color = Color.white;
            y += 26f;

            float boxW = (r.width - 16f) / 3f;
            float boxH = 132f;
            _roPaths = SlopWidgets.PathList(new Rect(r.x, y, boxW, boxH),
                "project.ro", "Read-only binds", _roPaths);
            _rwPaths = SlopWidgets.PathList(new Rect(r.x + boxW + 8f, y, boxW, boxH),
                "project.rw", "Read-write binds", _rwPaths);
            _passEnv = SlopWidgets.PathList(new Rect(r.x + (boxW + 8f) * 2f, y, boxW, boxH),
                "project.env", "Passed env vars", _passEnv);
            y += boxH + 12f;

            y = DoEffective(r, y, boxW);
            _contentH = y - r.y + 8f;
        }

        // The three boxes above are what this project *adds*. On their own they say
        // nothing about what an agent in here can actually reach, which is the only
        // question anybody opens this dialog to answer - and it is the reason the
        // machine-wide lists on the config window read as doing nothing. So the merge is
        // drawn where it is asked about, in the same three groups and the same order the
        // daemon assembles them: `[sandbox]`, then the ticked presets, then this project.
        //
        // Asked for rather than handed over: `paths()` drops any bind whose path is not
        // on this machine, and only the daemon knows which those are. Saying so is
        // cheaper than a readout that is quietly wrong about a socket that was not there.
        float DoEffective(Rect r, float y, float colW)
        {
            Widgets.Label(new Rect(r.x, y, r.width, 22f),
                "What an agent here asks for");
            y += 22f;

            GUI.color = SlopWidgets.Dim;
            var note = new Rect(r.x, y, r.width, 20f);
            Widgets.Label(note,
                "The base, the presets and the boxes above, together. A path that is not " +
                "on this machine is skipped.");
            y += 22f;

            var cols = new[]
            {
                Merge(_baseRo, pr => pr.Ro, Split(_roPaths)),
                Merge(_baseRw, pr => pr.Rw, Split(_rwPaths)),
                Merge(_baseEnv, pr => pr.Env, Split(_passEnv)),
            };

            float tallest = 0f;
            for (int i = 0; i < 3; i++)
            {
                string text = cols[i].Count > 0
                    ? string.Join("\n", cols[i].ToArray())
                    : "(nothing)";
                float w = colW - 8f;
                float h = Text.CalcHeight(text, w);
                Widgets.Label(new Rect(r.x + i * (colW + 8f), y, w, h), text);
                tallest = Mathf.Max(tallest, h);
            }
            GUI.color = Color.white;

            return y + tallest;
        }

        // Gathered in the order `sandbox.rs` binds them - global, presets, project - so
        // a path named twice is deduplicated against the first that asked for it, then
        // sorted, because this column is read to find out whether a particular path is
        // in it. Bind order is the daemon's business and settles nothing a reader here
        // can see; alphabetical means a path can be looked for rather than hunted, and
        // means two projects' columns can be held side by side and compared.
        List<string> Merge(List<string> baseList,
                           System.Func<PresetInfo, List<string>> pick,
                           List<string> own)
        {
            var all = new List<string>(baseList);
            foreach (var pr in SessionHub.Instance.Presets)
                if (_p.Sandbox.Contains(pr.Name))
                    all.AddRange(pick(pr));
            all.AddRange(own);

            var seen = new List<string>();
            foreach (var s in all)
                if (s.Length > 0 && !seen.Contains(s))
                    seen.Add(s);

            seen.Sort(System.StringComparer.OrdinalIgnoreCase);
            return seen;
        }

        void Save()
        {
            _p.RoPaths = Split(_roPaths);
            _p.RwPaths = Split(_rwPaths);
            _p.PassEnv = Split(_passEnv);

            if (string.IsNullOrEmpty((_p.Name ?? "").Trim()))
            {
                SlopWidgets.Fail("a project needs a name");
                return;
            }
            // A temporary project's directory is the daemon's to coin, and it coins it again
            // on the way in - this is only so the list has the right path before the answer
            // comes back.
            if (_p.Temp) _p.Dir = ProjectInfo.TempDir(_p.Name);
            else if (string.IsNullOrEmpty((_p.Dir ?? "").Trim()))
            {
                SlopWidgets.Fail("a project needs a directory");
                return;
            }

            SessionHub.Instance.SaveProject(_p, _isNew, _origName,
                ok: () => Close(),
                fail: SlopWidgets.Fail);
        }

        static string Lines(List<string> items) => string.Join("\n", items.ToArray());

        static List<string> Split(string text) =>
            (text ?? "").Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
    }
}
