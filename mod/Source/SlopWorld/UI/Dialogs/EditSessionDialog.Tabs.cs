using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Agent editor fields, tab bodies, and their tab-specific option lists.
    public partial class EditSessionDialog
    {
        // The agent itself: what it is called, where it works and what it runs. Everything a
        // new agent must have to start; the other tabs only refine it.
        void DrawGeneral(Listing_Standard l)
        {
            if (_identity.IsNew && !EditingTemplate && !string.IsNullOrEmpty(_templateName))
                UiLayout.Note(l, "Started from: " + _templateName +
                    ". Settings were copied once; later template changes do not update this agent.");

            l.Label(EditingTemplate ? "Template name" : "Name (also the colonist's name)");
            _s.Name = UiControls.Field(l, "agent.name", _s.Name);

            if (EditingTemplate)
            {
                l.Label("Description");
                _templateDraft.Description = UiControls.Area(l, 48f, "template.description", _templateDraft.Description);
                UiLayout.Note(l, "Saved agent customizations. Copied once when creating an agent; project mounts are chosen separately.");
                l.Label("Source: " + _templateDraft.Source);
                var origin = JVal.Parse(_templateDraft.OriginJson);
                if (TemplateReadOnly) l.Label("Edit " + origin["file"].AsString() + " or duplicate this template.");
                else if (!string.IsNullOrEmpty(_templateDraft.OriginProject))
                    l.Label("Captured from: " + _templateDraft.OriginProject + "/" + _templateDraft.OriginAgent);
            }
            else
            {
                var projectOptions = SessionHub.Instance.Projects
                    .Select(p => new SelectorOption($"{p.Name}  -  {p.Dir}",
                        () => _s.Project = p.Name)).ToList();
                projectOptions.Add(new SelectorOption("New project...",
                    () => Find.WindowStack.Add(new EditProjectDialog(null))));
                UiControls.Select(l, "Project (the directory and shared mounts it uses)",
                    string.IsNullOrEmpty(_s.Project) ? "Pick a project..." : _s.Project,
                    projectOptions, out _);

                var project = SessionHub.Instance.Project(_s.Project);
                GUI.color = UiTheme.Dim;
                l.Label(project != null
                    ? $"{project.Dir}  ({ProjectsView.Summary(project)})"
                    : SessionHub.Instance.Projects.Count == 0
                        ? "No projects yet - make one in the Projects window first."
                        : "");
                GUI.color = Color.white;

            }

            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = EditorCommand(commandName);

            l.Gap(UiTheme.GapS);
            UiControls.Select(l, "Command", CommandLabel(preset), CommandOptions(), out _);

            // Editable whichever it is: a preset says what an agent is, and this box says
            // what this one runs, which is the same field either way.
            _s.Cmd = UiControls.Field(l, "agent.cmd", _s.Cmd ?? "");
            GUI.color = UiTheme.Dim;
            l.Label(CommandNote(preset));
            GUI.color = Color.white;

            l.Gap(UiTheme.GapS);
            _s.Autostart = RecipeFlag(l, "autostart", "Start with the daemon", _s.Autostart, value => _s.Autostart = value);
            _s.AutoResume = RecipeFlag(l, "auto_resume", "Auto-resume last conversation", _s.AutoResume, value => _s.AutoResume = value,
                "After startup settles, send /resume and choose the latest conversation.");
            _s.SlopworldMd = RecipeFlag(l, "slopworld_md", "Mount SLOPWORLD.md", _s.SlopworldMd, value => _s.SlopworldMd = value,
                "Mount generated runtime context read-only at the Instructions mount path. Requires instructions in Settings > General > Experimental.",
                locked: !EditingTemplate && !SessionHub.Instance.Config.ExperimentalInstructions);
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
            UiLayout.Note(l, "Owned by this agent and copied into templates; project changes do not alter it.");

            l.Gap(UiTheme.GapS);
            DnsForm.Draw(l, _s.Dns, "agent.dns", ref _dnsServers,
                dns => { _s.Dns = dns; if (EditingTemplate) _templateDraft.DnsSpecified = true; });
        }

        float DrawExtraPresets(Rect rect, float y, CommandInfo preset,
            float availableHeight)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Sandbox contributions");
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

        // Per-agent resource caps the daemon enforces with a systemd scope. Edited as strings;
        // parsed on Save. ScrollableListing owns the tab's measured height.
        void DrawLimits(Listing_Standard l)
        {
            if (!EditingTemplate && !SessionHub.Instance.Capabilities.PerSessionLimits)
            {
                UiLayout.Note(l,
                    "slopcar has one outer CPU, memory and process budget. Per-agent limits " +
                    "need delegated cgroups and are unavailable in this runtime.");
                return;
            }

            UiLayout.Note(l, "Limits are owned by this agent. No cap leaves the field unset.");
            _s.Limits = _resourceLimits.Draw(l);
        }

        // The three states this pair of fields can be in: a command preset, a command line
        // of its own, or neither, which is whatever the daemon's `[defaults] agent` names.
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
                return $"Blank runs '{preset.Cmd}'.{sandbox}";
            }
            if (!string.IsNullOrEmpty((_s.Cmd ?? "").Trim()))
                return "A command line of its own, so no agent's state directory comes with it.";
            if (EditingTemplate) return "Blank uses the destination daemon’s default command.";
            return !string.IsNullOrEmpty(_s.Agent)
                ? $"Blank runs the daemon's default, which is '{_s.Agent}'."
                : "Blank runs the daemon's default agent.";
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

            // Named by the daemon rather than listed here, so a command file dropped in its
            // preset directory is an entry in this menu and nothing to rebuild.
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

            options.Add(new SelectorOption("Command line...", () => { _s.Command = ""; _s.CommandPreset = ""; }));
            return options;
        }

    }
}
