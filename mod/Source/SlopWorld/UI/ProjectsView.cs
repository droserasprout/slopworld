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
    public class ProjectsView : SlopListView<ProjectInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.RefreshProjects();
            SessionHub.Instance.LoadPresets();
        }

        public override string Title => "Projects";

        // The second line holds a row button, so the pitch is off that rather than off two
        // line heights - see SessionsView, which has the same two-line row.
        protected override float RowH =>
            SlopWidgets.GapXS + SlopWidgets.LineH + SlopWidgets.RowBtnH + SlopWidgets.GapXS + 4f;

        protected override string EmptyNote =>
            "No projects yet. Add one, then put an agent in it.";

        protected override IEnumerable<ProjectInfo> Rows => SessionHub.Instance.Projects;

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new SlopWidgets.Bar(bar);

            if (row.Left("Add project", SlopWidgets.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditProjectDialog(null));

            if (row.Right("Reload", SlopWidgets.Btn.Ghost))
                hub.RefreshProjects(SlopWidgets.Fail);
        }

        protected override void DrawRow(Rect r, ProjectInfo p)
        {
            SlopWidgets.RowChrome(r);

            // Said rather than inherited: the widths below are measured, and a measurement is
            // about whichever tier is current when it is taken.
            Text.Font = GameFont.Small;

            // The two lines of the row, off the font rather than off a pair of figures four
            // pixels apart: `Widgets.Label` clips to the rect it is handed, so a literal here
            // is one that crops descenders on any font but the one it was chosen against.
            float l1 = r.y + SlopWidgets.GapXS, l2 = l1 + SlopWidgets.LineH;

            // The name's column, measured: 200 and 214 held for one face at one size.
            float nameW = Mathf.Max(SlopWidgets.Wide("mmmmmmmmmmmmmmmm"), 200f);

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(
                new Rect(r.x + SlopWidgets.GapS, l1, nameW, SlopWidgets.LineH), p.Name);

            // The number that decides whether this project can be deleted at all.
            int agents = SessionHub.Instance.Sessions.Count(s => s.Project == p.Name);
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(
                new Rect(r.x + SlopWidgets.GapS + nameW + SlopWidgets.GapS, l1,
                    SlopWidgets.Wide("99 agents") + 4f, SlopWidgets.LineH),
                agents == 1 ? "1 agent" : $"{agents} agents");

            // Cut rather than wrapped: a directory and a summary beside it have spaces in
            // them, and a wrapped line in a one-line slot loses the half of each that is
            // outside the rect.
            SlopWidgets.RowLabel(
                new Rect(r.x + SlopWidgets.GapS, l2, Mathf.Max(60f, r.width - 150f),
                    SlopWidgets.LineH), $"{p.Dir}  ({Summary(p)})");
            GUI.color = Color.white;

            float right = r.xMax - 6f;
            float actW = Mathf.Max(SlopWidgets.BtnW("Delete", 120f), SlopWidgets.BtnW("Edit", 120f));

            if (SlopWidgets.Button(new Rect(right - actW, r.y + 1f, actW, SlopWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditProjectDialog(p));

            if (SlopWidgets.Button(new Rect(right - actW, l2, actW, SlopWidgets.RowBtnH), "Delete",
                    SlopWidgets.Btn.Danger))
            {
                var name = p.Name;
                TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
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
            bits.Add(NetworkModeText.ShortLabel(p.Network));
            bits.AddRange(p.Sandbox);
            return string.Join(", ", bits.ToArray());
        }
    }

    // Presets are checkboxes drawn from whatever the daemon says it knows, so this
    // never has to be kept in step with sandbox.rs by hand.
    public class EditProjectDialog : SlopWindow
    {
        enum Tab { Edit, Preview }

        readonly bool _isNew;
        readonly ProjectInfo _p;
        // A changed name in the field is a rename, and the daemon carries its sessions
        // over.
        readonly string _origName;

        // For the title. Null unless it is a duplicate: an edit already has `_origName`.
        readonly string _copiedFrom;

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        const float PresetsH = 152f;
        const float BreadcrumbsH = 132f;
        Tab _tab;

        // Last frame's laid-out height, so the scroll view is sized by what the form
        // actually drew rather than by a number that drifts as fields are added.
        float _contentH = 690f;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        // A second project built on the first: the presets are what took the work to get right,
        // and ticking/copying all of them again by hand is the step that gets one wrong. The
        // directory comes over with them - the same repo under a tighter sandbox is what this
        // is for, and nothing refuses two projects on one directory. Only the name cannot, so
        // it is the one field suggested rather than copied.
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

            resizeable = true;

            SessionHub.Instance.LoadPresets(fail: SlopWidgets.Fail);
        }

        public override Vector2 InitialSize => new Vector2(680f, 680f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New project" : $"Edit '{_origName}'");

            float tabsY = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            DrawTabs(new Rect(rect.x, tabsY, rect.width, SlopWidgets.BtnH));
            float top = tabsY + SlopWidgets.BtnH + SlopWidgets.GapM;
            var body = new Rect(rect.x, top, rect.width,
                rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS - top);
            if (_tab == Tab.Edit)
            {
                var view = new Rect(0f, 0f, body.width - SlopWidgets.ScrollbarW,
                    Mathf.Max(_contentH, body.height));
                _scroll.Begin(body, view);
                DoFields(view);
                _scroll.End();
            }
            else
            {
                SandboxPreviewPanel.Draw(body, ref _previewScroll,
                    SandboxPreviewData.ForProject(_p));
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save();
        }

        void DrawTabs(Rect r)
        {
            float gap = SlopWidgets.GapS;
            float w = (r.width - gap) / 2f;
            if (SlopWidgets.Button(new Rect(r.x, r.y, w, r.height), "Edit",
                    _tab == Tab.Edit ? SlopWidgets.Btn.Primary : SlopWidgets.Btn.Ghost))
                _tab = Tab.Edit;
            if (SlopWidgets.Button(new Rect(r.x + w + gap, r.y, w, r.height), "Preview",
                    _tab == Tab.Preview ? SlopWidgets.Btn.Primary : SlopWidgets.Btn.Ghost))
                _tab = Tab.Preview;
        }

        void DoFields(Rect r)
        {
            // Begun on the room it has and pinned to one column. Listing_Standard breaks to a
            // second column the moment a control would cross the bottom of the rect it was
            // begun on - curX past the whole width, so everything after is clipped away by
            // the group, and CurHeight back to nearly nothing.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(r.x, r.y, r.width, r.height));

            l.Label("Name");
            _p.Name = SlopWidgets.Field(l, "project.name", _p.Name);

            l.Gap(SlopWidgets.GapS);
            _p.Temp = SlopWidgets.Checkbox(l, "Temporary - scratch space under /tmp", _p.Temp,
                "The directory is made for you under " + ProjectInfo.TempRoot + ", named after " +
                "this project, and it is there the first time an agent starts. Nothing " +
                "deletes it; the machine clears /tmp.");

            l.Gap(SlopWidgets.GapS);
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
                    TerminalWindow.OpenOverPane(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

            l.Gap(SlopWidgets.GapS);
            l.Label("Network ceiling");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), NetworkModeText.Label(_p.Network)))
                PickNetwork();
            GUI.color = SlopWidgets.Dim;
            l.Label(_p.Network == NetworkMode.Host
                ? "Agents may use the host network, including local services."
                : _p.Network == NetworkMode.Private
                    ? "Agents may use the Internet through a private namespace."
                    : "Agents have no network access.");
            GUI.color = Color.white;

            float used = l.CurHeight;
            l.End();

            float y = r.y + used + SlopWidgets.GapL;
            SlopWidgets.SectionHeading(new Rect(r.x, y, r.width, SlopWidgets.RowH),
                "Sandbox presets");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            PresetList.Draw(new Rect(r.x, y, r.width, PresetsH), _p.Sandbox, _presetScroll);
            y += PresetsH + SlopWidgets.GapXS;

            SlopWidgets.SectionHeading(new Rect(r.x, y, r.width, SlopWidgets.RowH),
                "Prompt breadcrumbs");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;
            BreadcrumbList.Draw(new Rect(r.x, y, r.width, BreadcrumbsH), _p.Breadcrumbs,
                _breadcrumbScroll);
            y += BreadcrumbsH + SlopWidgets.GapXS;

            _contentH = y - r.y + SlopWidgets.GapS;
        }

        void PickNetwork()
        {
            var options = new List<FloatMenuOption>();
            foreach (NetworkMode mode in new[]
            {
                NetworkMode.None, NetworkMode.Private, NetworkMode.Host,
            })
            {
                var picked = mode;
                options.Add(new FloatMenuOption(NetworkModeText.Label(picked),
                    () => _p.Network = picked));
            }

            Find.WindowStack.Add(new SlopMenu(options));
        }

        void Save()
        {
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

    }
}
