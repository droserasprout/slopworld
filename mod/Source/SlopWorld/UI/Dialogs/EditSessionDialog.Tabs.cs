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
        void DrawGeneral(Listing_Standard l)
        {
            if (_identity.IsNew && !EditingTemplate)
                DrawTemplatePicker(l);

            l.Label(EditingTemplate ? "Template name" : "Name (also the colonist's name)");
            _s.Name = UiControls.Field(l, "agent.name", _s.Name);

            if (EditingTemplate)
            {
                l.Label("Description");
                _templateDraft.Description = UiControls.Area(l, 48f, "template.description", _templateDraft.Description);
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
                UiControls.Select(l, "Project (the directory and sandbox it works in)",
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

        void DrawTemplatePicker(Listing_Standard l)
        {
            var options = new List<SelectorOption>
            {
                new SelectorOption("Manual creation", () => { _templateName = null; _templateSnapshot = null; }),
            };
            options.AddRange(SessionHub.Instance.Templates
                .OrderBy(t => t.Name, System.StringComparer.OrdinalIgnoreCase)
                .Select(t => new SelectorOption(t.DisplayLabel, () => ApplyTemplate(t))));

            string label = string.IsNullOrEmpty(_templateName) ? "Manual creation" : _templateName;
            UiControls.Select(l, "Template (optional)", label, options, out _);
            GUI.color = UiTheme.Dim;
            l.Label(string.IsNullOrEmpty(_templateName)
                ? "Choose a Library template or continue with the fields below."
                : "Template defaults are copied; the fields below remain editable overrides.");
            GUI.color = Color.white;
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
            _dnsServers = _s.DnsOverride?.Mode == DnsMode.Servers
                ? string.Join(", ", _s.DnsOverride.Servers.ToArray())
                : "";
        }

        void DrawMounts(Rect rect)
        {
            var projects = SessionHub.Instance.Projects;

            GUI.color = UiTheme.Dim;
            var hint = new Rect(rect.x, rect.y, rect.width, UiTheme.LineH);
            UiText.RowLabel(hint,
                "Mount other project directories into /mnt/<name>. The agent's own project is always mounted.");
            GUI.color = Color.white;

            float y = hint.yMax + UiTheme.GapS;

            if (projects.Count == 0)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH),
                    "No projects defined.");
                GUI.color = Color.white;
                return;
            }

            float rowH = UiTheme.RowH;
            float listH = projects.Count * rowH;
            var listRect = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Slab.Box(listRect, UiTheme.Well, UiTheme.Edge);
            var pad = listRect.ContractedBy(UiTheme.ListInset);
            var inner = new Rect(0f, 0f, pad.width - UiTheme.ScrollbarW, listH);

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

                    float labelW = row.width - MountButtonWidth - UiTheme.GapS;
                    GUI.color = isPrimary ? UiTheme.Lead : UiTheme.Name;
                    UiText.RowLabel(new Rect(row.x + UiTheme.GapS, row.y, labelW, row.height),
                        isPrimary ? p.Name + "  (primary)" : p.Name);
                    GUI.color = Color.white;

                    var btnRect = new Rect(row.xMax - MountButtonWidth, row.y,
                        MountButtonWidth, row.height);
                    if (UiButtons.Button(btnRect, MountEntry.ModeLabel(mode),
                            isPrimary ? UiTheme.Btn.Default : UiTheme.Btn.Ghost))
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
        void DrawSandboxFields(Listing_Standard l)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            DrawNetworkFields(l, project);
        }

        float DrawSandboxTrailing(Rect rect, float y, float availableHeight)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            string commandName = string.IsNullOrEmpty(_s.Command) ? _s.CommandPreset : _s.Command;
            var preset = EditorCommand(commandName);

            return DrawExtraPresets(rect, y + UiTheme.GapL, project, preset, availableHeight);
        }

        void DrawNetworkFields(Listing_Standard l, ProjectInfo project)
        {
            var projectNetwork = project?.Network ?? NetworkMode.Private;
            var inheritedDns = project?.Dns ?? _s.Dns;
            string networkLabel = _s.NetworkOverride.HasValue
                ? NetworkModeText.Label(_s.NetworkOverride.Value)
                : (EditingTemplate ? "Use destination project" : "Inherit project (" + NetworkModeText.ShortLabel(projectNetwork) + ")");
            var networkOptions = new List<SelectorOption>();
            networkOptions.Add(new SelectorOption((EditingTemplate ? "Use destination project" : "Inherit project (" + NetworkModeText.ShortLabel(projectNetwork) + ")"),
                    () => _s.NetworkOverride = null));
            networkOptions.AddRange(new[] { NetworkMode.None, NetworkMode.Private, NetworkMode.Host }
                .Select(mode => new SelectorOption(NetworkModeText.Label(mode),
                    () => _s.NetworkOverride = mode)));
            UiControls.Select(l, "Network", networkLabel, networkOptions, out _);
            GUI.color = UiTheme.Dim;
            l.Label(EditingTemplate ? "Copied to each agent created from this template."
                : "The project sets the default; this agent can use any network mode.");
            GUI.color = Color.white;

            l.Gap(UiTheme.GapS);
            DnsForm.Draw(l, _s.DnsOverride, inheritedDns, true, "agent.dns", ref _dnsServers,
                dns => _s.DnsOverride = dns);
        }

        float DrawExtraPresets(Rect rect, float y, ProjectInfo project, CommandInfo preset,
            float availableHeight)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Extra sandbox presets");
            y += UiTheme.RowH + UiTheme.GapXS;

            var inheritedPresets = new List<string>();
            if (preset != null) inheritedPresets.AddRange(preset.Sandbox);
            if (project != null) inheritedPresets.AddRange(project.Sandbox);
            // Size from the viewport, not the previous scroll content height, to avoid
            // growing the content on every layout pass.
            float height = Mathf.Max(PresetsH, rect.y + availableHeight - y - UiTheme.GapS);
            PresetList.Draw(new Rect(rect.x, y, rect.width, height), _s.Sandbox,
                _presetScroll, inheritedPresets, _templateSnapshot?.SandboxCatalog());
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

            GUI.color = UiTheme.Dim;
            l.Label(EditingTemplate ? "Blank uses the destination project limit."
                : "Blank inherits the project limit. With no project limit, no cap is configured.");
            GUI.color = Color.white;

            _s.Limits = _resourceLimits.Draw(l);
        }

        void DrawBreadcrumbs(Rect rect)
        {
            var config = SessionHub.Instance.Config;
            bool breadcrumbs = EditingTemplate || config.ExperimentalBreadcrumbs;
            bool instructions = EditingTemplate || config.ExperimentalInstructions;
            float y = rect.y;
            if (EditingTemplate)
            {
                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(rect);
                _s.BreadcrumbYolo = RecipeFlag(l, "breadcrumb_yolo", "Paste breadcrumbs on first Enter",
                    _s.BreadcrumbYolo, value => _s.BreadcrumbYolo = value);
                _s.InstructionsBreadcrumb = RecipeFlag(l, "instructions_breadcrumb", "Instructions discovery",
                    _s.InstructionsBreadcrumb, value => _s.InstructionsBreadcrumb = value);
                y += l.CurHeight + UiTheme.GapS;
                l.End();
            }
            else
            {
                _s.BreadcrumbYolo = UiControls.Checkbox(
                    new Rect(rect.x, rect.y, rect.width, UiTheme.RowH),
                    "YOLO breadcrumbs", _s.BreadcrumbYolo,
                    "Hijack the first Enter after startup and paste every enabled breadcrumb before it. Requires breadcrumbs in Settings > General > Experimental.",
                    locked: !breadcrumbs);
                y += UiTheme.RowH + UiTheme.GapXS;
            }
            var projectBreadcrumbs = SessionHub.Instance.Project(_s.Project)?.Breadcrumbs;
            BreadcrumbList.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)),
                _s.Breadcrumbs, _breadcrumbScroll, projectBreadcrumbs,
                EditingTemplate ? null : config.InstructionsBreadcrumb,
                _s.InstructionsBreadcrumb,
                onInstructionsChanged: on => _s.InstructionsBreadcrumb = on,
                instructionsLocked: !breadcrumbs || !instructions,
                breadcrumbsLocked: !breadcrumbs, catalog: _templateSnapshot?.BreadcrumbCatalog());
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
            if (EditingTemplate) return "Blank uses the destination daemon’s default command.";
            return !string.IsNullOrEmpty(_s.Agent)
                ? $"Blank runs the daemon's default, which is '{_s.Agent}'."
                : "Blank runs the daemon's default agent.";
        }

        IEnumerable<SelectorOption> CommandOptions()
        {
            var options = new List<SelectorOption>();
            options.Add(new SelectorOption(EditingTemplate ? "Use daemon default" : "Default", () =>
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
