using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Dialog for sending a durable task to an agent.
    public sealed class DelegateTaskDialog : UiWindow
    {
        readonly List<SessionInfo> _agents;
        string _to;
        string _body = "";
        string _error;
        bool _sending;

        public DelegateTaskDialog(string recipient)
        {
            _to = recipient ?? "";
            _agents = SessionHub.Instance.Sessions
                .Where(s => Eligible(s))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override Vector2 InitialSize => new Vector2(560f, 360f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), string.IsNullOrEmpty(_to)
                ? "Delegate task" : $"Delegate task to '{_to}'");

            float y = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            var targetRect = new Rect(rect.x, y, rect.width,
                UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            var options = _agents.Select(agent => new SelectorOption(AgentLabel(agent), () =>
            {
                _to = agent.Name;
            })).ToList();
            bool canChoose = options.Count > 0;
            string shown = AgentLabel(_to);
            UiControls.Select(targetRect, "Agent", shown, options, out _,
                canChoose ? "Choose the mailbox recipient." : "No agents are available.",
                canChoose);

            y = targetRect.yMax + UiTheme.GapM;
            GUI.color = UiTheme.Name;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.LineH), "Task");
            GUI.color = Color.white;
            y += UiTheme.LineH + UiTheme.GapXS;

            float footerY = rect.yMax - UiTheme.BtnH;
            float errorH = string.IsNullOrEmpty(_error) ? 0f : UiTheme.RowH;
            float areaH = Mathf.Max(72f, footerY - y - UiTheme.GapS - errorH);
            _body = UiText.Area(new Rect(rect.x, y, rect.width, areaH),
                "delegate.task", _body, !_sending);

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(new Rect(rect.x, y + areaH + UiTheme.GapXS,
                    rect.width, errorH), _error);
                GUI.color = Color.white;
            }

            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost, !_sending)) Close();

            bool ready = Eligible(SessionHub.Instance.Get(_to)) &&
                !string.IsNullOrWhiteSpace(_body) && !_sending;
            if (foot.Right("Delegate", UiTheme.Btn.Primary, ready)) Send();
        }

        string AgentLabel(string name)
        {
            var agent = _agents.FirstOrDefault(s => s.Name == name);
            return AgentLabel(agent) ?? (string.IsNullOrEmpty(name) ? "Select an agent" : name);
        }

        static string AgentLabel(SessionInfo agent)
        {
            if (agent == null) return null;
            string project = string.IsNullOrEmpty(agent.Project) ? "no project" : agent.Project;
            return $"{agent.Name}  -  {project}";
        }

        internal static bool Eligible(SessionInfo agent) =>
            agent != null && !agent.Host && !agent.Ephemeral && !string.IsNullOrEmpty(agent.Name);

        void Send()
        {
            if (_sending) return;
            if (!Eligible(SessionHub.Instance.Get(_to)))
            {
                _error = "Choose an available, non-ephemeral agent.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_body)) return;
            _sending = true;
            _error = null;
            SessionHub.Instance.TaskStore.Create(_to, _body.Trim(), _ => Close(), error =>
            {
                _sending = false;
                _error = error;
            });
        }
    }
}
