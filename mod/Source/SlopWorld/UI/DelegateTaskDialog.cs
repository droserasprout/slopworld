using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Dialog for sending a durable task to an agent.
    public sealed class DelegateTaskDialog : SlopWindow
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
                .Where(s => s != null && !s.Host && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override Vector2 InitialSize => new Vector2(560f, 360f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, string.IsNullOrEmpty(_to)
                ? "Delegate task" : $"Delegate task to '{_to}'");

            float y = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM;
            var targetRect = new Rect(rect.x, y, rect.width,
                SlopWidgets.LineH + SlopWidgets.GapXS + SlopWidgets.CompactH);
            var options = _agents.Select(agent => new SelectorOption(AgentLabel(agent), () =>
            {
                _to = agent.Name;
            })).ToList();
            bool canChoose = options.Count > 0;
            string shown = AgentLabel(_to);
            SlopWidgets.Select(targetRect, "Agent", shown, options, out _,
                canChoose ? "Choose the mailbox recipient." : "No agents are available.",
                canChoose);

            y = targetRect.yMax + SlopWidgets.GapM;
            GUI.color = SlopWidgets.Name;
            SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, SlopWidgets.LineH), "Task");
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;

            float footerY = rect.yMax - SlopWidgets.BtnH;
            float errorH = string.IsNullOrEmpty(_error) ? 0f : SlopWidgets.RowH;
            float areaH = Mathf.Max(72f, footerY - y - SlopWidgets.GapS - errorH);
            _body = SlopWidgets.Area(new Rect(rect.x, y, rect.width, areaH),
                "delegate.task", _body, !_sending);

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(new Rect(rect.x, y + areaH + SlopWidgets.GapXS,
                    rect.width, errorH), _error);
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost, !_sending)) Close();

            bool ready = canChoose && !string.IsNullOrEmpty(_to) &&
                !string.IsNullOrWhiteSpace(_body) && !_sending;
            if (foot.Right("Delegate", SlopWidgets.Btn.Primary, ready)) Send();
        }

        string AgentLabel(string name)
        {
            var agent = _agents.FirstOrDefault(s => s.Name == name);
            return AgentLabel(agent) ?? (string.IsNullOrEmpty(name) ? "Choose an agent..." : name);
        }

        static string AgentLabel(SessionInfo agent)
        {
            if (agent == null) return null;
            string project = string.IsNullOrEmpty(agent.Project) ? "no project" : agent.Project;
            return $"{agent.Name}  -  {project}";
        }

        void Send()
        {
            _sending = true;
            _error = null;
            SessionHub.Instance.CreateTask(_to, _body.Trim(), _ => Close(), error =>
            {
                _sending = false;
                _error = error;
            });
        }
    }
}
