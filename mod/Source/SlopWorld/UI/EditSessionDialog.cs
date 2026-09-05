using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What is left here is the two things about the agent: its name and what it runs.
    // Where it works moved to the project, which is why picking one is mandatory and
    // there is no directory field; network reach has an agent-level override here.
    public class EditSessionDialog : UiWindow
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
                DaemonClient.Get("/api/config",
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
            UiWidgets.Title(rect, _copiedFrom != null
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
            {
                string name = _origName;
                Find.WindowStack.Add(ConfirmDialog.Create(
                    $"Reset private state for '{name}'? This stops the agent and gives its tools " +
                    "a fresh state on next start. The old state stays recoverable for 14 days.",
                    () => SessionHub.Instance.ResetState(name, UiWidgets.Fail), destructive: true));
            }
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

        // The agent itself: what it is called, where it works and what it runs. Everything a
        // new agent must have to start; the other tabs only refine it.
        float DrawGeneral(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            l.Label("Name (also the colonist's name)");
            _s.Name = UiWidgets.Field(l, "agent.name", _s.Name);

            l.Gap(UiWidgets.GapS);
            var projectOptions = SessionHub.Instance.Projects
                .Select(p => new SelectorOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name)).ToList();
            projectOptions.Add(new SelectorOption("New project...",
                () => Find.WindowStack.Add(new EditProjectDialog(null))));
            UiWidgets.Select(l, "Project (the directory and sandbox it works in)",
                string.IsNullOrEmpty(_s.Project) ? "Pick a project..." : _s.Project,
                projectOptions, out _);

            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = UiWidgets.Dim;
            l.Label(project != null
                ? $"{project.Dir}  ({ProjectsView.Summary(project)})"
                : SessionHub.Instance.Projects.Count == 0
                    ? "No projects yet - make one in the Projects window first."
                    : "");
            GUI.color = Color.white;

            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = SessionHub.Instance.Command(commandName);

            l.Gap(UiWidgets.GapS);
            UiWidgets.Select(l, "Command", CommandLabel(preset), CommandOptions(), out _);

            // Editable whichever it is: a preset says what an agent is, and this box says
            // what this one runs, which is the same field either way.
            _s.Cmd = UiWidgets.Field(l, "agent.cmd", _s.Cmd ?? "");
            GUI.color = UiWidgets.Dim;
            l.Label(CommandNote(preset));
            GUI.color = Color.white;

            l.Gap(UiWidgets.GapS);
            _s.Autostart = UiWidgets.Checkbox(l, "Start with the daemon", _s.Autostart);
            _s.AutoResume = UiWidgets.Checkbox(l, "Auto-resume last conversation", _s.AutoResume,
                "After startup settles, send /resume and choose the latest conversation.");
            _s.SlopworldMd = UiWidgets.Checkbox(l, "Mount SLOPWORLD.md", _s.SlopworldMd,
                "Mount generated runtime context read-only at the Instructions mount path, exclude the source file from Git, and optionally tell the agent to read it on its first prompt.");
            _s.PersistentTmp = UiWidgets.Checkbox(l, "Persistent /tmp", _s.PersistentTmp,
                "Keep this agent's /tmp across restarts in its private state. Resetting private state gives it a fresh /tmp.");

            float used = l.CurHeight;
            l.End();
            return used + UiWidgets.GapS;
        }

        void DrawMounts(Rect rect)
        {
            var projects = SessionHub.Instance.Projects;

            GUI.color = UiWidgets.Dim;
            var hint = new Rect(rect.x, rect.y, rect.width, UiWidgets.LineH);
            UiWidgets.RowLabel(hint,
                "Mount other project directories into /mnt/<name>. The agent's own project is always mounted.");
            GUI.color = Color.white;

            float y = hint.yMax + UiWidgets.GapS;

            if (projects.Count == 0)
            {
                GUI.color = UiWidgets.Dim;
                UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, UiWidgets.LineH),
                    "No projects defined.");
                GUI.color = Color.white;
                return;
            }

            float rowH = UiWidgets.RowH;
            float listH = projects.Count * rowH;
            var listRect = new Rect(rect.x, y, rect.width, Mathf.Min(listH + 8f, rect.yMax - y));
            Slab.Box(listRect, UiWidgets.Well, UiWidgets.Edge);
            var pad = listRect.ContractedBy(UiWidgets.ListInset);
            var inner = new Rect(0f, 0f, pad.width - UiWidgets.ScrollbarW, listH);

            using (_mountsScroll.Scope(pad, inner))
            {
                float ry = 0f;
                const float btnW = 100f;
                foreach (var p in projects.OrderBy(pr => pr.Name, System.StringComparer.OrdinalIgnoreCase))
                {
                    var row = new Rect(0f, ry, inner.width, rowH);
                    ry += rowH;

                    bool isPrimary = p.Name == _s.Project;
                    var mount = _s.Mounts.FirstOrDefault(m => m.Project == p.Name);
                    MountMode mode = isPrimary
                        ? (mount?.Mode ?? MountMode.Rw)
                        : (mount?.Mode ?? MountMode.None);

                    float labelW = row.width - btnW - UiWidgets.GapS;
                    GUI.color = isPrimary ? UiWidgets.Lead : UiWidgets.Name;
                    UiWidgets.RowLabel(new Rect(row.x + UiWidgets.GapS, row.y, labelW, row.height),
                        isPrimary ? p.Name + "  (primary)" : p.Name);
                    GUI.color = Color.white;

                    var btnRect = new Rect(row.xMax - btnW, row.y, btnW, row.height);
                    if (UiWidgets.Button(btnRect, MountEntry.ModeLabel(mode),
                            isPrimary ? UiWidgets.Btn.Default : UiWidgets.Btn.Ghost))
                        PickMountMode(p.Name, isPrimary);
                }
            }
        }

        void PickMountMode(string project, bool isPrimary)
        {
            var options = new List<FloatMenuOption>();
            if (!isPrimary)
            {
                options.Add(new FloatMenuOption(MountEntry.ModeLabel(MountMode.None),
                    () => SetMount(project, MountMode.None)));
            }
            options.Add(new FloatMenuOption(MountEntry.ModeLabel(MountMode.Ro),
                () => SetMount(project, MountMode.Ro)));
            options.Add(new FloatMenuOption(MountEntry.ModeLabel(MountMode.Rw),
                () => SetMount(project, MountMode.Rw)));
            Find.WindowStack.Add(new UiMenu(options));
        }

        void SetMount(string project, MountMode mode)
        {
            _s.Mounts.RemoveAll(m => m.Project == project);
            if (mode != MountMode.None)
                _s.Mounts.Add(new MountEntry { Project = project, Mode = mode });
        }

        // Reach and the extra sandbox presets this agent adds on top of its command's and
        // project's.
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

            float y = rect.y + used + UiWidgets.GapL;
            y = DrawExtraPresets(rect, y, project, preset);

            return y - rect.y + UiWidgets.GapS;
        }

        void DrawNetworkFields(Listing_Standard l, ProjectInfo project)
        {
            var projectNetwork = project?.Network ?? NetworkMode.Private;
            var inheritedDns = project?.Dns ?? _s.Dns;
            string networkLabel = _s.NetworkOverride.HasValue
                ? NetworkModeText.Label(_s.NetworkOverride.Value)
                : "Inherit project (" + NetworkModeText.ShortLabel(projectNetwork) + ")";
            var networkOptions = new List<SelectorOption>
            {
                new SelectorOption("Inherit project (" + NetworkModeText.ShortLabel(projectNetwork) + ")",
                    () => _s.NetworkOverride = null),
            };
            networkOptions.AddRange(new[] { NetworkMode.None, NetworkMode.Private, NetworkMode.Host }
                .Select(mode => new SelectorOption(NetworkModeText.Label(mode),
                    () => _s.NetworkOverride = mode)));
            UiWidgets.Select(l, "Network", networkLabel, networkOptions, out _);
            GUI.color = UiWidgets.Dim;
            l.Label("The project sets the default; this agent can use any network mode.");
            GUI.color = Color.white;

            l.Gap(UiWidgets.GapS);
            string dnsLabel = _s.DnsOverride == null
                ? "Inherit project (" + inheritedDns.Label + ")"
                : _s.DnsOverride.Label;
            UiWidgets.Select(l, "DNS", dnsLabel, new[]
            {
                new SelectorOption("Inherit project (" + inheritedDns.Label + ")",
                    () => _s.DnsOverride = null),
                new SelectorOption("System resolver", () => _s.DnsOverride = DnsConfig.Resolved()),
                new SelectorOption("Custom DNS servers", () =>
                {
                    if (_s.DnsOverride?.Mode != DnsMode.Servers)
                        _s.DnsOverride = DnsConfig.Custom();
                }),
            }, out _);
            if (_s.DnsOverride?.Mode == DnsMode.Servers)
            {
                _dnsServers = UiWidgets.Field(l, "agent.dns", _dnsServers ?? "");
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
        }

        float DrawExtraPresets(Rect rect, float y, ProjectInfo project, CommandInfo preset)
        {
            UiWidgets.SectionHeading(new Rect(rect.x, y, rect.width, UiWidgets.RowH),
                "Extra sandbox presets");
            y += UiWidgets.RowH + UiWidgets.GapXS;

            var inheritedPresets = new List<string>();
            if (preset != null) inheritedPresets.AddRange(preset.Sandbox);
            if (project != null) inheritedPresets.AddRange(project.Sandbox);
            PresetList.Draw(new Rect(rect.x, y, rect.width, PresetsH), _s.Sandbox,
                _presetScroll, inheritedPresets);
            return y + PresetsH + UiWidgets.GapL;
        }

        // Per-agent resource caps the daemon enforces with a systemd scope. Edited as strings;
        // parsed on Save. Returns the height drawn so its tab can size its scroll view.
        float DrawLimits(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            if (!SessionHub.Instance.Capabilities.PerSessionLimits)
            {
                UiWidgets.Note(l,
                    "slopcar has one outer CPU, memory and process budget. Per-agent limits " +
                    "need delegated cgroups and are unavailable in this runtime.");
                float unavailable = l.CurHeight;
                l.End();
                return unavailable;
            }

            GUI.color = UiWidgets.Dim;
            l.Label("Blank means no cap. An unset field inherits the project, then the host.");
            GUI.color = Color.white;

            l.Label("Memory (MiB)");
            _limMem = UiWidgets.Field(l, "agent.lim.mem", _limMem ?? "");
            l.Label("Max processes and threads");
            _limPids = UiWidgets.Field(l, "agent.lim.pids", _limPids ?? "");
            l.Label("Open files per process");
            _limNofile = UiWidgets.Field(l, "agent.lim.nofile", _limNofile ?? "");
            l.Label("CPU (% of one core)");
            _limCpu = UiWidgets.Field(l, "agent.lim.cpu", _limCpu ?? "");

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
            _s.BreadcrumbYolo = UiWidgets.Checkbox(
                new Rect(rect.x, rect.y, rect.width, UiWidgets.RowH),
                "YOLO breadcrumbs", _s.BreadcrumbYolo,
                "Hijack the first Enter after startup and paste every enabled breadcrumb before it.");
            float y = rect.y + UiWidgets.RowH + UiWidgets.GapXS;
            var config = SessionHub.Instance.Config;
            var projectBreadcrumbs = SessionHub.Instance.Project(_s.Project)?.Breadcrumbs;
            BreadcrumbList.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)),
                _s.Breadcrumbs, _breadcrumbScroll, projectBreadcrumbs,
                config.InstructionsBreadcrumb,
                _s.InstructionsBreadcrumb,
                onInstructionsChanged: on => _s.InstructionsBreadcrumb = on);
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

        IEnumerable<SelectorOption> CommandOptions()
        {
            var options = new List<SelectorOption>
            {
                new SelectorOption("Default", () =>
                {
                    _s.Command = "";
                    _s.Cmd = "";
                    DaemonClient.Get("/api/config",
                        j => _s.CommandPreset = j["values"]["defaults"]["agent"].AsString("claude"),
                        UiWidgets.Fail);
                }),
            };

            // Named by the daemon rather than listed here, so a command file dropped in its
            // preset directory is an entry in this menu and nothing to rebuild.
            foreach (var c in SessionHub.Instance.Commands)
            {
                var pick = c;
                options.Add(new SelectorOption($"{pick.Name}  -  {pick.Cmd}",
                    () => _s.Command = pick.Name));
            }

            options.Add(new SelectorOption("Command line...", () => _s.Command = ""));
            return options;
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
            UiWidgets.Fail($"{label} must be a whole number of at least 1, or blank for no cap");
            return false;
        }

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

    // The game is inside Wine and cannot see the host filesystem, so the daemon does
    // the listing.
    public class BrowseDialog : UiWindow
    {
        readonly System.Action<string> _pick;
        string _path;
        string _parent;
        string[] _dirs = new string[0];
        readonly SmoothScroll _scroll = new SmoothScroll();

        // One row and the clearance under it, so the list has a pitch rather than two figures
        // four pixels apart written at three call sites.
        static float Pitch => UiWidgets.RowH + UiWidgets.GapXS;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
            AcceptOnEnter(() =>
            {
                _pick?.Invoke(_path);
                Close();
            });
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        void Load(string path)
        {
            DaemonClient.Get($"/api/browse?path={System.Uri.EscapeDataString(path)}",
                j =>
                {
                    _path = j["path"].AsString();
                    _parent = j["parent"].IsNull ? null : j["parent"].AsString();
                    _dirs = j["dirs"].Items.Select(d => d.AsString()).ToArray();
                },
                UiWidgets.Fail);
        }

        protected override void DoBody(Rect rect)
        {
            UiWidgets.PageCaption(rect, _path ?? "loading...");

            float top = rect.y + UiWidgets.RowH + UiWidgets.GapXS;
            var list = new Rect(rect.x, top, rect.width,
                rect.yMax - UiWidgets.BtnH - UiWidgets.GapS - top);
            int count = _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - UiWidgets.ScrollbarW, count * Pitch);

            using (_scroll.Scope(list, view))
            {
                float y = 0f;
                if (_parent != null)
                {
                    // Ghost the whole way down: forty directories in forty raised slabs is a wall
                    // of buttons, and what this is is a list that answers to a click.
                    if (UiWidgets.Button(new Rect(0f, y, view.width, UiWidgets.RowH), "..",
                            UiWidgets.Btn.Ghost))
                        Load(_parent);
                    y += Pitch;
                }

                foreach (var d in _dirs)
                {
                    if (UiWidgets.Button(new Rect(0f, y, view.width, UiWidgets.RowH), d,
                            UiWidgets.Btn.Ghost))
                    {
                        Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                        break; // _dirs is about to be replaced under us
                    }
                    y += Pitch;
                }
            }

            if (UiWidgets.Button(UiWidgets.FooterBar(rect),
                    "Use this directory", UiWidgets.Btn.Primary))
            {
                _pick(_path);
                Close();
            }
        }
    }
}
