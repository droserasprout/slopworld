using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Project selection owns the working directory. Network settings belong to the agent.
    public partial class EditSessionDialog : UiWindow
    {
        enum Tab { General, Sandbox, ResourceLimits, Preview }

        readonly EditIdentity _identity;
        readonly SessionInfo _s;
        readonly SmoothScroll _presetScroll = new SmoothScroll();
        SmoothScroll _previewScroll = new SmoothScroll();
        readonly DaemonSettingsPreview _settingsPreview = new DaemonSettingsPreview();
        readonly ScrollableListing _generalListing = new ScrollableListing(320f);
        readonly ScrollableListing _sandboxListing = new ScrollableListing(480f);
        readonly ScrollableListing _limitsListing = new ScrollableListing(320f);
        const float PresetsH = 240f;
        Tab _tab;
        string _templateName;
        string _error;
        bool _saving;

        // Keep limits as raw strings so a half-typed number survives each frame.
        // On Save, parse the strings into `_s.Limits`.
        ResourceLimitsForm _resourceLimits;
        string _dnsServers;

        public EditSessionDialog(SessionInfo existing) : this(existing, null) { }

        // Preselected project, for "add an agent here" from the projects list.
        public EditSessionDialog(SessionInfo existing, string project) : this(existing, project, false) { }

        public static EditSessionDialog FromTemplate(AgentTemplateInfo template)
        {
            var dialog = new EditSessionDialog(null, null);
            dialog.ApplyTemplate(template);
            return dialog;
        }

        public static EditSessionDialog Copy(SessionInfo of) => new EditSessionDialog(of, null, true);

        EditSessionDialog(SessionInfo existing, string project, bool copy, AgentTemplateInfo template = null)
        {
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
                    Worktree = existing.Worktree,
                    WorktreeName = existing.WorktreeName,
                    Command = existing.Command,
                    CommandPreset = existing.CommandPreset,
                    Cmd = existing.Cmd,
                    Args = existing.Args,
                    Sandbox = new List<string>(existing.Sandbox),
                    PersistentTmp = existing.PersistentTmp,
                    Network = existing.Network,
                    Dns = existing.Dns.Copy(),
                    Limits = existing.Limits,
                    Mounts = new List<MountEntry>(existing.Mounts
                        .Select(m => new MountEntry { From = m.From, To = m.To, Mode = m.Mode })),
                    Agent = existing.Agent,
                    Autostart = existing.Autostart,
                    AutoResume = existing.AutoResume,
                };


            if (existing != null && !copy)
            {
                string endpoint = DaemonClient.BaseUrl;
                int connection = SessionHub.Instance.ConnectionGeneration;
                DaemonClient.Post<Wire.SettingsPreview>(WireProtocol.Routes.SettingsPreview,
                    new Wire.SettingsPreviewRequest { Existing = _identity.OriginalName },
                    response =>
                    {
                        if (endpoint == DaemonClient.BaseUrl && connection == SessionHub.Instance.ConnectionGeneration)
                            _templateSnapshot = AgentTemplateInfo.FromWire(new Wire.AgentTemplate { Defaults = response.Definitions?.Defaults });
                    }, UiLayout.Fail);
            }

            if (template != null)
            {
                _templateDraft = template.Copy();
                _templateOriginalName = template.Name;
                ApplyTemplate(_templateDraft);
                _s.Name = template.Name;
            }

            SessionHub.Instance.Catalog.RefreshProjects();
            // Both tables are files the daemon reads, so they are asked for on every open
            // rather than once per process.
            SessionHub.Instance.Catalog.LoadPresets();
            SessionHub.Instance.Catalog.RefreshTemplates();
            SessionHub.Instance.Catalog.RefreshLibrary();
            if (!EditingTemplate && string.IsNullOrEmpty(_s.CommandPreset) && string.IsNullOrEmpty(_s.Command) &&
                string.IsNullOrWhiteSpace(_s.Cmd))
                DaemonClient.Get<Wire.ConfigResult>(WireProtocol.Routes.Config,
                    j =>
                    {
                        if (string.IsNullOrEmpty(_templateName) && string.IsNullOrEmpty(_s.Command) &&
                            string.IsNullOrEmpty(_s.CommandPreset) && string.IsNullOrWhiteSpace(_s.Cmd))
                            _s.CommandPreset = j.Values.Defaults.Agent;
                    },
                    UiLayout.Fail);

            _resourceLimits = new ResourceLimitsForm(_s.Limits);
            _dnsServers = _s.Dns?.Mode == DnsMode.Servers
                ? string.Join(", ", _s.Dns.Servers.ToArray())
                : "";
            // Template descriptions are multiline; Enter must remain a newline.
            if (!EditingTemplate) AcceptOnEnter(Save);
        }


        // A left rail of short pages rather than one long form. The agent, its sandbox, its
        // resource limits, and the preview each get their own tab.
        public override Vector2 InitialSize => new Vector2(660f, 800f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), EditingTemplate
                ? (_templateDraft.Version == 0 ? "New template" : "Edit template")
                : _identity.Title("agent"));

            var layout = TabbedFormLayout.Arrange(SettingsPageLayout.FromRect(rect), 132f,
                UiTheme.HeaderH, UiTheme.BtnH, UiTheme.GapS, UiTheme.GapM);
            DrawRail(SettingsPageLayout.ToRect(layout.Rail));
            var body = SettingsPageLayout.ToRect(layout.Body);

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !_templateBusy && !_saving;
            try
            {
                switch (_tab)
                {
                    case Tab.General:
                        _generalListing.Draw(body, DrawGeneral);
                        break;
                    case Tab.Sandbox:
                        _sandboxListing.Draw(body, DrawSandboxFields,
                            (view, y) => DrawSandboxTrailing(view, y, body.height));
                        break;
                    case Tab.ResourceLimits:
                        _limitsListing.Draw(body, DrawLimits);
                        break;
                    case Tab.Preview:
                        DrawSettingsPreview(body);
                        break;
                }

            }
            finally { GUI.enabled = enabled; }

            var foot = new UiLayout.Bar(SettingsPageLayout.ToRect(layout.Footer));
            if (EditingTemplate)
            {
                if (_templateDraft.Version != 0 &&
                    foot.Left("Reload", UiTheme.Btn.Ghost, !_templateBusy)) ReloadTemplate();
            }
            if (!EditingTemplate && !_identity.IsNew && foot.Left("Reset private state", UiTheme.Btn.Danger))
                Find.WindowStack.Add(CatalogActions.ResetState(_identity.OriginalName));
            if (!EditingTemplate && !_identity.IsNew && foot.Left("Save as template", UiTheme.Btn.Ghost))
                Find.WindowStack.Add(new SaveAgentTemplateDialog(_identity.OriginalName));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            bool save = foot.Right("Save", UiTheme.Btn.Primary, !_templateBusy && !_saving);
            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
            if (save) Save();
        }

        void DrawRail(Rect r) => UiLayout.DrawRail(r, new[]
        {
            ("General", Tab.General),
            ("Sandbox", Tab.Sandbox),
            ("Resource limits", Tab.ResourceLimits),
            ("Preview", Tab.Preview),
        }, ref _tab);

        void Save()
        {
            if (_templateBusy || _saving) return;
            _error = null;
            if (string.IsNullOrWhiteSpace(_s.Name) || (!EditingTemplate && string.IsNullOrEmpty(_s.Project)))
            {
                _error = EditingTemplate ? "Enter a template name." : "Enter an agent name and project.";
                return;
            }

            if (!_resourceLimits.TrySave(out var limits, out var limitError))
            {
                _error = limitError;
                return;
            }
            string dnsError;
            if (!DnsForm.TrySave(_s.Dns, _dnsServers, out dnsError))
            {
                _error = "DNS: " + dnsError;
                return;
            }
            _s.Limits = limits;

            if (EditingTemplate)
            {
                _templateDraft.Name = _s.Name.Trim();
                _templateBusy = true;
                SessionHub.Instance.Catalog.SaveAgentTemplateForm(_templateDraft, _s,
                    _templateDraft.Version == 0, _templateOriginalName, () => Close(), TemplateFailed);
                return;
            }

            string from = _identity.OriginalName, to = _s.Name;
            Action ok = () =>
            {
                // The daemon took the rename, so carry the colonist over before the next
                // reconcile sees a name it doesn't know and retires it.
                if (!_identity.IsNew && from != to)
                {
                    AgentColony.Current?.Rename(from, to);
                    TerminalWindow.RenameActive(from, to);
                }
                Close();
            };
            _saving = true;
            if (_identity.IsNew && !string.IsNullOrEmpty(_templateName))
                SessionHub.Instance.CreateFromTemplate(_templateName, _s, ok, SaveFailed);
            else
                SessionHub.Instance.Save(_s, _identity.IsNew, _identity.OriginalName, ok,
                    SaveFailed);
        }

        void SaveFailed(string error)
        {
            _saving = false;
            _error = string.IsNullOrWhiteSpace(error)
                ? "The daemon rejected the save without an error message."
                : error;
        }
    }

}
