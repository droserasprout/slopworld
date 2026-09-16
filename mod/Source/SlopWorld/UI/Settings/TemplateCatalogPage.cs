using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon-backed personal template catalog. The draft is a copy of the selected wire
    // definition, so a failed PUT/DELETE or a superseded refresh never replaces what the player
    // is editing. The editor deliberately uses the same controls as the agent form for portable
    // command, sandbox, prompt, network, startup, and limit fields.
    public sealed class TemplateCatalogPage : IOptionPage
    {
        readonly AsyncLoadState<bool> _load = new AsyncLoadState<bool>();
        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly ScrollableListing _editor = new ScrollableListing(320f);
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        AgentTemplateInfo _draft;
        SessionInfo _form;
        string _originalName;
        string _error;
        bool _saving;
        string _limMem, _limPids, _limNofile, _limCpu, _dnsServers;

        bool Loaded => _load.HasValue && !_load.Loading;

        public void Load()
        {
            SessionHub.Instance.Catalog.LoadPresets(fail: message => _error = message);
            SessionHub.Instance.Catalog.RefreshLibrary(message => _error = message);
            _load.Load((ok, fail) => SessionHub.Instance.Catalog.RefreshTemplates(fail, () => ok(true)),
                _ =>
                {
                    _error = null;
                    if (_draft == null && SessionHub.Instance.Templates.Count > 0)
                        Select(SessionHub.Instance.Templates[0]);
                });
        }

        public void Draw(Rect rect)
        {
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !_saving;
            try { DrawPage(rect); }
            finally { GUI.enabled = enabled; }
        }

        void DrawPage(Rect rect)
        {
            var body = SettingsPageLayout.Body(rect);
            if (!Loaded)
            {
                UiText.StatusLabel(body, _error ?? _load.Error ?? "Waiting for the daemon...",
                    (_error ?? _load.Error) != null ? UiTheme.Bad : UiTheme.Dim);
            }
            else
            {
                DrawCatalog(body);
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Reload", UiTheme.Btn.Ghost, !_load.Loading)) Reload();
            if (foot.Left("New", UiTheme.Btn.Ghost, Loaded)) Select(new AgentTemplateInfo());
            if (foot.Left("Capture agent", UiTheme.Btn.Ghost, !_load.Loading)) CaptureAgent();
            if (_draft != null && _draft.Version != 0 && foot.Left("Duplicate", UiTheme.Btn.Ghost, !_load.Loading))
                Duplicate();
            if (_draft != null && _draft.Version != 0 && foot.Right("Delete", UiTheme.Btn.Danger, !_load.Loading))
                Delete();
            if (_draft != null && foot.Right("Save", UiTheme.Btn.Primary, !_load.Loading))
                Save();
        }

        void Reload()
        {
            // This is an explicit draft discard. Ordinary catalog refreshes leave the draft
            // alone, including when a stale response is ignored by CatalogRequest.
            _draft = null;
            _form = null;
            _originalName = null;
            _error = null;
            Load();
        }

        void DrawCatalog(Rect rect)
        {
            UiLayout.SectionHeading(new Rect(rect.x, rect.y, rect.width, UiTheme.RowH),
                "Personal agent templates");
            string caption = "Templates capture portable agent behavior. Origin is display metadata; existing agents keep their own snapshots.";
            float captionY = rect.y + UiTheme.RowH + UiTheme.GapXS;
            UiText.StatusLabel(new Rect(rect.x, captionY, rect.width,
                UiText.StatusLabelHeight(caption, rect.width)), caption, UiTheme.Dim);
            float top = captionY + UiText.StatusLabelHeight(caption, rect.width) + UiTheme.GapS;
            var content = new UiLayoutRect(rect.x, top, rect.width,
                Mathf.Max(0f, rect.yMax - top));
            var split = SandboxLayout.Arrange(content, UiTheme.GapM);
            DrawList(SettingsPageLayout.ToRect(split.List));
            DrawEditor(SettingsPageLayout.ToRect(split.Editor));
        }

        void DrawList(Rect rect)
        {
            Slab.Box(rect, UiTheme.Well, UiTheme.Edge);
            var pad = rect.ContractedBy(UiTheme.ListInset);
            var templates = SessionHub.Instance.Templates;
            var geometry = UiScrollBody.Measure(pad, templates.Count * UiTheme.RowH,
                UiScrollbarReservation.WhenNeeded);
            using (_listScroll.Scope(pad, geometry.View))
            {
                float y = 0f;
                foreach (var template in templates)
                {
                    var row = new Rect(UiTheme.GapS, y,
                        Mathf.Max(0f, geometry.View.width - UiTheme.GapS), UiTheme.RowH);
                    y += UiTheme.RowH;
                    bool selected = _draft != null && template.Name == _originalName;
                    if (UiButtons.Button(row, template.DisplayLabel,
                        selected ? UiTheme.Btn.Primary : UiTheme.Btn.Ghost))
                        Select(template);
                }
                if (templates.Count == 0)
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(UiTheme.GapS, 0f, geometry.View.width,
                        UiTheme.LineH), "No personal templates yet.");
                    GUI.color = Color.white;
                }
            }
        }

        void Select(AgentTemplateInfo template)
        {
            _draft = template.Copy();
            _originalName = template.Name;
            _form = new SessionInfo();
            _draft.ApplyTo(_form);
            _form.Name = template.Name;
            _limMem = LimStr(_form.Limits.MemoryMb);
            _limPids = LimStr(_form.Limits.Pids);
            _limNofile = LimStr(_form.Limits.Nofile);
            _limCpu = LimStr(_form.Limits.CpuPct);
            _dnsServers = _form.Dns?.Mode == DnsMode.Servers
                ? string.Join(", ", _form.Dns.Servers.ToArray()) : "";
            _error = null;
        }

        void DrawEditor(Rect rect)
        {
            if (_draft == null)
            {
                Slab.Box(rect, UiTheme.Well, UiTheme.Edge);
                UiText.StatusLabel(rect.ContractedBy(UiTheme.GapM),
                    "Select a template to inspect or edit it.", UiTheme.Dim);
                return;
            }

            _editor.Draw(rect, DrawFields);
        }

        void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, string.IsNullOrEmpty(_draft.Name) ? "New template" : _draft.Name);
            if (!string.IsNullOrEmpty(_error)) UiLayout.Validation(l, _error);
            l.Label("Origin: " + OriginLabel(_draft));
            l.Gap(UiTheme.GapS);

            l.Label("Name");
            _draft.Name = UiControls.Field(l, "template.name", _draft.Name);
            l.Label("Description");
            _draft.Description = UiControls.Area(l, 48f, "template.description",
                _draft.Description, on: true);

            var commands = SessionHub.Instance.Commands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new SelectorOption(c.Name + "  -  " + c.Cmd, () =>
                {
                    _form.Command = c.Name;
                    _form.CommandPreset = c.Name;
                })).ToList();
            commands.Add(new SelectorOption("Command line...", () =>
            {
                _form.Command = "";
                _form.CommandPreset = "";
            }));
            string commandName = string.IsNullOrEmpty(_form.Command)
                ? _form.CommandPreset : _form.Command;
            UiControls.Select(l, "Command", commandName, commands, out _);
            l.Label("Command line override");
            _form.Cmd = UiControls.Field(l, "template.cmd", _form.Cmd ?? "");

            l.Gap(UiTheme.GapS);
            UiLayout.SectionHeading(l, "Sandbox and prompts");
            var captured = JVal.Parse(_draft.DefaultsJson);
            var presets = captured["sandbox_presets"].Items.Select(PresetInfo.FromJson)
                .Concat(SessionHub.Instance.Presets).GroupBy(p => p.Name).Select(g => g.First());
            var command = captured["command"]["name"].AsString() == commandName
                ? CommandInfo.FromJson(captured["command"])
                : SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == commandName);
            PresetList.Draw(l.GetRect(190f), _form.Sandbox, _presetScroll, command?.Sandbox, presets);
            l.Gap(UiTheme.GapS);
            l.Label("Named prompts");
            string prompts = UiControls.Area(l, 72f, "template.prompts",
                DaemonConfig.Lines(_form.Breadcrumbs), on: true);
            _form.Breadcrumbs = DaemonConfig.Split(prompts);

            l.Gap(UiTheme.GapS);
            UiLayout.SectionHeading(l, "Reach and startup");
            var networks = new[] { NetworkMode.None, NetworkMode.Private, NetworkMode.Host }
                .Select(mode => new SelectorOption(NetworkModeText.Label(mode),
                    () => _form.Network = mode));
            UiControls.Select(l, "Network", NetworkModeText.Label(_form.Network), networks, out _);
            DnsForm.Draw(l, _form.Dns, DnsConfig.Resolved(), false, "template.dns",
                ref _dnsServers, dns => _form.Dns = dns);
            _form.SlopworldMd = UiControls.Checkbox(l, "Mount SLOPWORLD.md", _form.SlopworldMd);
            _form.InstructionsBreadcrumb = UiControls.Checkbox(l, "Instructions breadcrumb",
                _form.InstructionsBreadcrumb);
            _form.PersistentTmp = UiControls.Checkbox(l, "Persistent /tmp", _form.PersistentTmp);
            _form.BreadcrumbYolo = UiControls.Checkbox(l, "Paste prompts on first Enter",
                _form.BreadcrumbYolo);
            _form.Autostart = UiControls.Checkbox(l, "Start with the daemon", _form.Autostart);
            _form.AutoResume = UiControls.Checkbox(l, "Auto-resume last conversation",
                _form.AutoResume);

            l.Gap(UiTheme.GapS);
            UiLayout.SectionHeading(l, "Resource limits");
            l.Label("Memory (MB)");
            _limMem = UiControls.Field(l, "template.limit.memory", _limMem ?? "");
            l.Label("Max processes");
            _limPids = UiControls.Field(l, "template.limit.pids", _limPids ?? "");
            l.Label("Open files");
            _limNofile = UiControls.Field(l, "template.limit.nofile", _limNofile ?? "");
            l.Label("CPU (%)");
            _limCpu = UiControls.Field(l, "template.limit.cpu", _limCpu ?? "");

            l.Gap(UiTheme.GapS);
            UiLayout.SectionHeading(l, "Effective preview");
            l.Label("Command: " + (string.IsNullOrWhiteSpace(_form.Cmd)
                ? command?.Cmd ?? "Choose a command" : _form.Cmd));
            try
            {
                var preview = JVal.Parse(_draft.ToJson(_form))["defaults"];
                foreach (var preset in preview["sandbox_presets"].Items)
                    l.Label("Sandbox: " + preset["name"].AsString() + " — " + preset["description"].AsString());
                foreach (var prompt in preview["prompts"].Items)
                    l.Label("Prompt: " + prompt["name"].AsString() + "\n" + prompt["text"].AsString());
            }
            catch (InvalidOperationException error) { UiLayout.Validation(l, error.Message); }
            l.Label("Limits: " + LimitsLabel());
        }

        void CaptureAgent()
        {
            var options = SessionHub.Instance.Sessions
                .Where(s => !s.Worker && !s.Ephemeral && !s.Host)
                .Select(s => new FloatMenuOption(s.Name, () =>
                    Find.WindowStack.Add(new SaveAgentTemplateDialog(s.Name))))
                .ToList();
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No configured agents", null));
            Find.WindowStack.Add(new UiMenu(options));
        }

        void Duplicate()
        {
            string name = _draft.Name + "-copy";
            int suffix = 2;
            while (SessionHub.Instance.Templates.Any(t => t.Name == name))
                name = _draft.Name + "-copy-" + suffix++;
            _saving = true;
            SessionHub.Instance.Catalog.DuplicateAgentTemplate(_originalName, name,
                _draft.Description, Saved, Failed);
        }

        void Delete()
        {
            var name = _originalName;
            var target = _draft.Copy();
            Find.WindowStack.Add(ConfirmDialog.Create(
                "Remove template '" + name + "'? Existing agents keep their snapshots.",
                () =>
                {
                    _saving = true;
                    SessionHub.Instance.Catalog.RemoveAgentTemplate(target, name, Saved, Failed);
                }, destructive: true));
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(_draft.Name))
            {
                _error = "Template name is required.";
                return;
            }
            if (!TryLimit(_limMem, "Memory", out var memory) ||
                !TryLimit(_limPids, "Max processes", out var pids) ||
                !TryLimit(_limNofile, "Open files", out var nofile) ||
                !TryLimit(_limCpu, "CPU", out var cpu)) return;
            _form.Limits = new SessionLimits
            {
                MemoryMb = memory,
                Pids = pids,
                Nofile = nofile,
                CpuPct = cpu,
            };
            _saving = true;
            SessionHub.Instance.Catalog.SaveAgentTemplateForm(_draft, _form,
                _draft.Version == 0, _originalName,
                Saved, Failed);
        }

        void Saved()
        {
            _saving = false;
            Reload();
        }

        void Failed(string message)
        {
            _saving = false;
            _error = message;
        }

        string LimitsLabel() => string.Join(", ", new[]
        {
            LimitLabel("memory", _limMem), LimitLabel("pids", _limPids),
            LimitLabel("nofile", _limNofile), LimitLabel("cpu", _limCpu),
        }.Where(value => value.Length > 0).ToArray());

        static string LimitLabel(string name, string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : name + "=" + value;

        static bool TryLimit(string text, string label, out int? value)
        {
            value = null;
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return true;
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
            {
                value = n;
                return true;
            }
            UiLayout.Fail(label + " must be a whole number of at least 1, or blank for no cap");
            return false;
        }

        static string LimStr(int? value) => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

        static string OriginLabel(AgentTemplateInfo template) =>
            string.IsNullOrEmpty(template.OriginProject) || string.IsNullOrEmpty(template.OriginAgent)
                ? template.Source : template.Source + " (" + template.OriginProject + "/" +
                  template.OriginAgent + ")";
    }
}
