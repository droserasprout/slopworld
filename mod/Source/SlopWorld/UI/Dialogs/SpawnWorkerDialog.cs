using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Starts a task-owned worker from an agent template.
    // Caller and project fields provide context. The template supplies the child's settings.
    public sealed class SpawnWorkerDialog : UiWindow
    {
        static readonly Dictionary<string, (string Worktree, bool NewWorktree)> WorktreeChoices =
            new Dictionary<string, (string Worktree, bool NewWorktree)>();
        string ChoiceKey => DaemonClient.BaseUrl + "\n" + _project;
        readonly string _fixedCaller;
        readonly string _project;
        readonly List<SessionInfo> _agents;
        string _caller;
        string _template;
        string _body = "";
        string _error;
        bool _durable = true;
        bool _sending;
        List<Wire.Worktree> _worktrees = new List<Wire.Worktree>();
        string _worktree = "";
        bool _newWorktree = true;
        string _baseRevision = "";
        string _worktreeName = "";
        string _baseCommit = "";
        string _previewKey = "";
        string _previewError;

        public SpawnWorkerDialog(string caller, string project)
        {
            SessionHub.Instance.RefreshConfig(UiLayout.Fail);
            _fixedCaller = caller;
            _project = project ?? "";
            if (WorktreeChoices.TryGetValue(ChoiceKey, out var choice)) { _worktree = choice.Worktree; _newWorktree = choice.NewWorktree; }
            _caller = string.IsNullOrEmpty(caller) ? TaskInfo.Host : caller;
            _agents = SessionHub.Instance.Sessions
                .Where(s => s != null && !s.Host && s.Project == _project &&
                    !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _template = AvailableTemplates().FirstOrDefault()?.Name ?? "";
            DaemonClient.Get<Wire.WorktreesReply>(WireProtocol.Routes.Worktrees + "?project=" + Uri.EscapeDataString(_project),
                reply =>
                {
                    _worktrees = reply.Worktrees.ToList();
                    if (!_newWorktree && !_worktrees.Any(w => w.Id == _worktree && w.Phase == "ready"))
                        _worktree = "main";
                }, error => _error = error, TaskInfo.Host, 60000);
        }

        public override Vector2 InitialSize => new Vector2(680f, 740f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), "Spawn worker");

            float y = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            var callerRect = new Rect(rect.x, y, rect.width,
                UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            var callerOptions = _agents.Select(agent => new SelectorOption(AgentLabel(agent), () =>
            {
                SelectCaller(agent.Name);
            })).ToList();
            callerOptions.Insert(0, new SelectorOption("You (host)", () => SelectCaller(TaskInfo.Host)));
            bool callerCanChoose = string.IsNullOrEmpty(_fixedCaller) && callerOptions.Count > 0;
            UiControls.Select(callerRect, "Caller", AgentLabel(_caller),
                callerOptions, out _, callerCanChoose
                    ? "Choose yourself or an agent to own the task and worker."
                    : "The selected caller owns the task and worker.",
                callerCanChoose);

            y = callerRect.yMax + UiTheme.GapS;
            GUI.color = UiTheme.Name;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH),
                $"Project: {_project}");
            GUI.color = Color.white;

            y += UiTheme.LineH + UiTheme.GapS;
            var templateRect = new Rect(rect.x, y, rect.width,
                UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            if (!AvailableTemplates().Any(t => t.Name == _template)) _template = "";
            var templateOptions = AvailableTemplates().Select(template =>
                new SelectorOption(template.DisplayLabel, () => _template = template.Name)).ToList();
            bool templateCanChoose = templateOptions.Count > 0;
            UiControls.Select(templateRect, "Template", TemplateLabel(_template),
                templateOptions, out _, templateCanChoose
                    ? "Choose a template allowed for this caller. Manage worker access in Settings > Agents > Workers."
                    : "No agent templates are available to this caller.",
                templateCanChoose);

            y = templateRect.yMax + UiTheme.GapM;
            y = DrawWorktreeFields(rect, y);
            GUI.color = UiTheme.Name;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH), "Task");
            GUI.color = Color.white;
            y += UiTheme.LineH + UiTheme.GapXS;

            float footerY = rect.yMax - UiTheme.BtnH;
            float errorH = string.IsNullOrEmpty(_error) ? 0f : UiTheme.RowH;
            float checkboxH = UiTheme.RowH + UiTheme.GapS;
            float areaH = Mathf.Max(60f, footerY - y - checkboxH - UiTheme.GapS - errorH);
            _body = UiText.Area(new Rect(rect.x, y, rect.width, areaH),
                "spawn-worker.task", _body, !_sending);

            y += areaH + UiTheme.GapS;
            _durable = UiControls.Checkbox(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                "Keep worker after exit", _durable,
                "Keep this worker as a stopped session after its process exits.");

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(new Rect(rect.x, y + checkboxH, rect.width, errorH), _error);
                GUI.color = Color.white;
            }

            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost, !_sending)) Close();

            bool ready = !_sending && (!_newWorktree || !string.IsNullOrEmpty(_baseCommit)) && !string.IsNullOrEmpty(_caller) &&
                !string.IsNullOrEmpty(_project) && !string.IsNullOrEmpty(_template) &&
                !string.IsNullOrWhiteSpace(_body);
            if (foot.Right("Spawn", UiTheme.Btn.Primary, ready)) Send();
        }

        void SelectCaller(string caller)
        {
            _caller = caller;
            if (!AvailableTemplates().Any(t => t.Name == _template)) _template = "";
        }

        float DrawWorktreeFields(Rect rect, float y)
        {
            var worktreeRect = new Rect(rect.x, y, rect.width, UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            var choices = new List<SelectorOption> { new SelectorOption("New worktree", () => { _newWorktree = true; _worktree = ""; }) };
            choices.AddRange(_worktrees.Where(w => w.Phase == "ready").Select(w => new SelectorOption(
                w.Name + (string.IsNullOrEmpty(w.Branch) ? " (detached)" : " — " + w.Branch),
                () => { _newWorktree = false; _worktree = w.Id; })));
            UiControls.Select(worktreeRect, "Worktree", _newWorktree ? "New worktree" :
                _worktrees.FirstOrDefault(w => w.Id == _worktree)?.Name ?? "Main checkout", choices, out _);
            y = worktreeRect.yMax + UiTheme.GapS;
            if (_newWorktree)
            {
                UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH), "Base revision (blank uses caller's HEAD)");
                y += UiTheme.LineH;
                _baseRevision = UiText.Field(new Rect(rect.x, y, rect.width, UiTheme.CompactH), "spawn-worker.base", _baseRevision);
                y += UiTheme.CompactH + UiTheme.GapS;
                UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH), "Worktree name (optional)");
                y += UiTheme.LineH;
                _worktreeName = UiText.Field(new Rect(rect.x, y, rect.width, UiTheme.CompactH), "spawn-worker.worktree-name", _worktreeName);
                y += UiTheme.CompactH + UiTheme.GapS;
                UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH),
                    "The new worktree uses committed files. Uncommitted changes stay in the caller's checkout.");
                y += UiTheme.LineH + UiTheme.GapS;
                string previewKey = _caller + "\n" + _baseRevision;
                if (previewKey != _previewKey) ResolveBase();
                if (UiButtons.Button(new Rect(rect.x, y, 130f, UiTheme.BtnH), "Resolve base", on: !_sending)) ResolveBase();
                UiText.RowLabel(new Rect(rect.x + 140f, y, rect.width - 140f, UiTheme.BtnH),
                    _previewError ?? (string.IsNullOrEmpty(_baseCommit)
                        ? "Resolve the base revision before you start the worker."
                        : _baseCommit));
                y += UiTheme.BtnH + UiTheme.GapS;
            }
            return y;
        }

        IEnumerable<AgentTemplateInfo> AvailableTemplates()
        {
            return SessionHub.Instance.Templates
                .Where(template => template != null && (_caller == TaskInfo.Host ||
                    SessionHub.Instance.Config.WorkerTemplates.Contains(template.Name)))
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase);
        }

        string TemplateLabel(string name)
        {
            var template = AvailableTemplates().FirstOrDefault(t => t.Name == name);
            return template?.DisplayLabel ??
                (string.IsNullOrEmpty(name) ? "Select a template" : name);
        }

        string AgentLabel(string name)
        {
            if (name == TaskInfo.Host) return "You (host)";
            var agent = _agents.FirstOrDefault(s => s.Name == name);
            if (agent == null)
                return string.IsNullOrEmpty(name) ? "Select an agent" : name;
            return AgentLabel(agent);
        }

        static string AgentLabel(SessionInfo agent) =>
            agent == null ? "Select an agent" : $"{agent.Name}  -  {agent.Project}";

        void ResolveBase()
        {
            string key = _caller + "\n" + _baseRevision;
            _previewKey = key;
            _baseCommit = "";
            _previewError = null;
            DaemonClient.Post<Wire.WorktreeBase>(WireProtocol.Routes.WorktreePreview,
                new Wire.CreateWorktreeReq { Project = _project, Base = _baseRevision },
                reply => { if (_caller + "\n" + _baseRevision == key) _baseCommit = reply.Commit; },
                error => { if (_caller + "\n" + _baseRevision == key) _previewError = error; }, _caller);
        }

        void Send()
        {
            if (_sending) return;
            if (!AvailableTemplates().Any(t => t.Name == _template))
            {
                _template = "";
                _error = "Choose a template allowed for this caller.";
                return;
            }
            _sending = true;
            _error = null;
            SessionHub.Instance.SpawnWorker(_caller, _project, _template, _body.Trim(), _durable,
                worker =>
                {
                    WorktreeChoices[ChoiceKey] = (_worktree, _newWorktree);
                    TerminalWindow.Open(worker);
                    Close();
                },
                error =>
                {
                    _sending = false;
                    _error = error;
                }, new WorkerWorktreeOptions
                {
                    Worktree = _worktree,
                    NewWorktree = _newWorktree,
                    BaseRevision = _baseCommit,
                    WorktreeName = _worktreeName,
                });
        }
    }
}
