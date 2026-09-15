using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What is left here is the two things about the agent: its name and what it runs.
    // Where it works moved to the project, which is why picking one is mandatory and
    // there is no directory field; network reach has an agent-level override here.
    public partial class EditSessionDialog : UiWindow
    {
        enum Tab { General, Mounts, Sandbox, ResourceLimits, Breadcrumbs, Preview }

        readonly EditIdentity _identity;
        readonly SessionInfo _s;
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly SmoothScroll _mountsScroll = new SmoothScroll();
        readonly ScrollableListing _generalListing = new ScrollableListing(320f);
        readonly ScrollableListing _sandboxListing = new ScrollableListing(480f);
        readonly ScrollableListing _limitsListing = new ScrollableListing(320f);
        const float PresetsH = 240f;
        Tab _tab;

        // Limits are edited as raw strings so a half-typed number is not lost to a reparse each
        // frame; they are parsed back into `_s.Limits` on Save. Blank means no cap.
        string _limMem, _limPids, _limNofile, _limCpu;
        string _dnsServers;

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
            _identity = copy ? EditIdentity.ForCopy(existing?.Name) :
                existing == null ? EditIdentity.ForNew() : EditIdentity.ForEdit(existing.Name);
            _s = existing == null
                ? new SessionInfo { Name = "", Project = project ?? "" }
                : new SessionInfo
                {
                    Name = copy
                        ? _identity.CopyName(SessionHub.Instance.Sessions.Select(x => x.Name),
                            "agent")
                        : existing.Name,
                    Label = copy ? "" : existing.Label,
                    Project = existing.Project,
                    Command = existing.Command,
                    CommandPreset = existing.CommandPreset,
                    Cmd = existing.Cmd,
                    Sandbox = new List<string>(existing.Sandbox),
                    SlopworldMd = existing.SlopworldMd,
                    InstructionsBreadcrumb = existing.InstructionsBreadcrumb,
                    PersistentTmp = existing.PersistentTmp,
                    Breadcrumbs = new List<string>(existing.Breadcrumbs),
                    Network = existing.Network,
                    NetworkOverride = existing.NetworkOverride,
                    Dns = existing.Dns.Copy(),
                    DnsOverride = existing.DnsOverride?.Copy(),
                    Limits = existing.Limits,
                    Mounts = new List<MountEntry>(existing.Mounts
                        .Select(m => new MountEntry { Project = m.Project, Mode = m.Mode })),
                    Agent = existing.Agent,
                    Autostart = existing.Autostart,
                    AutoResume = existing.AutoResume,
                    BreadcrumbYolo = existing.BreadcrumbYolo,
                };


            SessionHub.Instance.Catalog.RefreshProjects();
            // Both tables are files the daemon reads, so they are asked for on every open
            // rather than once per process.
            SessionHub.Instance.Catalog.LoadPresets();
            if (string.IsNullOrEmpty(_s.CommandPreset) && string.IsNullOrEmpty(_s.Command) &&
                string.IsNullOrWhiteSpace(_s.Cmd))
                DaemonClient.Get(WireProtocol.Routes.Config,
                    j => _s.CommandPreset = j["values"]["defaults"]["agent"].AsString("claude"),
                    UiLayout.Fail);

            _limMem = LimStr(_s.Limits.MemoryMb);
            _limPids = LimStr(_s.Limits.Pids);
            _limNofile = LimStr(_s.Limits.Nofile);
            _limCpu = LimStr(_s.Limits.CpuPct);
            _dnsServers = _s.DnsOverride?.Mode == DnsMode.Servers
                ? string.Join(", ", _s.DnsOverride.Servers.ToArray())
                : "";
            AcceptOnEnter(Save);
        }

        static string LimStr(int? v) => v.HasValue ? v.Value.ToString() : "";

        // A left rail of short pages rather than one long form: the agent, its sandbox, its
        // resource limits, its breadcrumbs and the preview each get their own tab.
        public override Vector2 InitialSize => new Vector2(660f, 800f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _identity.Title("agent"));

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
                case Tab.Sandbox:
                    _sandboxListing.Draw(body, DrawSandboxFields,
                        (view, y) => DrawSandboxTrailing(view, y, body.height));
                    break;
                case Tab.ResourceLimits:
                    _limitsListing.Draw(body, DrawLimits);
                    break;
                case Tab.Breadcrumbs:
                    DrawBreadcrumbs(body);
                    break;
                case Tab.Preview:
                    SandboxPreviewPanel.Draw(body, ref _previewScroll,
                        SandboxPreviewData.ForAgent(_s));
                    break;
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.ToRect(layout.Footer));
            if (!_identity.IsNew && foot.Left("Reset private state", UiTheme.Btn.Danger))
                Find.WindowStack.Add(CatalogActions.ResetState(_identity.OriginalName));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => UiLayout.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Mounts", Tab.Mounts),
            ("Sandbox", Tab.Sandbox),
            ("Resource limits", Tab.ResourceLimits),
            ("Breadcrumbs", Tab.Breadcrumbs),
            ("Preview", Tab.Preview),
        }, ref _tab,
            tab => tab != Tab.Breadcrumbs || SessionHub.Instance.Config.ExperimentalBreadcrumbs);

        void Save()
        {
            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Project))
            {
                UiLayout.Fail("name and project are required");
                return;
            }

            if (!TryLimit(_limMem, "Memory", out var mem) ||
                !TryLimit(_limPids, "Max processes", out var pids) ||
                !TryLimit(_limNofile, "Open files", out var nofile) ||
                !TryLimit(_limCpu, "CPU", out var cpu))
                return;
            string dnsError;
            if (!DnsForm.TrySave(_s.DnsOverride, _dnsServers, out dnsError))
            {
                UiLayout.Fail("DNS: " + dnsError);
                return;
            }
            _s.Limits = new SessionLimits
            {
                MemoryMb = mem,
                Pids = pids,
                Nofile = nofile,
                CpuPct = cpu,
            };

            string from = _identity.OriginalName, to = _s.Name;
            SessionHub.Instance.Save(_s, _identity.IsNew, _identity.OriginalName,
                ok: () =>
                {
                    // The daemon took the rename, so carry the colonist over before the next
                    // reconcile sees a name it doesn't know and retires it.
                    if (!_identity.IsNew && from != to)
                    {
                        AgentColony.Current?.Rename(from, to);
                        TerminalWindow.RenameActive(from, to);
                    }
                    Close();
                },
                fail: UiLayout.Fail);
        }
    }

}
