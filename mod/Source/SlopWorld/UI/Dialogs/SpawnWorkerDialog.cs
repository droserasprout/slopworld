using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Starts a task-owned worker from an allowlisted template. The caller/project fields are
    // context only; no existing agent configuration is copied into the child.
    public sealed class SpawnWorkerDialog : UiWindow
    {
        readonly string _fixedCaller;
        readonly string _project;
        readonly List<SessionInfo> _agents;
        string _caller;
        string _template;
        string _body = "";
        string _error;
        bool _durable;
        bool _sending;

        public SpawnWorkerDialog(string caller, string project)
        {
            _fixedCaller = caller;
            _project = project ?? "";
            _caller = string.IsNullOrEmpty(caller) ? TaskInfo.Host : caller;
            _agents = SessionHub.Instance.Sessions
                .Where(s => s != null && !s.Host && s.Project == _project &&
                    !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _template = AvailableTemplates().FirstOrDefault()?.Name ?? "";
        }

        public override Vector2 InitialSize => new Vector2(620f, 430f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), "Spawn worker");

            float y = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            var callerRect = new Rect(rect.x, y, rect.width,
                UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            var callerOptions = _agents.Select(agent => new SelectorOption(AgentLabel(agent), () =>
            {
                _caller = agent.Name;
            })).ToList();
            callerOptions.Insert(0, new SelectorOption("You (host)", () => _caller = TaskInfo.Host));
            bool callerCanChoose = string.IsNullOrEmpty(_fixedCaller) && callerOptions.Count > 0;
            UiControls.Select(callerRect, "Caller", AgentLabel(_caller),
                callerOptions, out _, callerCanChoose
                    ? "Choose yourself or an agent to own the task and worker relationship."
                    : "The selected agent owns the task and worker relationship.",
                callerCanChoose);

            y = callerRect.yMax + UiTheme.GapS;
            GUI.color = UiTheme.Name;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH),
                $"Project: {_project}");
            GUI.color = Color.white;

            y += UiTheme.LineH + UiTheme.GapS;
            var templateRect = new Rect(rect.x, y, rect.width,
                UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            var templateOptions = AvailableTemplates().Select(template =>
                new SelectorOption(template.DisplayLabel, () => _template = template.Name)).ToList();
            bool templateCanChoose = templateOptions.Count > 0;
            UiControls.Select(templateRect, "Template", TemplateLabel(_template),
                templateOptions, out _, templateCanChoose
                    ? "All agent templates are available to you. Settings > Workers controls agents."
                    : "No agent templates are available in the catalog.",
                templateCanChoose);

            y = templateRect.yMax + UiTheme.GapM;
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
                "Durable workers remain as stopped sessions after their task finishes.");

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(new Rect(rect.x, y + checkboxH, rect.width, errorH), _error);
                GUI.color = Color.white;
            }

            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost, !_sending)) Close();

            bool ready = !_sending && !string.IsNullOrEmpty(_caller) &&
                !string.IsNullOrEmpty(_project) && !string.IsNullOrEmpty(_template) &&
                !string.IsNullOrWhiteSpace(_body);
            if (foot.Right("Spawn", UiTheme.Btn.Primary, ready)) Send();
        }

        IEnumerable<AgentTemplateInfo> AvailableTemplates()
        {
            return SessionHub.Instance.Templates
                .Where(template => template != null)
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase);
        }

        string TemplateLabel(string name)
        {
            var template = AvailableTemplates().FirstOrDefault(t => t.Name == name);
            return template?.DisplayLabel ??
                (string.IsNullOrEmpty(name) ? "Choose a template..." : name);
        }

        string AgentLabel(string name)
        {
            if (name == TaskInfo.Host) return "You (host)";
            var agent = _agents.FirstOrDefault(s => s.Name == name);
            if (agent == null)
                return string.IsNullOrEmpty(name) ? "Choose an agent..." : name;
            return AgentLabel(agent);
        }

        static string AgentLabel(SessionInfo agent) =>
            agent == null ? "Choose an agent..." : $"{agent.Name}  -  {agent.Project}";

        void Send()
        {
            _sending = true;
            _error = null;
            SessionHub.Instance.SpawnWorker(_caller, _project, _template, _body.Trim(), _durable,
                worker =>
                {
                    TerminalWindow.Open(worker);
                    Close();
                },
                error =>
                {
                    _sending = false;
                    _error = error;
                });
        }
    }
}
