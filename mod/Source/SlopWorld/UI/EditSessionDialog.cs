using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What is left here is the two things about the agent: its name and what it runs.
    // Where it works and what it can reach moved to the project, which is why picking
    // one is mandatory and there is no directory field.
    public class EditSessionDialog : SlopWindow
    {
        enum Tab { General, Sandbox, Breadcrumbs, Preview }

        readonly bool _isNew;
        readonly SessionInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _breadcrumbScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly SmoothScroll _generalScroll = new SmoothScroll();
        readonly SmoothScroll _sandboxScroll = new SmoothScroll();
        const float PresetsH = 132f;
        Tab _tab;

        // Last frame's content height per scrolling tab, so each can grow a scrollbar when its
        // fields and fixed lists do not fit - the body width the rail leaves varies with UI scale.
        float _generalH = 320f;
        float _sandboxH = 480f;

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
                        ? SlopWidgets.FreeName(existing.Name,
                            SessionHub.Instance.Sessions.Select(x => x.Name), "agent")
                        : existing.Name,
                    Label = copy ? "" : existing.Label,
                    Project = existing.Project,
                    Command = existing.Command,
                    CommandPreset = existing.CommandPreset,
                    Cmd = existing.Cmd,
                    Sandbox = new List<string>(existing.Sandbox),
                    Breadcrumbs = new List<string>(existing.Breadcrumbs),
                    Network = existing.Network,
                    NetworkOverride = existing.NetworkOverride,
                    Dns = existing.Dns.Copy(),
                    DnsOverride = existing.DnsOverride?.Copy(),
                    Limits = existing.Limits,
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
                SlopClient.Get("/api/config",
                    j => _s.CommandPreset = j["values"]["defaults"]["agent"].AsString("claude"),
                    SlopWidgets.Fail);

            _limMem = LimStr(_s.Limits.MemoryMb);
            _limPids = LimStr(_s.Limits.Pids);
            _limNofile = LimStr(_s.Limits.Nofile);
            _limCpu = LimStr(_s.Limits.CpuPct);
            _dnsServers = _s.DnsOverride?.Mode == DnsMode.Servers
                ? string.Join(", ", _s.DnsOverride.Servers.ToArray())
                : "";
        }

        static string LimStr(int? v) => v.HasValue ? v.Value.ToString() : "";

        // A left rail of short pages rather than one long form: the agent, its sandbox, its
        // breadcrumbs and the preview each get their own tab so none has to hold the others.
        public override Vector2 InitialSize => new Vector2(660f, 800f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New agent" : $"Edit '{_origName}'");

            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float bottom = rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS;

            const float railW = 132f;
            DrawRail(new Rect(rect.x, top, railW, bottom - top));
            var body = new Rect(rect.x + railW + SlopWidgets.GapM, top,
                rect.width - railW - SlopWidgets.GapM, bottom - top);

            switch (_tab)
            {
                case Tab.General:
                {
                    var view = new Rect(0f, 0f, body.width - SlopWidgets.ScrollbarW,
                        Mathf.Max(_generalH, body.height));
                    _generalScroll.Begin(body, view);
                    _generalH = DrawGeneral(view);
                    _generalScroll.End();
                    break;
                }
                case Tab.Sandbox:
                {
                    var view = new Rect(0f, 0f, body.width - SlopWidgets.ScrollbarW,
                        Mathf.Max(_sandboxH, body.height));
                    _sandboxScroll.Begin(body, view);
                    _sandboxH = DrawSandbox(view);
                    _sandboxScroll.End();
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

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (!_isNew && foot.Left("Reset private state", SlopWidgets.Btn.Danger))
            {
                string name = _origName;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Reset private state for '{name}'? This stops the agent and gives its tools " +
                    "a fresh state on next start. The old state stays recoverable for 14 days.",
                    () => SessionHub.Instance.ResetState(name, SlopWidgets.Fail), destructive: true));
            }
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save();
        }

        void DrawRail(Rect r) => SlopWidgets.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Sandbox", Tab.Sandbox),
            ("Breadcrumbs", Tab.Breadcrumbs),
            ("Preview", Tab.Preview),
        }, ref _tab);

        // The agent itself: what it is called, where it works and what it runs. Everything a
        // new agent must have to start; the other tabs only refine it.
        float DrawGeneral(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            l.Label("Name (also the colonist's name)");
            _s.Name = SlopWidgets.Field(l, "agent.name", _s.Name);

            l.Gap(SlopWidgets.GapS);
            l.Label("Project (the directory and sandbox it works in)");
            if (SlopWidgets.Button(l,
                    string.IsNullOrEmpty(_s.Project) ? "Pick a project..." : _s.Project))
                PickProject();

            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = SlopWidgets.Dim;
            l.Label(project != null
                ? $"{project.Dir}  ({ProjectsView.Summary(project)})"
                : SessionHub.Instance.Projects.Count == 0
                    ? "No projects yet - make one in the Projects window first."
                    : "");
            GUI.color = Color.white;

            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = SessionHub.Instance.Command(commandName);

            l.Gap(SlopWidgets.GapS);
            l.Label("Command");
            if (SlopWidgets.Button(l, CommandLabel(preset)))
                PickCommand();

            // Editable whichever it is: a preset says what an agent is, and this box says
            // what this one runs, which is the same field either way.
            _s.Cmd = SlopWidgets.Field(l, "agent.cmd", _s.Cmd ?? "");
            GUI.color = SlopWidgets.Dim;
            l.Label(CommandNote(preset));
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapS);
            _s.Autostart = SlopWidgets.Checkbox(l, "Start with the daemon", _s.Autostart);
            _s.AutoResume = SlopWidgets.Checkbox(l, "Auto-resume last conversation", _s.AutoResume,
                "After startup settles, send /resume and choose the latest conversation.");

            float used = l.CurHeight;
            l.End();
            return used + SlopWidgets.GapS;
        }

        // Reach and how much of it: the network ceiling this agent may reduce, the extra
        // presets it adds on top of its command's and project's, and the resource caps - all
        // the confinement knobs on one page.
        float DrawSandbox(Rect rect)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = SessionHub.Instance.Command(commandName);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);
            DrawNetworkFields(l, project);
            float used = l.CurHeight;
            l.End();

            float y = rect.y + used + SlopWidgets.GapL;
            y = DrawExtraPresets(rect, y, project, preset);
            y += DrawLimits(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)));

            return y - rect.y + SlopWidgets.GapS;
        }

        void DrawNetworkFields(Listing_Standard l, ProjectInfo project)
        {
            l.Label("Network");
            var ceiling = project?.Network ?? NetworkMode.Private;
            var inheritedDns = project?.Dns ?? _s.Dns;
            string networkLabel = _s.NetworkOverride.HasValue
                ? NetworkModeText.Label(_s.NetworkOverride.Value)
                : "Inherit project (" + NetworkModeText.ShortLabel(ceiling) + ")";
            if (SlopWidgets.Button(l, networkLabel))
                PickNetwork(ceiling);
            GUI.color = SlopWidgets.Dim;
            l.Label("The project is the ceiling; this agent can only reduce its network reach.");
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapS);
            l.Label("DNS");
            string dnsLabel = _s.DnsOverride == null
                ? "Inherit project (" + inheritedDns.Label + ")"
                : _s.DnsOverride.Label;
            if (SlopWidgets.Button(l, dnsLabel)) PickDns();
            if (_s.DnsOverride?.Mode == DnsMode.Servers)
            {
                _dnsServers = SlopWidgets.Field(l, "agent.dns", _dnsServers ?? "");
                GUI.color = SlopWidgets.Dim;
                l.Label("Comma-separated IPv4 addresses; maximum two. Changes apply on restart.");
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = SlopWidgets.Dim;
                l.Label("System resolver follows the daemon's current resolv.conf.");
                GUI.color = Color.white;
            }
        }

        float DrawExtraPresets(Rect rect, float y, ProjectInfo project, CommandInfo preset)
        {
            SlopWidgets.SectionHeading(new Rect(rect.x, y, rect.width, SlopWidgets.RowH),
                "Extra sandbox presets");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            var inheritedPresets = new List<string>();
            if (preset != null) inheritedPresets.AddRange(preset.Sandbox);
            if (project != null) inheritedPresets.AddRange(project.Sandbox);
            PresetList.Draw(new Rect(rect.x, y, rect.width, PresetsH), _s.Sandbox,
                _presetScroll, inheritedPresets);
            y += PresetsH + SlopWidgets.GapL;
            SlopWidgets.SectionHeading(new Rect(rect.x, y, rect.width, SlopWidgets.RowH),
                "Resource limits");
            return y + SlopWidgets.RowH + SlopWidgets.GapXS;
        }

        // Per-agent resource caps the daemon enforces with a systemd scope. Edited as strings;
        // parsed on Save. Returns the height drawn so `DrawSandbox` can size its scroll view.
        float DrawLimits(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            if (!SessionHub.Instance.Capabilities.PerSessionLimits)
            {
                SlopWidgets.Note(l,
                    "slopcar has one outer CPU, memory and process budget. Per-agent limits " +
                    "need delegated cgroups and are unavailable in this runtime.");
                float unavailable = l.CurHeight;
                l.End();
                return unavailable;
            }

            GUI.color = SlopWidgets.Dim;
            l.Label("Blank means no cap. An unset field inherits the project, then the host.");
            GUI.color = Color.white;

            l.Label("Memory (MiB)");
            _limMem = SlopWidgets.Field(l, "agent.lim.mem", _limMem ?? "");
            l.Label("Max processes and threads");
            _limPids = SlopWidgets.Field(l, "agent.lim.pids", _limPids ?? "");
            l.Label("Open files per process");
            _limNofile = SlopWidgets.Field(l, "agent.lim.nofile", _limNofile ?? "");
            l.Label("CPU (% of one core)");
            _limCpu = SlopWidgets.Field(l, "agent.lim.cpu", _limCpu ?? "");

            // Mirror the buffers into the model as they are typed, leniently, so the Preview tab
            // reflects them; Save reparses strictly and reports a typo rather than dropping it.
            _s.Limits = new SessionLimits
            {
                MemoryMb = LimVal(_limMem),
                Pids = LimVal(_limPids),
                Nofile = LimVal(_limNofile),
                CpuPct = LimVal(_limCpu),
            };

            float used = l.CurHeight;
            l.End();
            return used;
        }

        // Blank or not a positive whole number reads as no cap; the strict parse on Save is
        // what turns a typo into a message instead of silence.
        static int? LimVal(string text) =>
            int.TryParse((text ?? "").Trim(), out int n) && n >= 1 ? n : (int?)null;

        void DrawBreadcrumbs(Rect rect)
        {
            _s.BreadcrumbYolo = SlopWidgets.Checkbox(
                new Rect(rect.x, rect.y, rect.width, SlopWidgets.RowH),
                "YOLO breadcrumbs", _s.BreadcrumbYolo,
                "Hijack the first Enter after startup and paste every enabled breadcrumb before it.");
            float y = rect.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            var projectBreadcrumbs = SessionHub.Instance.Project(_s.Project)?.Breadcrumbs;
            BreadcrumbList.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)),
                _s.Breadcrumbs, _breadcrumbScroll, projectBreadcrumbs);
        }

        void PickProject()
        {
            var hub = SessionHub.Instance;
            var options = hub.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () =>
                    {
                        _s.Project = p.Name;
                        if (_s.NetworkOverride.HasValue &&
                            !NetworkModeText.Allowed(_s.NetworkOverride.Value, p.Network))
                            _s.NetworkOverride = null;
                    }))
                .ToList();

            options.Add(new FloatMenuOption("New project...",
                () => Find.WindowStack.Add(new EditProjectDialog(null))));

            Find.WindowStack.Add(new SlopMenu(options));
        }

        void PickNetwork(NetworkMode ceiling)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Inherit project (" + NetworkModeText.ShortLabel(ceiling) + ")",
                    () => _s.NetworkOverride = null),
            };

            foreach (NetworkMode mode in new[]
            {
                NetworkMode.None, NetworkMode.Private, NetworkMode.Host,
            })
            {
                if (!NetworkModeText.Allowed(mode, ceiling)) continue;
                var picked = mode;
                options.Add(new FloatMenuOption(NetworkModeText.Label(picked),
                    () => _s.NetworkOverride = picked));
            }

            Find.WindowStack.Add(new SlopMenu(options));
        }

        void PickDns()
        {
            var inheritedDns = SessionHub.Instance.Project(_s.Project)?.Dns ?? _s.Dns;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Inherit project (" + inheritedDns.Label + ")",
                    () => _s.DnsOverride = null),
                new FloatMenuOption("System resolver",
                    () => _s.DnsOverride = DnsConfig.Resolved()),
                new FloatMenuOption("Custom DNS servers",
                    () =>
                    {
                        if (_s.DnsOverride?.Mode != DnsMode.Servers)
                            _s.DnsOverride = DnsConfig.Custom();
                    }),
            };
            Find.WindowStack.Add(new SlopMenu(options));
        }

        // The three states this pair of fields can be in: a command preset, a command line
        // of its own, or neither, which is whatever the daemon's `[defaults] agent` names.
        string CommandLabel(CommandInfo preset)
        {
            if (preset != null) return preset.Name;
            if (!string.IsNullOrEmpty(_s.Command)) return _s.Command + " (unknown here)";
            return string.IsNullOrEmpty((_s.Cmd ?? "").Trim()) ? "Default" : "Command line";
        }

        string CommandNote(CommandInfo preset)
        {
            if (preset != null)
            {
                string sandbox = preset.Sandbox.Count > 0
                    ? "  Sandbox: " + string.Join(", ", preset.Sandbox.ToArray()) + "."
                    : "";
                return $"Blank runs '{preset.Cmd}'.{sandbox}";
            }
            if (!string.IsNullOrEmpty((_s.Cmd ?? "").Trim()))
                return "A command line of its own, so no agent's state directory comes with it.";
            return !string.IsNullOrEmpty(_s.Agent)
                ? $"Blank runs the daemon's default, which is '{_s.Agent}'."
                : "Blank runs the daemon's default agent.";
        }

        void PickCommand()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Default", () =>
                {
                    _s.Command = "";
                    _s.Cmd = "";
                    SlopClient.Get("/api/config",
                        j => _s.CommandPreset = j["values"]["defaults"]["agent"].AsString("claude"),
                        SlopWidgets.Fail);
                }),
            };

            // Named by the daemon rather than listed here, so a command file dropped in its
            // preset directory is an entry in this menu and nothing to rebuild.
            foreach (var c in SessionHub.Instance.Commands)
            {
                var pick = c;
                options.Add(new FloatMenuOption($"{pick.Name}  -  {pick.Cmd}",
                    () => _s.Command = pick.Name));
            }

            options.Add(new FloatMenuOption("Command line...", () => _s.Command = ""));
            Find.WindowStack.Add(new SlopMenu(options));
        }

        // Blank clears a cap; otherwise it must be a whole number of at least 1. A typo is
        // refused rather than silently dropped, so a cap the user typed is never lost on Save.
        static bool TryLimit(string text, string label, out int? value)
        {
            value = null;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return true;
            if (int.TryParse(t, out int n) && n >= 1)
            {
                value = n;
                return true;
            }
            SlopWidgets.Fail($"{label} must be a whole number of at least 1, or blank for no cap");
            return false;
        }

        void Save()
        {
            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Project))
            {
                SlopWidgets.Fail("name and project are required");
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
                SlopWidgets.Fail("DNS: " + dnsError);
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
                fail: SlopWidgets.Fail);
        }
    }

    // The game is inside Wine and cannot see the host filesystem, so the daemon does
    // the listing.
    public class BrowseDialog : SlopWindow
    {
        readonly System.Action<string> _pick;
        string _path;
        string _parent;
        string[] _dirs = new string[0];
        readonly SmoothScroll _scroll = new SmoothScroll();

        // One row and the clearance under it, so the list has a pitch rather than two figures
        // four pixels apart written at three call sites.
        static float Pitch => SlopWidgets.RowH + SlopWidgets.GapXS;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
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
                SlopWidgets.Fail);
        }

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.PageCaption(rect, _path ?? "loading...");

            float top = rect.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            var list = new Rect(rect.x, top, rect.width,
                rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS - top);
            int count = _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - SlopWidgets.ScrollbarW, count * Pitch);

            _scroll.Begin(list, view);
            float y = 0f;

            if (_parent != null)
            {
                // Ghost the whole way down: forty directories in forty raised slabs is a wall
                // of buttons, and what this is is a list that answers to a click.
                if (SlopWidgets.Button(new Rect(0f, y, view.width, SlopWidgets.RowH), "..",
                        SlopWidgets.Btn.Ghost))
                    Load(_parent);
                y += Pitch;
            }

            foreach (var d in _dirs)
            {
                if (SlopWidgets.Button(new Rect(0f, y, view.width, SlopWidgets.RowH), d,
                        SlopWidgets.Btn.Ghost))
                {
                    Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                    break; // _dirs is about to be replaced under us
                }
                y += Pitch;
            }
            _scroll.End();

            if (SlopWidgets.Button(SlopWidgets.FooterBar(rect),
                    "Use this directory", SlopWidgets.Btn.Primary))
            {
                _pick(_path);
                Close();
            }
        }
    }
}
