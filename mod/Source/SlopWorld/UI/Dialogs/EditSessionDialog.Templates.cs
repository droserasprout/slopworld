using System.Linq;

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
