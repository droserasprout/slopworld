using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Agent editor fields, tab bodies, and their tab-specific option lists.
    public partial class EditSessionDialog
    {
        const float MountButtonWidth = 100f;

        // The agent itself: what it is called, where it works and what it runs. Everything a
        // new agent must have to start; the other tabs only refine it.
        float DrawGeneral(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            l.Label("Name (also the colonist's name)");
            _s.Name = UiWidgets.Field(l, "agent.name", _s.Name);

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
                foreach (var p in projects.OrderBy(pr => pr.Name, System.StringComparer.OrdinalIgnoreCase))
                {
                    var row = new Rect(0f, ry, inner.width, rowH);
                    ry += rowH;

                    bool isPrimary = p.Name == _s.Project;
                    var mount = _s.Mounts.FirstOrDefault(m => m.Project == p.Name);
                    MountMode mode = isPrimary
                        ? (mount?.Mode ?? MountMode.Rw)
                        : (mount?.Mode ?? MountMode.None);

                    float labelW = row.width - MountButtonWidth - UiWidgets.GapS;
                    GUI.color = isPrimary ? UiWidgets.Lead : UiWidgets.Name;
                    UiWidgets.RowLabel(new Rect(row.x + UiWidgets.GapS, row.y, labelW, row.height),
                        isPrimary ? p.Name + "  (primary)" : p.Name);
                    GUI.color = Color.white;

                    var btnRect = new Rect(row.xMax - MountButtonWidth, row.y,
                        MountButtonWidth, row.height);
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
                    DaemonClient.Get(WireContract.Routes.Config,
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

    }
}
