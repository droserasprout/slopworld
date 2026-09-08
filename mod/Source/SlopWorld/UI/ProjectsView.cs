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
            SessionHub.Instance.RefreshProjects();
            SessionHub.Instance.LoadPresets();
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
            var row = new UiWidgets.Bar(bar);

            if (row.Left("Add project", UiWidgets.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditProjectDialog(null));

            if (row.Right("Reload", UiWidgets.Btn.Ghost))
                hub.RefreshProjects(UiWidgets.Fail);
        }

        protected override void DrawRow(Rect r, ProjectInfo p)
        {
            UiListRow.Prepare(r);

            // The two lines of the row, off the font rather than off a pair of figures four
            // pixels apart: `Widgets.Label` clips to the rect it is handed, so a literal here
            // is one that crops descenders on any font but the one it was chosen against.
            float l1 = UiListRow.LineY(r, 0), l2 = UiListRow.LineY(r, 1);

            // The name's column, measured: 200 and 214 held for one face at one size.
            float nameW = Mathf.Max(UiWidgets.Wide("mmmmmmmmmmmmmmmm"), 200f);

            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(
                new Rect(r.x + UiWidgets.GapS, l1, nameW, UiWidgets.LineH), p.Name);

            // The number that decides whether this project can be deleted at all.
            var hub = SessionHub.Instance;
            int agents = _counts.Get(hub.Sessions, hub.SessionsVersion, p.Name);
            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(
                new Rect(r.x + UiWidgets.GapS + nameW + UiWidgets.GapS, l1,
                    UiWidgets.Wide("99 agents") + 4f, UiWidgets.LineH),
                agents == 1 ? "1 agent" : $"{agents} agents");

            // Cut rather than wrapped: a directory and a summary beside it have spaces in
            // them, and a wrapped line in a one-line slot loses the half of each that is
            // outside the rect.
            UiWidgets.RowLabel(
                new Rect(r.x + UiWidgets.GapS, l2, Mathf.Max(60f, r.width - 150f),
                    UiWidgets.LineH), $"{p.Dir}  ({Summary(p)})");
            GUI.color = Color.white;

            float right = UiListRow.Right(r);
            float actW = Mathf.Max(UiWidgets.BtnW("Delete", 120f), UiWidgets.BtnW("Edit", 120f));

            if (UiWidgets.Button(new Rect(right - actW, r.y + 1f, actW, UiWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditProjectDialog(p));

            if (UiWidgets.Button(new Rect(right - actW, l2, actW, UiWidgets.RowBtnH), "Delete",
                    UiWidgets.Btn.Danger))
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
        enum Tab { General, Sandbox, Breadcrumbs, Preview }

        readonly bool _isNew;
        readonly ProjectInfo _p;
        // A changed name in the field is a rename, and the daemon carries its sessions
        // over.
        readonly string _origName;

        // For the title. Null unless it is a duplicate: an edit already has `_origName`.
        readonly string _copiedFrom;

        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly SmoothScroll _generalScroll = new SmoothScroll();
        readonly SmoothScroll _sandboxScroll = new SmoothScroll();
        const float PresetsH = 240f;
        string _dnsServers;
        Tab _tab;

        // Last frame's content height per scrolling tab, so each can grow a scrollbar when its
        // fields and fixed lists do not fit - the body width the rail leaves varies with UI scale.
        float _generalH = 320f;
        float _sandboxH = 400f;

        public EditProjectDialog(ProjectInfo existing) : this(existing, false) { }

        // Copy a project's directory and presets; only the name is regenerated because the daemon treats the result as new.
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
                _p.Name = UiWidgets.FreeName(_p.Name,
                    SessionHub.Instance.Projects.Select(p => p.Name), "project");
                // A temporary project's ground is named after the project, so the copy's is
                // named after the copy rather than pointing back at what it came from.
                if (_p.Temp) _p.Dir = ProjectInfo.TempDir(_p.Name);
            }
            _dnsServers = _p.Dns.Mode == DnsMode.Servers
                ? string.Join(", ", _p.Dns.Servers.ToArray())
                : "";

            resizeable = true;
            AcceptOnEnter(Save);

            SessionHub.Instance.LoadPresets(fail: UiWidgets.Fail);
        }

        // A left rail of short pages rather than one long form: the project, its sandbox, its
        // breadcrumbs and the preview each get their own tab so none has to hold the others.
        public override Vector2 InitialSize => new Vector2(780f, 680f);

        protected override void DoBody(Rect rect)
        {
            UiWidgets.Title(TitleRect(rect), _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New project" : $"Edit '{_origName}'");

            float top = rect.y + UiWidgets.HeaderH + UiWidgets.GapS;
            float bottom = rect.yMax - UiWidgets.BtnH - UiWidgets.GapS;

            const float railW = 132f;
            DrawRail(new Rect(rect.x, top, railW, bottom - top));
            var body = new Rect(rect.x + railW + UiWidgets.GapM, top,
                rect.width - railW - UiWidgets.GapM, bottom - top);

            switch (_tab)
            {
                case Tab.General:
                {
                    var view = new Rect(0f, 0f, body.width - UiWidgets.ScrollbarW,
                        Mathf.Max(_generalH, body.height));
                    using (_generalScroll.Scope(body, view))
                        _generalH = DrawGeneral(view);
                    break;
                }
                case Tab.Sandbox:
                {
                    var view = new Rect(0f, 0f, body.width - UiWidgets.ScrollbarW,
                        Mathf.Max(_sandboxH, body.height));
                    using (_sandboxScroll.Scope(body, view))
                        _sandboxH = DrawSandbox(view, body.height);
                    break;
                }
                case Tab.Breadcrumbs:
                    DrawBreadcrumbs(body);
                    break;
                case Tab.Preview:
                    SandboxPreviewPanel.Draw(body, ref _previewScroll,
                        SandboxPreviewData.ForProject(_p));
                    break;
            }

            var foot = new UiWidgets.Bar(UiWidgets.FooterBar(rect));
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", UiWidgets.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => UiWidgets.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Sandbox", Tab.Sandbox),
            ("Breadcrumbs", Tab.Breadcrumbs),
            ("Preview", Tab.Preview),
        }, ref _tab);

        // The project itself: its name, directory and whether that directory is temporary.
        // The other tabs refine the sandbox around it.
        float DrawGeneral(Rect rect)
        {
            // Begun on the room it has and pinned to one column. Listing_Standard breaks to a
            // second column the moment a control would cross the bottom of the rect it was
            // begun on - curX past the whole width, so everything after is clipped away by
            // the group, and CurHeight back to nearly nothing.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            l.Label("Name");
            _p.Name = UiWidgets.Field(l, "project.name", _p.Name);

            _p.Temp = UiWidgets.Checkbox(l, "Temporary - scratch space under /tmp", _p.Temp,
                "The directory is made for you under " + ProjectInfo.TempRoot + ", named after " +
                "this project, and it is there the first time an agent starts. Nothing " +
                "deletes it; the machine clears /tmp.");

            l.Label("Directory");
            if (_p.Temp)
            {
                // Stated rather than hidden: the path is the daemon's to coin and this is what
                // it will coin. Browse goes with it - there is nothing to find yet.
                UiWidgets.Field(l, "project.dir", ProjectInfo.TempDir(_p.Name), false);
            }
            else
            {
                _p.Dir = UiWidgets.Field(l, "project.dir", _p.Dir);
                if (UiWidgets.Button(l, "Browse..."))
                    TerminalWindow.OpenOverPane(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

            float used = l.CurHeight;
            l.End();
            return used + UiWidgets.GapS;
        }

        // The sandbox every agent in this project gets by default: network, DNS and the extra presets.
        float DrawSandbox(Rect rect, float availableHeight)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            var networkChoices = new[]
            {
                NetworkMode.None, NetworkMode.Private, NetworkMode.Host,
            };
            UiWidgets.Select(l, "Network default", NetworkModeText.Label(_p.Network),
                networkChoices.Select(mode => new SelectorOption(NetworkModeText.Label(mode),
                    () => _p.Network = mode)), out _);
            GUI.color = UiWidgets.Dim;
            l.Label(_p.Network == NetworkMode.Host
                ? SessionHub.Instance.Capabilities.HostNetworkIsContainer
                    ? "Agents share slopcar's network. Mac services are at host.docker.internal."
                    : "Agents may use the host network, including local services."
                : _p.Network == NetworkMode.Private
                    ? "Agents may use the Internet through a private namespace."
                    : "Agents have no network access.");
            GUI.color = Color.white;

            l.Gap(UiWidgets.GapS);
            UiWidgets.Select(l, "DNS", _p.Dns.Label, new[]
            {
                new SelectorOption("System resolver", () => _p.Dns = DnsConfig.Resolved()),
                new SelectorOption("Custom DNS servers", () =>
                {
                    if (_p.Dns.Mode != DnsMode.Servers) _p.Dns = DnsConfig.Custom();
                }),
            }, out _);
            if (_p.Dns.Mode == DnsMode.Servers)
            {
                _dnsServers = UiWidgets.Field(l, "project.dns", _dnsServers ?? "");
                GUI.color = UiWidgets.Dim;
                l.Label("Comma-separated IPv4 addresses; maximum two. Changes apply on restart.");
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = UiWidgets.Dim;
                l.Label("System resolver follows the daemon's current resolv.conf.");
                GUI.color = Color.white;
            }

            float used = l.CurHeight;
            l.End();

            float y = rect.y + used + UiWidgets.GapL;
            UiWidgets.SectionHeading(new Rect(rect.x, y, rect.width, UiWidgets.RowH),
                "Sandbox presets");
            y += UiWidgets.RowH + UiWidgets.GapXS;

            // The viewport stays stable even when the form needs an outer scroll view.
            float height = Mathf.Max(PresetsH, rect.y + availableHeight - y - UiWidgets.GapS);
            PresetList.Draw(new Rect(rect.x, y, rect.width, height), _p.Sandbox, _presetScroll);
            y += height;

            return y - rect.y + UiWidgets.GapS;
        }

        void DrawBreadcrumbs(Rect rect)
        {
            float y = rect.y;
            UiWidgets.SectionHeading(new Rect(rect.x, y, rect.width, UiWidgets.RowH),
                "Prompt breadcrumbs");
            y += UiWidgets.RowH + UiWidgets.GapXS;
            BreadcrumbList.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)),
                _p.Breadcrumbs, _breadcrumbScroll);
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_p.Name ?? "").Trim()))
            {
                UiWidgets.Fail("a project needs a name");
                return;
            }
            // A temporary project's directory is the daemon's to coin, and it coins it again
            // on the way in - this is only so the list has the right path before the answer
            // comes back.
            if (_p.Temp) _p.Dir = ProjectInfo.TempDir(_p.Name);
            else if (string.IsNullOrEmpty((_p.Dir ?? "").Trim()))
            {
                UiWidgets.Fail("a project needs a directory");
                return;
            }

            List<string> dnsServers = null;
            string dnsError;
            if (_p.Dns.Mode == DnsMode.Servers &&
                !DnsConfig.TryParseServers(_dnsServers, out dnsServers, out dnsError))
            {
                UiWidgets.Fail("DNS: " + dnsError);
                return;
            }
            if (_p.Dns.Mode == DnsMode.Servers)
                _p.Dns.Servers = dnsServers;

            SessionHub.Instance.SaveProject(_p, _isNew, _origName,
                ok: () => Close(),
                fail: UiWidgets.Fail);
        }

    }
}
