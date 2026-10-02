using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Projects edit workspace identity and shared path mounts.
    public class EditProjectDialog : UiWindow
    {
        enum Tab { General, Mounts, Worktrees }
        ProjectWorktrees _worktrees;

        readonly EditIdentity _identity;
        readonly ProjectInfo _p;

        readonly SmoothScroll _mountsScroll = new SmoothScroll();
        readonly ScrollableListing _generalListing = new ScrollableListing(320f);
        readonly TempProjectPreviewState _tempPreview = new TempProjectPreviewState();
        Tab _tab;
        bool _saving;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        public static EditProjectDialog ForWorktrees(ProjectInfo existing) =>
            new EditProjectDialog(existing, false, Tab.Worktrees);

        // Copy a project's directory and mounts. Only the name is regenerated because the daemon treats the result as new.
        public static EditProjectDialog Copy(ProjectInfo of) => new EditProjectDialog(of, true);

        EditProjectDialog(ProjectInfo existing, bool copy, Tab tab = Tab.General)
        {
            // A copy is a new project in every way that matters here: nothing on the daemon knows
            // it. Therefore, save posts rather than puts and there is no rename to carry any agents
            // across.
            _identity = copy ? EditIdentity.ForCopy(existing?.Name) :
                existing == null ? EditIdentity.ForNew() : EditIdentity.ForEdit(existing.Name);
            _p = existing?.Copy() ?? new ProjectInfo();
            _tab = tab;
            if (existing != null && !copy) _worktrees = new ProjectWorktrees(existing);
            if (copy)
            {
                _p.Name = _identity.CopyName(SessionHub.Instance.Projects.Select(p => p.Name),
                    "project");
                // Temporary project storage uses the project name as its directory.
                // The copy therefore gets a new directory name.
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
                case Tab.Worktrees:
                    if (_worktrees == null) Widgets.Label(body, "Save the project before managing worktrees.");
                    else _worktrees.Draw(body);
                    break;

            }

            var foot = new UiLayout.Bar(SettingsPageLayout.ToRect(layout.Footer));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary, !_saving)) Save();
        }

        void DrawRail(Rect r) => UiLayout.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Mounts", Tab.Mounts),
            ("Worktrees", Tab.Worktrees),
        }, ref _tab);

        // The project itself: its name, directory and whether that directory is temporary.
        void DrawGeneral(Listing_Standard l)
        {
            // Limit this listing to one column in the available rect.
            // Listing_Standard otherwise creates an offscreen column when a control crosses the bottom.
            // It also resets CurHeight, which would corrupt later layout.
            l.Label("Name");
            _p.Name = UiControls.Field(l, "project.name", _p.Name);
            l.Label("Managed worktree root (optional)");
            _p.WorktreeRoot = UiControls.Field(l, "project.worktree-root", _p.WorktreeRoot);

            _p.Temp = UiControls.Checkbox(l, "Temporary - scratch space under /tmp", _p.Temp,
                _identity.IsNew
                    ? "SlopWorld creates the directory under " + ProjectInfo.TempRoot + ". It uses the project name. " +
                      "The directory exists when the first agent starts. SlopWorld " +
                      "does not delete it. The machine clears /tmp."
                    : "You cannot change temporary mode after the daemon creates the project.",
                locked: !_identity.IsNew);

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
                if (UiLayout.Button(l, "Choose directory"))
                    TerminalWindow.OpenOverPane(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

        }

        void DrawMounts(Rect rect)
        {
            UiText.RowLabel(new Rect(rect.x, rect.y, rect.width, UiTheme.LineH),
                "Mounts apply at the next start. Absolute paths stay absolute. Relative paths start at the project directory.");
            float y = rect.y + UiTheme.LineH + UiTheme.GapS;
            if (UiButtons.Button(new Rect(rect.x, y, 110f, UiTheme.RowH), "Add path", UiTheme.Btn.Default))
                _p.Mounts.Add(new MountEntry());
            if (UiButtons.Button(new Rect(rect.x + 120f, y, 120f, UiTheme.RowH), "Add project", UiTheme.Btn.Default))
            {
                var projects = SessionHub.Instance.Projects.Where(p => p.Name != _p.Name).ToList();
                var options = projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new FloatMenuOption(p.Name, () => _p.Mounts.Add(new MountEntry
                    {
                        From = p.Dir,
                        To = p.Dir,
                    }))).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("No projects", null));
                Find.WindowStack.Add(new UiMenu(options));
            }
            y += UiTheme.RowH + UiTheme.GapS;
            float modeW = 100f, removeW = 32f, gap = UiTheme.GapS;
            float width = rect.width - UiTheme.ListInset * 2f - UiTheme.ScrollbarW;
            float pathW = (width - modeW - removeW - gap * 3f) / 2f;
            UiText.RowLabel(new Rect(rect.x + UiTheme.ListInset, y, pathW, UiTheme.LineH), "From (blank = managed cache)");
            UiText.RowLabel(new Rect(rect.x + UiTheme.ListInset + pathW + gap, y, pathW, UiTheme.LineH),
                "To (absolute or project-relative sandbox path)");
            y += UiTheme.LineH;
            var listRect = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Slab.Box(listRect, UiTheme.Well, UiTheme.Edge);
            var pad = listRect.ContractedBy(UiTheme.ListInset);
            MountEntry projectMount = _p.EnsurePrimaryMount();
            var inner = new Rect(0f, 0f, width, _p.Mounts.Count * (UiTheme.RowH + gap));
            MountEntry remove = null;
            using (_mountsScroll.Scope(pad, inner))
            {
                float ry = 0f;
                foreach (var mount in _p.Mounts.OrderBy(m => m == projectMount ? 0 : 1))
                {
                    bool isProject = mount == projectMount;
                    if (isProject)
                    {
                        UiText.ReadOnlyField(new Rect(0f, ry, pathW, UiTheme.RowH),
                            "mount.from." + mount.FieldId, mount.From);
                        UiText.ReadOnlyField(new Rect(pathW + gap, ry, pathW, UiTheme.RowH),
                            "mount.to." + mount.FieldId, mount.To);
                    }
                    else
                    {
                        mount.From = UiText.Field(new Rect(0f, ry, pathW, UiTheme.RowH),
                            "mount.from." + mount.FieldId, mount.From);
                        mount.To = UiText.Field(new Rect(pathW + gap, ry, pathW, UiTheme.RowH),
                            "mount.to." + mount.FieldId, mount.To);
                    }
                    if (UiButtons.Button(new Rect(2f * (pathW + gap), ry, modeW, UiTheme.RowH),
                        MountPresentation.ModeLabel(mount.Mode), UiTheme.Btn.Ghost))
                    {
                        var modes = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("Read-only", () => mount.Mode = MountMode.Ro),
                            new FloatMenuOption("Read-write", () => mount.Mode = MountMode.Rw),
                        };
                        if (!isProject) modes.Add(new FloatMenuOption("Cache", () => mount.Mode = MountMode.Cache));
                        Find.WindowStack.Add(new UiMenu(modes));
                    }
                    if (!isProject && UiButtons.Button(new Rect(width - removeW, ry, removeW, UiTheme.RowH), "×", UiTheme.Btn.Ghost))
                        remove = mount;
                    ry += UiTheme.RowH + gap;
                }
            }
            if (remove != null) _p.Mounts.Remove(remove);
        }

        void Save()
        {
            if (_saving) return;
            if (string.IsNullOrEmpty((_p.Name ?? "").Trim()))
            {
                UiLayout.Fail("a project needs a name");
                return;
            }
            // The daemon selects the temporary project directory and selects it again during creation.
            // Set this preview so the list shows the expected path before the daemon responds.
            if (_p.Temp)
            {
                if (_tempPreview.Name != _p.Name || string.IsNullOrEmpty(_tempPreview.Dir))
                {
                    UiLayout.Fail("temporary project path is not available yet");
                    return;
                }
                _p.Dir = _tempPreview.Dir;
            }
            else if (string.IsNullOrEmpty((_p.Dir ?? "").Trim()))
            {
                UiLayout.Fail("a project needs a directory");
                return;
            }

            _p.EnsurePrimaryMount();
            _saving = true;
            SessionHub.Instance.Catalog.SaveProject(_p, _identity.IsNew,
                _identity.OriginalName,
                ok: () => Close(),
                fail: error => { _saving = false; UiLayout.Fail(error); });
        }

        void RequestTempPreview()
        {
            string name = _p.Name ?? "";
            DateTime now = DateTime.UtcNow;
            if (!_tempPreview.ShouldRequest(name, now)) return;
            int serial = _tempPreview.Begin(name, now);
            if (serial == 0) return;
            DaemonClient.Post<Wire.ProjectPreviewResult>(WireProtocol.Routes.ProjectPreview,
                new Wire.ProjectPreviewReq { Name = name, Temp = true },
                j =>
                {
                    string dir = j.Dir;
                    if (string.IsNullOrEmpty(dir))
                        _tempPreview.Fail(serial, _p.Temp, _p.Name,
                            "Daemon preview did not include a path.", DateTime.UtcNow);
                    else if (_tempPreview.Accept(serial, _p.Temp, _p.Name, dir))
                    {
                        _p.Dir = dir;
                        _p.EnsurePrimaryMount();
                    }
                },
                error =>
                {
                    _tempPreview.Fail(serial, _p.Temp, _p.Name, error, DateTime.UtcNow);
                });
        }

    }
}
