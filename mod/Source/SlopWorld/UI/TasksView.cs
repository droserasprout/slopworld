using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The host's durable task board, kept in the sidebar beside the sessions and shortcuts.
    // Rows are deliberately compact: the full body and actions live in TaskDetailDialog.
    public static class TasksView
    {
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        static float HeaderH => SlopWidgets.TinyRowH;
        static float NameH => SlopWidgets.LineHOf(GameFont.Tiny);
        static float NoteH => SlopWidgets.LineHOf(GameFont.Tiny);
        static float RowH => NameH + NoteH + SlopWidgets.GapXS + 2f;

        static readonly SmoothScroll Scroll = new SmoothScroll();
        static readonly List<Line> Lines = new List<Line>();
        static readonly List<TaskInfo> VisibleTasks = new List<TaskInfo>();
        static readonly Dictionary<TaskInfo, string> FittedSummaries =
            new Dictionary<TaskInfo, string>();
        static float _summaryWidth = -1f;
        static Rect _body;
        static List<TaskInfo> _taskSnapshot;
        static bool _filtersDirty = true;

        enum DirectionFilter
        {
            All,
            Incoming,
            Outgoing,
            AgentToAgent,
        }

        static DelegatedTaskStatus? _status;
        static DirectionFilter _direction;
        static string _agent = "";

        public static bool Filtering => _status.HasValue || _direction != DirectionFilter.All ||
            !string.IsNullOrEmpty(_agent);

        struct Line
        {
            public TaskInfo Task;
            public Rect Rect;
        }

        static Rect Screen(Rect r) => new Rect(_body.x + r.x,
            _body.y + r.y - Scroll.Position.y, r.width, r.height);

        public static void Draw(Rect body)
        {
            _body = body;
            Lines.Clear();

            var hub = SessionHub.Instance;
            var allTasks = hub.Tasks;
            var tasks = Filtered(allTasks);
            if (tasks.Count == 0)
            {
                Empty(body, allTasks.Count == 0
                    ? hub.Online
                        ? "No delegated tasks yet. Right-click an agent to send one."
                        : $"daemon {hub.Status}"
                    : "No tasks match the current filters.");
                return;
            }

            float height = Pad + HeaderH + SlopWidgets.GapXS + tasks.Count * RowH + Pad;
            var list = new Rect(0f, 0f, body.width -
                (height > body.height ? SlopWidgets.ScrollbarW : 0f), height);
            bool scrollable = height > body.height;
            // XInput device discovery is disproportionately expensive on some Linux/X11
            // systems. Tasks use ordinary Unity wheel packets and thumb dragging instead;
            // unlike terminal history, this compact list does not need fractional gestures.
            if (scrollable) Scroll.Begin(body, list, preciseInput: false);
            else
            {
                // Keep the same local coordinate space and clipping as SmoothScroll.Begin,
                // without sampling XInput for a list that cannot scroll.
                Scroll.JumpTo(Vector2.zero);
                GUI.BeginGroup(body);
                GUI.BeginGroup(list);
            }
            try
            {
                float y = Pad;
                DrawHeader(new Rect(0f, y, list.width, HeaderH), tasks, allTasks.Count);
                y += HeaderH + SlopWidgets.GapXS;

                // The mailbox is durable and can grow indefinitely. Keep its complete model,
                // but submit only rows intersecting the viewport to IMGUI.
                float rowsTop = y;
                int first = Mathf.Clamp(Mathf.FloorToInt(
                    (Scroll.Position.y - rowsTop) / RowH), 0, tasks.Count);
                int last = Mathf.Clamp(Mathf.CeilToInt(
                    (Scroll.Position.y + body.height - rowsTop) / RowH), first, tasks.Count);
                y += first * RowH;
                for (int i = first; i < last; i++)
                {
                    var task = tasks[i];
                    var row = new Rect(0f, y, list.width, RowH);
                    DrawTask(row, task);
                    Lines.Add(new Line { Task = task, Rect = Screen(row) });
                    y += RowH;
                }
            }
            finally
            {
                if (scrollable) Scroll.End();
                else
                {
                    GUI.EndGroup();
                    GUI.EndGroup();
                }
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        static void DrawHeader(Rect r, List<TaskInfo> tasks, int total)
        {
            int incoming = tasks.Count(t => t.Incoming && !t.Terminal);
            int sent = tasks.Count(t => t.Outgoing && !t.Terminal);
            int peer = tasks.Count(t => !t.Incoming && !t.Outgoing && !t.Terminal);
            string count = Filtering ? tasks.Count + " of " + total : total.ToString();
            string text = count + " tasks  ·  " + incoming + " incoming  ·  " +
                sent + " sent" + (peer > 0 ? "  ·  " + peer + " agent-to-agent" : "");

            Text.Font = GameFont.Tiny;
            GUI.color = SlopWidgets.Faint;
            SlopWidgets.RowLabel(new Rect(r.x + CellX, r.y, r.width - CellX * 2f, r.height), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Slab.Hairline(new Rect(r.x + CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                SlopWidgets.Edge);
        }

        static void DrawTask(Rect r, TaskInfo task)
        {
            SlopWidgets.HoverRow(r);

            Color status = StatusColor(task.Status);
            float dot = Mathf.Min(8f, r.height - 4f);
            GUI.color = status;
            GUI.DrawTexture(Icons.DotBox(new Vector2(r.x + CellX + dot / 2f,
                r.y + NameH / 2f + 1f), dot), Icons.Dot);

            Text.Font = GameFont.Tiny;
            float left = r.x + CellX + dot + SlopWidgets.GapXS;
            string ageText = task.Age(true);
            float ageW = SlopWidgets.Wide(ageText);
            var age = new Rect(r.xMax - CellX - ageW, r.y, ageW, NameH);
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(age, ageText, TextAnchor.MiddleRight);

            string direction = task.Direction;
            string statusText = TaskInfo.StatusText(task.Status);
            float statusW = SlopWidgets.Wide(statusText);
            float right = age.x - SlopWidgets.GapS;
            var state = new Rect(Mathf.Max(left, right - statusW), r.y, statusW, NameH);
            GUI.color = status;
            SlopWidgets.RowLabel(state, statusText, TextAnchor.MiddleRight);

            float directionW = Mathf.Max(0f, state.x - SlopWidgets.GapS - left);
            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(left, r.y, directionW, NameH), direction);

            GUI.color = SlopWidgets.Dim;
            var summary = new Rect(left, r.y + NameH + SlopWidgets.GapXS,
                Mathf.Max(0f, r.width - left - CellX), NoteH);
            SlopWidgets.RowLabel(summary, FittedSummary(task, summary.width));

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        static string FittedSummary(TaskInfo task, float width)
        {
            // Width changes only when the sidebar or its scrollbar changes. Cache the fitted
            // result so GenText.Truncate's linear character-removal loop sees an already fitting
            // string during every subsequent IMGUI pass.
            if (Mathf.Abs(width - _summaryWidth) > 0.5f)
            {
                _summaryWidth = width;
                FittedSummaries.Clear();
            }
            if (FittedSummaries.TryGetValue(task, out var fitted)) return fitted;

            string text = task.Summary;
            if (SlopWidgets.Wide(text) <= width)
                fitted = text;
            else
            {
                const string ellipsis = "...";
                int low = 0;
                int high = text.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    string probe = text.Substring(0, mid).TrimEnd() + ellipsis;
                    if (SlopWidgets.Wide(probe) <= width) low = mid;
                    else high = mid - 1;
                }
                fitted = text.Substring(0, low).TrimEnd() + ellipsis;
            }
            FittedSummaries[task] = fitted;
            return fitted;
        }

        static void Empty(Rect body, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = SlopWidgets.Faint;
            SlopWidgets.RowLabel(new Rect(CellX, body.y + Pad,
                body.width - CellX * 2f, RowH * 2f), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        public static Color StatusColor(DelegatedTaskStatus status)
        {
            switch (status)
            {
                case DelegatedTaskStatus.Done: return SlopWidgets.Yes;
                case DelegatedTaskStatus.Failed: return SlopWidgets.Bad;
                case DelegatedTaskStatus.Working: return SlopWidgets.StateWorking;
                case DelegatedTaskStatus.Accepted: return SlopWidgets.StateWaiting;
                default: return SlopWidgets.Info;
            }
        }

        static bool Matches(TaskInfo task)
        {
            if (_status.HasValue && task.Status != _status.Value) return false;
            switch (_direction)
            {
                case DirectionFilter.Incoming:
                    if (!task.Incoming) return false;
                    break;
                case DirectionFilter.Outgoing:
                    if (!task.Outgoing) return false;
                    break;
                case DirectionFilter.AgentToAgent:
                    if (task.Incoming || task.Outgoing) return false;
                    break;
            }
            return string.IsNullOrEmpty(_agent) || task.From == _agent || task.To == _agent;
        }

        static List<TaskInfo> Filtered(List<TaskInfo> allTasks)
        {
            if (ReferenceEquals(_taskSnapshot, allTasks) && !_filtersDirty)
                return VisibleTasks;

            _taskSnapshot = allTasks;
            _filtersDirty = false;
            VisibleTasks.Clear();
            foreach (var task in allTasks)
                if (Matches(task)) VisibleTasks.Add(task);
            return VisibleTasks;
        }

        public static void FilterButton(Rect r)
        {
            TooltipHandler.TipRegion(r, Filtering
                ? "Task filters active. Click to change them."
                : "All tasks. Click to filter by status, direction or agent.");
            if (SlopWidgets.IconButton(r, Icons.Filter,
                    Filtering ? SlopWidgets.Lead : SlopWidgets.Off))
                OpenFilterMenu();
        }

        static List<FloatMenuOption> StatusOptions()
        {
            var options = new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("All statuses", !_status.HasValue, () =>
                {
                    _status = null;
                    _filtersDirty = true;
                    OpenFilterMenu();
                }),
            };
            foreach (DelegatedTaskStatus status in Enum.GetValues(typeof(DelegatedTaskStatus)))
            {
                var chosen = status;
                options.Add(SlopWidgets.MenuToggle(TaskInfo.StatusText(chosen),
                    _status == chosen, () =>
                    {
                        _status = chosen;
                        _filtersDirty = true;
                        OpenFilterMenu();
                    }));
            }
            return options;
        }

        static List<FloatMenuOption> DirectionOptions()
        {
            return new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("All directions", _direction == DirectionFilter.All,
                    () => SetDirection(DirectionFilter.All)),
                SlopWidgets.MenuToggle("Incoming to you", _direction == DirectionFilter.Incoming,
                    () => SetDirection(DirectionFilter.Incoming)),
                SlopWidgets.MenuToggle("Sent by you", _direction == DirectionFilter.Outgoing,
                    () => SetDirection(DirectionFilter.Outgoing)),
                SlopWidgets.MenuToggle("Agent to agent", _direction == DirectionFilter.AgentToAgent,
                    () => SetDirection(DirectionFilter.AgentToAgent)),
            };
        }

        static List<FloatMenuOption> AgentOptions()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in SessionHub.Instance.Sessions)
                if (session != null && !session.Host && !string.IsNullOrEmpty(session.Name))
                    names.Add(session.Name);
            foreach (var task in SessionHub.Instance.Tasks)
            {
                if (task.From != TaskInfo.Host) names.Add(task.From);
                if (task.To != TaskInfo.Host) names.Add(task.To);
            }

            var options = new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("All agents", string.IsNullOrEmpty(_agent),
                    () => SetAgent("")),
            };
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var chosen = name;
                options.Add(SlopWidgets.MenuToggle(chosen, _agent == chosen,
                    () => SetAgent(chosen)));
            }
            return options;
        }

        static void SetDirection(DirectionFilter direction)
        {
            _direction = direction;
            _filtersDirty = true;
            OpenFilterMenu();
        }

        static void SetAgent(string agent)
        {
            _agent = agent ?? "";
            _filtersDirty = true;
            OpenFilterMenu();
        }

        static void ClearFilters()
        {
            _status = null;
            _direction = DirectionFilter.All;
            _agent = "";
            _filtersDirty = true;
            OpenFilterMenu();
        }

        public static void OpenFilterMenu()
        {
            var options = new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("All tasks", !Filtering, ClearFilters),
                new SlopSubmenu("Status", StatusOptions),
                new SlopSubmenu("Direction", DirectionOptions),
                new SlopSubmenu("Agent", AgentOptions),
            };
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;

            foreach (var line in Lines)
            {
                if (!ColonistBarStrip.MouseOver(line.Rect)) continue;
                e.Use();
                if (e.button == 1) TaskActions.OpenMenu(line.Task);
                else TaskDetailDialog.Open(line.Task);
                return;
            }
        }
    }

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
            var labels = _agents.Select(AgentLabel).ToList();
            bool canChoose = labels.Count > 0;
            string shown = AgentLabel(_to);
            if (SlopWidgets.Select(targetRect, "Agent", shown, labels, out var box,
                    canChoose ? "Choose the mailbox recipient." : "No agents are available.",
                    canChoose))
            {
                var options = _agents.Select(agent => new FloatMenuOption(AgentLabel(agent), () =>
                {
                    _to = agent.Name;
                })).ToList();
                Find.WindowStack.Add(new SlopMenu(options, SlopWidgets.MenuAt(box)));
            }

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

    public static class TaskActions
    {
        public static void OpenMenu(TaskInfo task, Action<TaskInfo> updated = null)
        {
            if (task == null) return;

            var options = new List<FloatMenuOption>();
            if (task.Incoming && !task.Terminal)
            {
                AddStatus(options, task, DelegatedTaskStatus.Accepted, updated);
                AddStatus(options, task, DelegatedTaskStatus.Working, updated);
                AddStatus(options, task, DelegatedTaskStatus.Done, updated);
                AddStatus(options, task, DelegatedTaskStatus.Failed, updated);
            }

            var counterpart = SessionHub.Instance.Get(task.Counterpart);
            if (counterpart != null)
            {
                var terminal = new FloatMenuOption("Open agent terminal", () =>
                    TerminalWindow.Open(counterpart.Name));
                terminal.Disabled = !counterpart.Alive;
                options.Add(terminal);
            }

            if (task.Terminal)
                options.Add(new FloatMenuOption("Remove", () => RemoveTask(task)));

            if (options.Count > 0)
                TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        static void AddStatus(List<FloatMenuOption> options, TaskInfo task,
                              DelegatedTaskStatus status, Action<TaskInfo> updated)
        {
            var option = new FloatMenuOption(TaskInfo.StatusText(status), () =>
                SessionHub.Instance.UpdateTask(task.Id, status, null, updated, SlopWidgets.Fail));
            option.Disabled = task.Status == status;
            options.Add(option);
        }

        public static void RemoveTask(TaskInfo task)
        {
            if (task == null || !task.Terminal) return;
            TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                $"Remove task '{task.Id}'? It will disappear for both participants.",
                () => SessionHub.Instance.RemoveTask(task.Id, null, SlopWidgets.Fail),
                destructive: true));
        }
    }

    public sealed class TaskDetailDialog : SlopWindow
    {
        TaskInfo _task;
        readonly SmoothScroll _scroll = new SmoothScroll();

        TaskDetailDialog(TaskInfo task) { _task = task; }

        public static void Open(TaskInfo task) =>
            TerminalWindow.OpenOverPane(new TaskDetailDialog(task));

        public override Vector2 InitialSize => new Vector2(620f, 500f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, "Task " + _task.Id);

            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float bottom = rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS;
            var outer = new Rect(rect.x, top, rect.width, Mathf.Max(0f, bottom - top));
            float width = Mathf.Max(1f, outer.width - SlopWidgets.ScrollbarW);
            float bodyH = MessageHeight(_task.Body, width - SlopWidgets.FieldPadX * 2f) +
                SlopWidgets.FieldPadY * 2f;
            float noteH = string.IsNullOrEmpty(_task.Note)
                ? 0f
                : SlopWidgets.GapM + MessageHeight(_task.Note,
                    width - SlopWidgets.FieldPadX * 2f) + SlopWidgets.FieldPadY * 2f;
            float contentH = SlopWidgets.TinyRowH + SlopWidgets.GapS + bodyH + noteH +
                SlopWidgets.GapS;

            _scroll.Begin(outer, new Rect(0f, 0f, width,
                Mathf.Max(outer.height, contentH)));
            try
            {
                float y = 0f;
                Text.Font = GameFont.Tiny;
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(0f, y, width, SlopWidgets.TinyRowH),
                    _task.Direction + "  ·  " + TaskInfo.StatusText(_task.Status) +
                    "  ·  " + _task.Age(true));
                GUI.color = Color.white;
                y += SlopWidgets.TinyRowH + SlopWidgets.GapS;

                DrawTextBox(new Rect(0f, y, width, bodyH), _task.Body);
                y += bodyH;
                if (!string.IsNullOrEmpty(_task.Note))
                {
                    y += SlopWidgets.GapM;
                    DrawTextBox(new Rect(0f, y, width, noteH - SlopWidgets.GapM),
                        "Latest note\n" + _task.Note);
                }
            }
            finally
            {
                _scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (_task.Incoming && !_task.Terminal &&
                foot.Left("Status", SlopWidgets.Btn.Default))
                TaskActions.OpenMenu(_task, updated => _task = updated);
            else if (_task.Terminal && foot.Left("Remove", SlopWidgets.Btn.Danger))
                TaskActions.RemoveTask(_task);

            if (foot.Right("Close", SlopWidgets.Btn.Ghost)) Close();
        }

        static void DrawTextBox(Rect r, string text)
        {
            Slab.Box(r, SlopWidgets.Well, SlopWidgets.Edge);
            var inner = r.ContractedBy(SlopWidgets.FieldPadX, SlopWidgets.FieldPadY);
            var wrap = Text.WordWrap;
            var anchor = Text.Anchor;
            var font = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = SlopWidgets.Lead;
                Widgets.Label(inner, text ?? "");
            }
            finally
            {
                GUI.color = Color.white;
                Text.WordWrap = wrap;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }
    }
}
