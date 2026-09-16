using System;
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
    public class ProjectsView : UiListView<ProjectInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.Catalog.RefreshProjects();
            SessionHub.Instance.Catalog.LoadPresets();
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

        // The sandbox in one line, the way the agent rows read it.
        public static string Summary(ProjectInfo p)
        {
            var bits = new List<string>();
            // First, because it is the one thing here about the ground rather than about the
            // sandbox around it.
            if (p.Temp) bits.Add("temporary");
            bits.Add(NetworkModeText.ShortLabel(p.Network));
            bits.Add(p.Dns.IsResolved ? "system DNS" : "custom DNS");
            bits.AddRange(p.Sandbox);
            return string.Join(", ", bits.ToArray());
        }
    }

    // Presets are checkboxes drawn from whatever the daemon says it knows, so this
    // never has to be kept in step with sandbox.rs by hand.
    public class EditProjectDialog : UiWindow
    {
        enum Tab { General, Sandbox, ResourceLimits, Breadcrumbs, Preview }

        readonly EditIdentity _identity;
        readonly ProjectInfo _p;

        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly DaemonSettingsPreview _settingsPreview = new DaemonSettingsPreview();
        readonly ScrollableListing _generalListing = new ScrollableListing(320f);
        readonly ScrollableListing _sandboxListing = new ScrollableListing(400f);
        const float PresetsH = 240f;
        string _dnsServers;
        readonly ResourceLimitsForm _resourceLimits;
        readonly ScrollableListing _limitsListing = new ScrollableListing(320f);
        readonly TempProjectPreviewState _tempPreview = new TempProjectPreviewState();
        Tab _tab;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        // Copy a project's directory and presets; only the name is regenerated because the daemon treats the result as new.
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
            _resourceLimits = new ResourceLimitsForm(_p.Limits);
            _dnsServers = _p.Dns.Mode == DnsMode.Servers
                ? string.Join(", ", _p.Dns.Servers.ToArray())
                : "";

            resizeable = true;
            AcceptOnEnter(Save);

            SessionHub.Instance.Catalog.LoadPresets(fail: UiLayout.Fail);
        }

        // A left rail of short pages rather than one long form: the project, its sandbox, its
        // breadcrumbs and the preview each get their own tab so none has to hold the others.
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
                case Tab.Sandbox:
                    _sandboxListing.Draw(body, DrawSandboxFields,
                        (view, y) => DrawSandboxTrailing(view, y, body.height));
                    break;
                case Tab.ResourceLimits:
                    _limitsListing.Draw(body, l =>
                    {
                        if (!SessionHub.Instance.Capabilities.PerSessionLimits)
                        {
                            UiLayout.Note(l, "This runtime uses one outer resource budget; per-agent limits are unavailable.");
                            return;
                        }
                        UiLayout.Note(l, "Defaults for agents in this project. Blank means no project cap.");
                        _p.Limits = _resourceLimits.Draw(l);
                    });
                    break;
                case Tab.Breadcrumbs:
                    DrawBreadcrumbs(body);
                    break;
                case Tab.Preview:
                    if (!_resourceLimits.TrySave(out var previewLimits, out var limitError))
                        UiText.StatusLabel(body, limitError, UiTheme.Bad);
                    else if (DnsForm.TrySave(_p.Dns, _dnsServers, out var dnsError))
                    {
                        _p.Limits = previewLimits;
                        _settingsPreview.Draw(body, "{\"project\":" + _p.ToJson() + "}", ref _previewScroll);
                    }
                    else UiText.StatusLabel(body, "DNS: " + dnsError, UiTheme.Bad);
                    break;
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.ToRect(layout.Footer));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => UiLayout.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Sandbox", Tab.Sandbox),
            ("Resource limits", Tab.ResourceLimits),
            ("Breadcrumbs", Tab.Breadcrumbs),
            ("Preview", Tab.Preview),
        }, ref _tab,
            tab => tab != Tab.Breadcrumbs || SessionHub.Instance.Config.ExperimentalBreadcrumbs);

        // The project itself: its name, directory and whether that directory is temporary.
        // The other tabs refine the sandbox around it.
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

        // The sandbox every agent in this project gets by default: network, DNS and the extra presets.
        void DrawSandboxFields(Listing_Standard l)
        {
            var networkChoices = new[]
            {
                NetworkMode.None, NetworkMode.Private, NetworkMode.Host,
            };
            UiControls.Select(l, "Network default", NetworkModeText.Label(_p.Network),
                networkChoices.Select(mode => new SelectorOption(NetworkModeText.Label(mode),
                    () => _p.Network = mode)), out _);
            GUI.color = UiTheme.Dim;
            l.Label(_p.Network == NetworkMode.Host
                ? SessionHub.Instance.Capabilities.HostNetworkIsContainer
                    ? "Agents share slopcar's network. Mac services are at host.docker.internal."
                    : "Agents may use the host network, including local services."
                : _p.Network == NetworkMode.Private
                    ? "Agents may use the Internet through a private namespace."
                    : "Agents have no network access.");
            GUI.color = Color.white;

            l.Gap(UiTheme.GapS);
            DnsForm.Draw(l, _p.Dns, null, false, "project.dns", ref _dnsServers,
                dns => _p.Dns = dns);

        }

        float DrawSandboxTrailing(Rect rect, float y, float availableHeight)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Sandbox presets");
            y += UiTheme.RowH + UiTheme.GapXS;

            // The viewport stays stable even when the form needs an outer scroll view.
            float height = Mathf.Max(PresetsH, rect.y + availableHeight - y - UiTheme.GapS);
            PresetList.Draw(new Rect(rect.x, y, rect.width, height), _p.Sandbox, _presetScroll);
            y += height;

            return y;
        }

        void DrawBreadcrumbs(Rect rect)
        {
            float y = rect.y;
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Prompt breadcrumbs");
            y += UiTheme.RowH + UiTheme.GapXS;
            BreadcrumbList.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)),
                _p.Breadcrumbs, _breadcrumbScroll,
                breadcrumbsLocked: !SessionHub.Instance.Config.ExperimentalBreadcrumbs);
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

            if (!_resourceLimits.TrySave(out var limits, out var limitError))
            {
                UiLayout.Fail(limitError);
                return;
            }
            _p.Limits = limits;
            string dnsError;
            if (!DnsForm.TrySave(_p.Dns, _dnsServers, out dnsError))
            {
                UiLayout.Fail("DNS: " + dnsError);
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
