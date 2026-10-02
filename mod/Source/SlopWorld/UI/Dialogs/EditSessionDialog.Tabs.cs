using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Agent editor fields, tab bodies, and their tab-specific option lists.
    public partial class EditSessionDialog
    {
        List<Wire.Worktree> _worktreeChoices = new List<Wire.Worktree>();
        string _worktreeProject;
        string _worktreeError;

        void RefreshWorktreeChoices()
        {
            string project = _s.Project;
            _worktreeProject = project;
            _worktreeChoices.Clear();
            _worktreeError = null;
            if (string.IsNullOrEmpty(project)) return;
            DaemonClient.Get<Wire.WorktreesReply>(WireProtocol.Routes.Worktrees + "?project=" + System.Uri.EscapeDataString(project),
                reply => { if (_s.Project == project) _worktreeChoices = reply.Worktrees.ToList(); },
                error => { if (_s.Project == project) _worktreeError = error; }, TaskInfo.Host, 60000);
        }
        // This tab sets the agent name, workspace, and command required for startup.
        // The other tabs provide additional settings.
        void DrawGeneral(Listing_Standard l)
        {
            if (_identity.IsNew && !EditingTemplate && !string.IsNullOrEmpty(_templateName))
                UiLayout.Note(l, "Template: " + _templateName +
                    ". This agent uses settings copied from the template. Later template edits do not change this agent.");

            l.Label(EditingTemplate ? "Template name" : "Name (also the colonist's name)");
            _s.Name = UiControls.Field(l, "agent.name", _s.Name);

            if (EditingTemplate)
            {
                l.Label("Description");
                _templateDraft.Description = UiControls.Area(l, 48f, "template.description", _templateDraft.Description);
                UiLayout.Note(l, "New agents copy these settings once. Select a project to provide the working directory and shared mounts.");
            }
            else
            {
                var projectOptions = SessionHub.Instance.Projects
                    .Select(p => new SelectorOption($"{p.Name}  -  {p.Dir}",
                        () => { _s.Project = p.Name; _s.Worktree = ""; RefreshWorktreeChoices(); })).ToList();
                projectOptions.Add(new SelectorOption("Create new project",
                    () => Find.WindowStack.Add(new EditProjectDialog(null))));
                UiControls.Select(l, "Project (the directory and shared mounts it uses)",
                    string.IsNullOrEmpty(_s.Project) ? "Select a project" : _s.Project,
                    projectOptions, out _);

                if (_worktreeProject != _s.Project) RefreshWorktreeChoices();
                var worktreeOptions = _worktreeChoices.Where(w => w.Phase == "ready").Select(w => new SelectorOption(
                    w.Name, () => _s.Worktree = w.Id)).ToList();
                UiControls.Select(l, "Worktree", _worktreeChoices.FirstOrDefault(w => w.Id == _s.Worktree)?.Name ??
                    (string.IsNullOrEmpty(_s.Worktree) ? "Main checkout" : _s.Worktree), worktreeOptions, out _);
                if (!string.IsNullOrEmpty(_worktreeError)) UiLayout.Note(l, _worktreeError);
                var project = SessionHub.Instance.Project(_s.Project);
                GUI.color = UiTheme.Dim;
                l.Label(project != null
                    ? $"{project.Dir}  ({ProjectSummary.Of(project)})"
                    : SessionHub.Instance.Projects.Count == 0
                        ? "No projects exist. Add a project in the Projects window."
                        : "");
                GUI.color = Color.white;

            }

            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = EditorCommand(commandName);

            l.Gap(UiTheme.GapS);
            UiControls.Select(l, "Command", CommandLabel(preset), CommandOptions(), out _);

            // This field sets the raw command line. It can replace the selected preset or daemon default.
            l.Label("Command line override");
            _s.Cmd = UiControls.Field(l, "agent.cmd", _s.Cmd ?? "");
            GUI.color = UiTheme.Dim;
            l.Label(CommandNote(preset));
            GUI.color = Color.white;

            l.Gap(UiTheme.GapS);
            l.Label("Arguments");
            _s.Args = UiControls.Field(l, "agent.args", _s.Args ?? "");
            UiLayout.Note(l, "Appended to the preset, default, or overridden command line. Quote values containing spaces.");

            l.Gap(UiTheme.GapS);
            _s.Autostart = RecipeFlag(l, "autostart", "Start with the daemon", _s.Autostart, value => _s.Autostart = value);
            _s.AutoResume = RecipeFlag(l, "auto_resume", "Auto-resume last conversation", _s.AutoResume, value => _s.AutoResume = value,
                "After startup settles, send /resume and choose the latest conversation.");
            _s.PersistentTmp = RecipeFlag(l, "persistent_tmp", "Persistent /tmp", _s.PersistentTmp, value => _s.PersistentTmp = value,
                "Keep this agent's /tmp across restarts in its private state. Resetting private state gives it a fresh /tmp.");

        }

        void ApplyTemplate(AgentTemplateInfo template)
        {
            string name = _s.Name;
            string project = _s.Project;
            _templateSnapshot = template.Copy();
            template.ApplyTo(_s);
            _s.Name = name;
            _s.Project = project;
            _templateName = template.Name;
            _resourceLimits = new ResourceLimitsForm(_s.Limits);
            _dnsServers = _s.Dns?.Mode == DnsMode.Servers
                ? string.Join(", ", _s.Dns.Servers.ToArray())
                : "";
        }

        // Reach and the extra sandbox presets this agent adds on top of its command's.
        void DrawSandboxFields(Listing_Standard l)
        {
            DrawNetworkFields(l);
        }

        float DrawSandboxTrailing(Rect rect, float y, float availableHeight)
        {
            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = EditorCommand(commandName);

            return DrawExtraPresets(rect, y + UiTheme.GapL, preset, availableHeight);
        }

        void DrawNetworkFields(Listing_Standard l)
        {
            string networkLabel = NetworkModeText.ShortLabel(_s.Network);
            var networkOptions = new List<SelectorOption>
            {
            };
            networkOptions.AddRange(new[] { NetworkMode.None, NetworkMode.Private, NetworkMode.Host }
                .Select(mode => new SelectorOption("Custom: " + NetworkModeText.Label(mode),
                    () => { _s.Network = mode; if (EditingTemplate) _templateDraft.NetworkSpecified = true; })));
            UiControls.Select(l, "Network", networkLabel, networkOptions, out _);
            UiLayout.Note(l, "This agent owns this setting. Templates copy it. Project changes do not alter it.");

            l.Gap(UiTheme.GapS);
            DnsForm.Draw(l, _s.Dns, "agent.dns", ref _dnsServers,
                dns => { _s.Dns = dns; if (EditingTemplate) _templateDraft.DnsSpecified = true; });
        }

        float DrawExtraPresets(Rect rect, float y, CommandInfo preset,
            float availableHeight)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Additional sandbox presets");
            y += UiTheme.RowH + UiTheme.GapXS;

            var inheritedPresets = new List<string>();
            if (preset != null) inheritedPresets.AddRange(preset.Sandbox);
            // Size from the viewport, not the previous scroll content height, to avoid
            // growing the content on every layout pass.
            float height = Mathf.Max(PresetsH, rect.y + availableHeight - y - UiTheme.GapS);
            PresetList.Draw(new Rect(rect.x, y, rect.width, height), _s.Sandbox,
                _presetScroll, inheritedPresets, _templateSnapshot?.SandboxCatalog(),
                EditingTemplate ? "Added by template" : "Added by this agent");
            return y + height;
        }

        // Per-agent resource caps the daemon enforces with a systemd scope. Edited as strings.
        // parsed on Save. ScrollableListing owns the tab's measured height.
        void DrawLimits(Listing_Standard l)
        {
            if (!EditingTemplate && !SessionHub.Instance.Capabilities.PerSessionLimits)
            {
                UiLayout.Note(l,
                    "slopcar sets one CPU, memory, and process limit for the whole daemon. " +
                    "This runtime does not support per-agent limits.");
                return;
            }

            UiLayout.Note(l, "These limits apply to this agent. Leave a field blank for no cap.");
            _s.Limits = _resourceLimits.Draw(l);
        }

        // `command` selects an app preset. `cmd` can override its command line.
        // If both fields are blank, the daemon default applies.
        string CommandLabel(CommandInfo preset)
        {
            if (preset != null) return preset.Name;
            if (!string.IsNullOrEmpty(_s.Command)) return _s.Command + " (unknown here)";
            return string.IsNullOrEmpty((_s.Cmd ?? "").Trim()) ? "Daemon default" : "Command line";
        }

        string CommandNote(CommandInfo preset)
        {
            if (preset != null)
            {
                string sandbox = preset.Sandbox.Count > 0
                    ? "  Sandbox: " + string.Join(", ", preset.Sandbox.ToArray()) + "."
                    : "";
                return $"If blank, the daemon runs '{preset.Cmd}'.{sandbox}";
            }
            if (!string.IsNullOrEmpty((_s.Cmd ?? "").Trim()))
                return "This custom command does not use an app preset or its sandbox settings.";
            if (EditingTemplate) return "If blank, a new agent uses the destination daemon's default command.";
            return !string.IsNullOrEmpty(_s.Agent)
                ? $"If blank, the daemon runs its default agent ('{_s.Agent}')."
                : "If blank, the daemon runs its default agent.";
        }

        IEnumerable<SelectorOption> CommandOptions()
        {
            var options = new List<SelectorOption>();
            options.Add(new SelectorOption("Daemon default", () =>
            {
                _s.Command = "";
                _s.CommandPreset = "";
                _s.Cmd = "";
            }));

            // The daemon reads command names from the catalog. A new command file appears here after refresh.
            IEnumerable<CommandInfo> commands = SessionHub.Instance.Commands;
            var captured = _templateSnapshot?.ResolveCommand(_templateSnapshot.Command);
            if (captured != null)
                commands = new[] { captured }.Concat(commands).GroupBy(c => c.Name).Select(g => g.First());
            foreach (var c in commands)
            {
                var pick = c;
                options.Add(new SelectorOption($"{pick.Name}  -  {pick.Cmd}",
                    () => { _s.Command = pick.Name; _s.CommandPreset = pick.Name; }));
            }

            options.Add(new SelectorOption("Custom command", () => { _s.Command = ""; _s.CommandPreset = ""; }));
            return options;
        }

    }
}
