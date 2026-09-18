using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What is left here is the two things about the agent: its name and what it runs.
    // Where it works moved to the project, which is why picking one is mandatory and
    // there is no directory field; network reach is an agent-level setting here.
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

        // Limits are edited as raw strings so a half-typed number is not lost to a reparse each
        // frame; they are parsed back into `_s.Limits` on Save.
        ResourceLimitsForm _resourceLimits;
        string _dnsServers;

        public EditSessionDialog(SessionInfo existing) : this(existing, null) { }

        // Preselected project, for "add an agent here" from the projects list.
        public EditSessionDialog(SessionInfo existing, string project) : this(existing, project, false) { }

        // Everything the dialog can edit comes over - the project above all, since a
        // second agent in the same repo is what this is for and picking that project
        // again by hand is the step that gets it wrong. The name cannot, so it is the one
        // field that is suggested rather than copied.
        public static EditSessionDialog FromTemplate(AgentTemplateInfo template)
        {
            var dialog = new EditSessionDialog(null, null);
            dialog.ApplyTemplate(template);
            return dialog;
        }

        public static EditSessionDialog Copy(SessionInfo of) => new EditSessionDialog(of, null, true);

        EditSessionDialog(SessionInfo existing, string project, bool copy, AgentTemplateInfo template = null)
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
            AcceptOnEnter(Save);
        }


        // A left rail of short pages rather than one long form: the agent, its sandbox, its
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
            GUI.enabled = enabled && !_templateBusy;
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
            if (foot.Right("Save", UiTheme.Btn.Primary, !_templateBusy)) Save();
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
            if (_templateBusy) return;
            if (string.IsNullOrWhiteSpace(_s.Name) || (!EditingTemplate && string.IsNullOrEmpty(_s.Project)))
            {
                UiLayout.Fail(EditingTemplate ? "Template name is required" : "name and project are required");
                return;
            }

            if (!_resourceLimits.TrySave(out var limits, out var limitError))
            {
                UiLayout.Fail(limitError);
                return;
            }
            string dnsError;
            if (!DnsForm.TrySave(_s.Dns, _dnsServers, out dnsError))
            {
                UiLayout.Fail("DNS: " + dnsError);
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
            if (_identity.IsNew && !string.IsNullOrEmpty(_templateName))
                SessionHub.Instance.CreateFromTemplate(_templateName, _s, ok, UiLayout.Fail);
            else
                SessionHub.Instance.Save(_s, _identity.IsNew, _identity.OriginalName, ok,
                    UiLayout.Fail);
        }
    }

}
