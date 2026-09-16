using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A project owns a directory and the mounts shared by every agent in it.
    public class ProjectsView : UiListView<ProjectInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.Catalog.RefreshProjects();
        }

        public override string Title => "Projects";

        // The second line holds a row button, so the pitch is off that rather than off two
        // line heights - see SessionsView, which has the same two-line row.
        protected override float RowH => UiListRow.TwoLineH;

        protected override string EmptyNote =>
            "No projects yet. Add one, then put an agent in it.";

        protected override IList<ProjectInfo> Rows => SessionHub.Instance.Projects;

        readonly ProjectSessionCounts _counts = new ProjectSessionCounts();

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new UiLayout.Bar(bar);

            if (row.Left("Add project", UiTheme.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditProjectDialog(null));

            if (row.Right("Reload", UiTheme.Btn.Ghost))
                hub.Catalog.RefreshProjects(UiLayout.Fail);
        }

        protected override void DrawRow(Rect r, ProjectInfo p)
        {
            UiListRow.Prepare(r);

            // The two lines of the row, off the font rather than off a pair of figures four
            // pixels apart: `Widgets.Label` clips to the rect it is handed, so a literal here
            // is one that crops descenders on any font but the one it was chosen against.
            float l1 = UiListRow.LineY(r, 0), l2 = UiListRow.LineY(r, 1);

            // The name's column, measured: 200 and 214 held for one face at one size.
            float nameW = Mathf.Max(UiTheme.Wide("mmmmmmmmmmmmmmmm"), 200f);

            GUI.color = UiTheme.Lead;
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, l1, nameW, UiTheme.LineH), p.Name);

            // The number that decides whether this project can be deleted at all.
            var hub = SessionHub.Instance;
            int agents = _counts.Get(hub.Sessions, hub.SessionsVersion, p.Name);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS + nameW + UiTheme.GapS, l1,
                    UiTheme.Wide("99 agents") + 4f, UiTheme.LineH),
                agents == 1 ? "1 agent" : $"{agents} agents");

            // Cut rather than wrapped: a directory and a summary beside it have spaces in
            // them, and a wrapped line in a one-line slot loses the half of each that is
            // outside the rect.
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, l2, Mathf.Max(60f, r.width - 150f),
                    UiTheme.LineH), $"{p.Dir}  ({Summary(p)})");
            GUI.color = Color.white;

            float right = UiListRow.Right(r);
            float actW = Mathf.Max(UiLayout.BtnW("Delete", 120f), UiLayout.BtnW("Edit", 120f));

            if (UiButtons.Button(new Rect(right - actW, r.y + 1f, actW, UiTheme.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditProjectDialog(p));

            if (UiButtons.Button(new Rect(right - actW, l2, actW, UiTheme.RowBtnH), "Delete",
                    UiTheme.Btn.Danger))
                TerminalWindow.OpenOverPane(CatalogActions.RemoveProject(p.Name));
        }

        // The workspace in one line, the way the project rows read it.
        public static string Summary(ProjectInfo p)
        {
            var bits = new List<string>();
            // First, because it is the one thing here about the ground rather than about the
            // sandbox around it.
            if (p.Temp) bits.Add("temporary");
            bits.Add(p.Mounts.Count == 0 ? "no extra mounts" : p.Mounts.Count + " shared mounts");
            return string.Join(", ", bits.ToArray());
        }
    }

    // Projects edit workspace identity and shared path mounts.
    public class EditProjectDialog : UiWindow
    {
        enum Tab { General, Mounts }

        readonly EditIdentity _identity;
        readonly ProjectInfo _p;

        readonly SmoothScroll _mountsScroll = new SmoothScroll();
        readonly ScrollableListing _generalListing = new ScrollableListing(320f);
        readonly TempProjectPreviewState _tempPreview = new TempProjectPreviewState();
        Tab _tab;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        // Copy a project's directory and mounts; only the name is regenerated because the daemon treats the result as new.
        public static EditProjectDialog Copy(ProjectInfo of) => new EditProjectDialog(of, true);

        EditProjectDialog(ProjectInfo existing, bool copy)
        {
            // A copy is a new project in every way that matters here: nothing on the daemon
            // knows it, so Save posts rather than puts and there is no rename to carry any
            // agents across.
            _identity = copy ? EditIdentity.ForCopy(existing?.Name) :
                existing == null ? EditIdentity.ForNew() : EditIdentity.ForEdit(existing.Name);
            _p = existing?.Copy() ?? new ProjectInfo();
            if (copy)
            {
                _p.Name = _identity.CopyName(SessionHub.Instance.Projects.Select(p => p.Name),
                    "project");
                // A temporary project's ground is named after the project, so the copy's is
                // named after the copy rather than pointing back at what it came from.
                if (_p.Temp) RequestTempPreview();
            }
            resizeable = true;
            AcceptOnEnter(Save);
        }

        // Project identity and shared mounts each get their own tab.
        public override Vector2 InitialSize => new Vector2(780f, 680f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _identity.Title("project"));

            var layout = TabbedFormLayout.Arrange(SettingsPageLayout.FromRect(rect), 132f,
                UiTheme.HeaderH, UiTheme.BtnH, UiTheme.GapS, UiTheme.GapM);
            DrawRail(SettingsPageLayout.ToRect(layout.Rail));
            var body = SettingsPageLayout.ToRect(layout.Body);

            switch (_tab)
            {
                case Tab.General:
                    _generalListing.Draw(body, DrawGeneral);
                    break;
                case Tab.Mounts:
                    DrawMounts(body);
                    break;

            }

            var foot = new UiLayout.Bar(SettingsPageLayout.ToRect(layout.Footer));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => UiLayout.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Mounts", Tab.Mounts),
        }, ref _tab);

        // The project itself: its name, directory and whether that directory is temporary.
        void DrawGeneral(Listing_Standard l)
        {
            // Begun on the room it has and pinned to one column. Listing_Standard breaks to a
            // second column the moment a control would cross the bottom of the rect it was
            // begun on - curX past the whole width, so everything after is clipped away by
            // the group, and CurHeight back to nearly nothing.
            l.Label("Name");
            _p.Name = UiControls.Field(l, "project.name", _p.Name);

            _p.Temp = UiControls.Checkbox(l, "Temporary - scratch space under /tmp", _p.Temp,
                "The directory is made for you under " + ProjectInfo.TempRoot + ", named after " +
                "this project, and it is there the first time an agent starts. Nothing " +
                "deletes it; the machine clears /tmp.");

            l.Label("Directory");
            if (_p.Temp)
            {
                // Stated rather than hidden: the path is the daemon's to coin and this is what
                // it will coin. Browse goes with it - there is nothing to find yet.
                RequestTempPreview();
                UiControls.Field(l, "project.dir", _tempPreview.Status, false);
                if (!string.IsNullOrEmpty(_tempPreview.Error))
                    UiLayout.Note(l, _tempPreview.Status);
            }
            else
            {
                _tempPreview.Cancel();
                _p.Dir = UiControls.Field(l, "project.dir", _p.Dir);
                if (UiLayout.Button(l, "Browse..."))
                    TerminalWindow.OpenOverPane(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

        }

        void DrawMounts(Rect rect)
        {
            UiText.RowLabel(new Rect(rect.x, rect.y, rect.width, UiTheme.LineH),
                "Mounts apply at next start. Add project copies its current paths into an editable row.");
            float y = rect.y + UiTheme.LineH + UiTheme.GapS;
            if (UiButtons.Button(new Rect(rect.x, y, 110f, UiTheme.RowH), "Add path", UiTheme.Btn.Default))
                _p.Mounts.Add(new MountEntry());
            if (UiButtons.Button(new Rect(rect.x + 120f, y, 120f, UiTheme.RowH), "Add project", UiTheme.Btn.Default))
            {
                var projects = SessionHub.Instance.Projects.Where(p => p.Name != _p.Name).ToList();
                if (!string.IsNullOrWhiteSpace(_p.Name)) projects.Add(_p);
                var options = projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new FloatMenuOption(p.Name, () => _p.Mounts.Add(new MountEntry
                    {
                        From = p.Dir,
                        To = p.Name == _p.Name ? p.Dir : "/mnt/" + p.Name,
                    }))).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("No projects", null));
                Find.WindowStack.Add(new UiMenu(options));
            }
            y += UiTheme.RowH + UiTheme.GapS;
            float modeW = 100f, removeW = 32f, gap = UiTheme.GapS;
            float width = rect.width - UiTheme.ListInset * 2f - UiTheme.ScrollbarW;
            float pathW = (width - modeW - removeW - gap * 3f) / 2f;
            UiText.RowLabel(new Rect(rect.x + UiTheme.ListInset, y, pathW, UiTheme.LineH), "From (host path)");
            UiText.RowLabel(new Rect(rect.x + UiTheme.ListInset + pathW + gap, y, pathW, UiTheme.LineH), "To (sandbox path)");
            y += UiTheme.LineH;
            var listRect = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Slab.Box(listRect, UiTheme.Well, UiTheme.Edge);
            var pad = listRect.ContractedBy(UiTheme.ListInset);
            var inner = new Rect(0f, 0f, width, _p.Mounts.Count * (UiTheme.RowH + gap));
            MountEntry remove = null;
            using (_mountsScroll.Scope(pad, inner))
            {
                float ry = 0f;
                foreach (var mount in _p.Mounts)
                {
                    mount.From = UiText.Field(new Rect(0f, ry, pathW, UiTheme.RowH),
                        "mount.from." + mount.FieldId, mount.From);
                    mount.To = UiText.Field(new Rect(pathW + gap, ry, pathW, UiTheme.RowH),
                        "mount.to." + mount.FieldId, mount.To);
                    if (UiButtons.Button(new Rect(2f * (pathW + gap), ry, modeW, UiTheme.RowH),
                        MountEntry.ModeLabel(mount.Mode), UiTheme.Btn.Ghost))
                        Find.WindowStack.Add(new UiMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption("Read-only", () => mount.Mode = MountMode.Ro),
                            new FloatMenuOption("Read-write", () => mount.Mode = MountMode.Rw),
                        }));
                    if (UiButtons.Button(new Rect(width - removeW, ry, removeW, UiTheme.RowH), "×", UiTheme.Btn.Ghost))
                        remove = mount;
                    ry += UiTheme.RowH + gap;
                }
            }
            if (remove != null) _p.Mounts.Remove(remove);
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_p.Name ?? "").Trim()))
            {
                UiLayout.Fail("a project needs a name");
                return;
            }
            // A temporary project's directory is the daemon's to coin, and it coins it again
            // on the way in - this is only so the list has the right path before the answer
            // comes back.
            if (_p.Temp)
                _p.Dir = _tempPreview.Name == _p.Name ? _tempPreview.Dir : null;
            else if (string.IsNullOrEmpty((_p.Dir ?? "").Trim()))
            {
                UiLayout.Fail("a project needs a directory");
                return;
            }

            SessionHub.Instance.Catalog.SaveProject(_p, _identity.IsNew,
                _identity.OriginalName,
                ok: () => Close(),
                fail: UiLayout.Fail);
        }

        void RequestTempPreview()
        {
            string name = _p.Name ?? "";
            DateTime now = DateTime.UtcNow;
            if (!_tempPreview.ShouldRequest(name, now)) return;
            int serial = _tempPreview.Begin(name, now);
            if (serial == 0) return;
            DaemonClient.Post(WireProtocol.Routes.ProjectPreview,
                "{" + $"\"name\":{JVal.Q(name)},\"temp\":true" + "}",
                j =>
                {
                    string dir = j["dir"].AsString();
                    if (string.IsNullOrEmpty(dir))
                        _tempPreview.Fail(serial, _p.Temp, _p.Name,
                            "Daemon preview did not include a path.", DateTime.UtcNow);
                    else
                        _tempPreview.Accept(serial, _p.Temp, _p.Name, dir);
                },
                error =>
                {
                    _tempPreview.Fail(serial, _p.Temp, _p.Name, error, DateTime.UtcNow);
                });
        }

    }
}
