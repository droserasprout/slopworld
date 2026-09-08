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

        readonly bool _isNew;
        readonly SessionInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly SmoothScroll _generalScroll = new SmoothScroll();
        readonly SmoothScroll _mountsScroll = new SmoothScroll();
        readonly SmoothScroll _sandboxScroll = new SmoothScroll();
        readonly SmoothScroll _limitsScroll = new SmoothScroll();
        const float PresetsH = 240f;
        Tab _tab;

        // Last frame's content height per scrolling tab, so each can grow a scrollbar when its
        // fields and fixed lists do not fit - the body width the rail leaves varies with UI scale.
        float _generalH = 320f;
        float _sandboxH = 480f;
        float _limitsH = 320f;

        // Limits are edited as raw strings so a half-typed number is not lost to a reparse each
        // frame; they are parsed back into `_s.Limits` on Save. Blank means no cap.
        string _limMem, _limPids, _limNofile, _limCpu;
        string _dnsServers;

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
                    Name = copy
                        ? UiWidgets.FreeName(existing.Name,
                            SessionHub.Instance.Sessions.Select(x => x.Name), "agent")
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


            SessionHub.Instance.RefreshProjects();
            // Both tables are files the daemon reads, so they are asked for on every open
            // rather than once per process.
            SessionHub.Instance.LoadPresets();
            if (string.IsNullOrEmpty(_s.CommandPreset) && string.IsNullOrEmpty(_s.Command) &&
                string.IsNullOrWhiteSpace(_s.Cmd))
                DaemonClient.Get(WireContract.Routes.Config,
                    j => _s.CommandPreset = j["values"]["defaults"]["agent"].AsString("claude"),
                    UiWidgets.Fail);

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
            UiWidgets.Title(TitleRect(rect), _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New agent" : $"Edit '{_origName}'");

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
                case Tab.Mounts:
                    DrawMounts(body);
                    break;
                case Tab.Sandbox:
                {
                    var view = new Rect(0f, 0f, body.width - UiWidgets.ScrollbarW,
                        Mathf.Max(_sandboxH, body.height));
                    using (_sandboxScroll.Scope(body, view))
                        _sandboxH = DrawSandbox(view);
                    break;
                }
                case Tab.ResourceLimits:
                {
                    var view = new Rect(0f, 0f, body.width - UiWidgets.ScrollbarW,
                        Mathf.Max(_limitsH, body.height));
                    using (_limitsScroll.Scope(body, view))
                        _limitsH = DrawLimits(view);
                    break;
                }
                case Tab.Breadcrumbs:
                    DrawBreadcrumbs(body);
                    break;
                case Tab.Preview:
                    SandboxPreviewPanel.Draw(body, ref _previewScroll,
                        SandboxPreviewData.ForAgent(_s));
                    break;
            }

            var foot = new UiWidgets.Bar(UiWidgets.FooterBar(rect));
            if (!_isNew && foot.Left("Reset private state", UiWidgets.Btn.Danger))
                Find.WindowStack.Add(CatalogActions.ResetState(_origName));
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", UiWidgets.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => UiWidgets.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Mounts", Tab.Mounts),
            ("Sandbox", Tab.Sandbox),
            ("Resource limits", Tab.ResourceLimits),
            ("Breadcrumbs", Tab.Breadcrumbs),
            ("Preview", Tab.Preview),
        }, ref _tab);

        void Save()
        {
            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Project))
            {
                UiWidgets.Fail("name and project are required");
                return;
            }

            if (!TryLimit(_limMem, "Memory", out var mem) ||
                !TryLimit(_limPids, "Max processes", out var pids) ||
                !TryLimit(_limNofile, "Open files", out var nofile) ||
                !TryLimit(_limCpu, "CPU", out var cpu))
                return;
            List<string> dnsServers = null;
            string dnsError;
            if (_s.DnsOverride?.Mode == DnsMode.Servers &&
                !DnsConfig.TryParseServers(_dnsServers, out dnsServers, out dnsError))
            {
                UiWidgets.Fail("DNS: " + dnsError);
                return;
            }
            if (_s.DnsOverride?.Mode == DnsMode.Servers)
                _s.DnsOverride.Servers = dnsServers;
            _s.Limits = new SessionLimits
            {
                MemoryMb = mem,
                Pids = pids,
                Nofile = nofile,
                CpuPct = cpu,
            };

            string from = _origName, to = _s.Name;
            SessionHub.Instance.Save(_s, _isNew, _origName,
                ok: () =>
                {
                    // The daemon took the rename, so carry the colonist over before the next
                    // reconcile sees a name it doesn't know and retires it.
                    if (!_isNew && from != to)
                    {
                        AgentColony.Current?.Rename(from, to);
                        TerminalWindow.RenameActive(from, to);
                    }
                    Close();
                },
                fail: UiWidgets.Fail);
        }
    }

}
