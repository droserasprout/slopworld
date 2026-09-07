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
                .Where(s => s != null && !s.Host && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override Vector2 InitialSize => new Vector2(560f, 360f);

        protected override void DoBody(Rect rect)
        {
            UiWidgets.Title(TitleRect(rect), string.IsNullOrEmpty(_to)
                ? "Delegate task" : $"Delegate task to '{_to}'");

            float y = rect.y + UiWidgets.HeaderH + UiWidgets.GapM;
            var targetRect = new Rect(rect.x, y, rect.width,
                UiWidgets.LineH + UiWidgets.GapXS + UiWidgets.CompactH);
            var options = _agents.Select(agent => new SelectorOption(AgentLabel(agent), () =>
            {
                _to = agent.Name;
            })).ToList();
            bool canChoose = options.Count > 0;
            string shown = AgentLabel(_to);
            UiWidgets.Select(targetRect, "Agent", shown, options, out _,
                canChoose ? "Choose the mailbox recipient." : "No agents are available.",
                canChoose);

            y = targetRect.yMax + UiWidgets.GapM;
            GUI.color = UiWidgets.Name;
            UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, UiWidgets.LineH), "Task");
            GUI.color = Color.white;
            y += UiWidgets.LineH + UiWidgets.GapXS;

            float footerY = rect.yMax - UiWidgets.BtnH;
            float errorH = string.IsNullOrEmpty(_error) ? 0f : UiWidgets.RowH;
            float areaH = Mathf.Max(72f, footerY - y - UiWidgets.GapS - errorH);
            _body = UiWidgets.Area(new Rect(rect.x, y, rect.width, areaH),
                "delegate.task", _body, !_sending);

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = UiWidgets.Bad;
                UiWidgets.RowLabel(new Rect(rect.x, y + areaH + UiWidgets.GapXS,
                    rect.width, errorH), _error);
                GUI.color = Color.white;
            }

            var foot = new UiWidgets.Bar(UiWidgets.FooterBar(rect));
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost, !_sending)) Close();

            bool ready = canChoose && !string.IsNullOrEmpty(_to) &&
                !string.IsNullOrWhiteSpace(_body) && !_sending;
            if (foot.Right("Delegate", UiWidgets.Btn.Primary, ready)) Send();
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
