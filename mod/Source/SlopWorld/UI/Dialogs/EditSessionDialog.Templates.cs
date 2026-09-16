using System;
using System.Linq;
using Verse;
using UnityEngine;

namespace SlopWorld
{
    // Templates use the agent form with portable defaults and a different persistence target.
    public partial class EditSessionDialog
    {
        AgentTemplateInfo _templateDraft;
        AgentTemplateInfo _templateSnapshot;
        string _templateOriginalName;
        bool _templateBusy;

        bool EditingTemplate => _templateDraft != null;
        bool TemplateReadOnly => _templateDraft?.Source == "project";

        public static EditSessionDialog EditTemplate(AgentTemplateInfo template = null, bool copy = false)
        {
            var draft = template?.Copy() ?? new AgentTemplateInfo();
            if (copy)
            {
                var name = draft.Name.Split(new[] { "::" }, System.StringSplitOptions.None).Last();
                draft.Name = EditIdentity.ForCopy(name).CopyName(
                    SessionHub.Instance.Templates.Select(t => t.Name), "template");
                draft.Version = 0;
                draft.Source = "personal";
            }
            return new EditSessionDialog(null, null, false, draft);
        }

        CommandInfo EditorCommand(string name) => _templateSnapshot == null
            ? SessionHub.Instance.Command(name) : _templateSnapshot.ResolveCommand(name);

        bool RecipeFlag(Listing_Standard l, string name, string label, bool value, Action<bool> set,
            string tip = null, bool locked = false)
        {
            if (!EditingTemplate) return UiControls.Checkbox(l, label, value, tip, locked: locked);
            UiControls.Select(l, label, _templateDraft.SpecifiedFlags.Contains(name)
                ? (value ? "Enabled" : "Disabled") : "Use default", new[]
            {
                new SelectorOption("Use default", () =>
                {
                    _templateDraft.SpecifiedFlags.Remove(name);
                    set(name == "instructions_breadcrumb" || name == "breadcrumb_yolo");
                }),
                new SelectorOption("Enabled", () => { _templateDraft.SpecifiedFlags.Add(name); set(true); }),
                new SelectorOption("Disabled", () => { _templateDraft.SpecifiedFlags.Add(name); set(false); }),
            }, out _);
            return value;
        }

        void DrawSettingsPreview(Rect rect)
        {
            if (!_resourceLimits.TrySave(out var limits, out var limitError))
            {
                UiText.StatusLabel(rect, limitError, UiTheme.Bad);
                return;
            }
            _s.Limits = limits;
            string dnsError;
            if (!DnsForm.TrySave(_s.DnsOverride, _dnsServers, out dnsError))
            {
                UiText.StatusLabel(rect, "DNS: " + dnsError, UiTheme.Bad);
                return;
            }
            try
            {
                string request;
                if (EditingTemplate)
                {
                    request = "{\"recipe\":true,\"template\":" + _templateDraft.ToJson(_s) + "}";
                }
                else
                {
                    request = "{\"session\":" + _s.ToJson();
                    if (!_identity.IsNew) request += ",\"existing\":" + JVal.Q(_identity.OriginalName);
                    else if (!string.IsNullOrEmpty(_templateName) && _templateSnapshot != null)
                    {
                        var baseline = new SessionInfo();
                        _templateSnapshot.ApplyTo(baseline);
                        request += ",\"template\":" + _templateSnapshot.ToJson(baseline);
                    }
                    request += "}";
                }
                _settingsPreview.Draw(rect, request, ref _previewScroll);
            }
            catch (InvalidOperationException error) { UiText.StatusLabel(rect, error.Message, UiTheme.Bad); }
        }

        void ReloadTemplate()
        {
            _templateBusy = true;
            SessionHub.Instance.Catalog.RefreshTemplates(TemplateFailed, () =>
            {
                var current = SessionHub.Instance.Templates.FirstOrDefault(t => t.Name == _templateOriginalName);
                if (current == null)
                {
                    TemplateFailed("Template no longer exists. Your draft has been kept.");
                    return;
                }
                _templateDraft = current.Copy();
                ApplyTemplate(_templateDraft);
                _s.Name = current.Name;
                _templateBusy = false;
            });
        }

        void TemplateFailed(string error)
        {
            _templateBusy = false;
            UiLayout.Fail(error);
        }
    }
}
