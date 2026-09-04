using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The host's durable task board, kept in the sidebar beside the sessions and Library.
    // Rows are deliberately compact: the full body and actions live in TaskDetailView.
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
        static readonly HashSet<string> SelectedIds = new HashSet<string>();
        static readonly Dictionary<TaskInfo, string> FittedSummaries =
            new Dictionary<TaskInfo, string>();
        static float _summaryWidth = -1f;
        static Rect _body;
        static List<TaskInfo> _taskSnapshot;
        static bool _filtersDirty = true;
        static string _selectionAnchorId;

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
            PruneSelection(allTasks);
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
                sent + " sent" + (peer > 0 ? "  ·  " + peer + " agent-to-agent" : "") +
                (SelectedIds.Count > 0 ? "  ·  " + SelectedIds.Count + " selected" : "");

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
            RowChrome.Hover(r, IsSelected(task), true, RowHoverPolicy.OverlayAware);

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
                if (e.button == 1)
                {
                    if (!IsSelected(line.Task)) SelectOnly(line.Task);
                    SetAnchor(line.Task);
                    TaskActions.OpenMenu(line.Task);
                }
                else if (e.shift)
                {
                    SelectRange(line.Task, e.control);
                }
                else if (e.control)
                {
                    Toggle(line.Task);
                    SetAnchor(line.Task);
                }
                else
                {
                    SelectOnly(line.Task);
                    SetAnchor(line.Task);
                    TaskDetailView.Open(line.Task);
                }
                return;
            }
        }

        static bool IsSelected(TaskInfo task) => task != null &&
            !string.IsNullOrEmpty(task.Id) && SelectedIds.Contains(task.Id);

        internal static List<TaskInfo> SelectionFor(TaskInfo fallback)
        {
            var selected = SessionHub.Instance.Tasks.Where(IsSelected).ToList();
            if (fallback != null && selected.Any(task => task.Id == fallback.Id))
                return selected;
            return fallback == null ? new List<TaskInfo>() : new List<TaskInfo> { fallback };
        }

        static void SelectOnly(TaskInfo task)
        {
            SelectedIds.Clear();
            if (task != null && !string.IsNullOrEmpty(task.Id)) SelectedIds.Add(task.Id);
        }

        static void SetAnchor(TaskInfo task)
        {
            _selectionAnchorId = task == null || string.IsNullOrEmpty(task.Id)
                ? null : task.Id;
        }

        static void Toggle(TaskInfo task)
        {
            if (task == null || string.IsNullOrEmpty(task.Id)) return;
            if (!SelectedIds.Add(task.Id)) SelectedIds.Remove(task.Id);
        }

        static void SelectRange(TaskInfo task, bool extend)
        {
            int anchor = VisibleTasks.FindIndex(candidate =>
                candidate != null && candidate.Id == _selectionAnchorId);
            int current = VisibleTasks.FindIndex(candidate =>
                candidate != null && task != null && candidate.Id == task.Id);
            if (anchor < 0 || current < 0)
            {
                SelectOnly(task);
                SetAnchor(task);
                return;
            }

            if (!extend) SelectedIds.Clear();
            int first = Mathf.Min(anchor, current);
            int last = Mathf.Max(anchor, current);
            for (int i = first; i <= last; i++)
            {
                var candidate = VisibleTasks[i];
                if (candidate != null && !string.IsNullOrEmpty(candidate.Id))
                    SelectedIds.Add(candidate.Id);
            }
        }

        static void PruneSelection(List<TaskInfo> tasks)
        {
            var live = new HashSet<string>(tasks.Where(task => task != null)
                .Select(task => task.Id));
            SelectedIds.RemoveWhere(id => !live.Contains(id));
            if (_selectionAnchorId != null && !live.Contains(_selectionAnchorId))
                _selectionAnchorId = null;
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

            var selected = TasksView.SelectionFor(task);
            var removable = selected.Where(candidate => candidate != null && candidate.Terminal)
                .ToList();

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

            if (removable.Count > 0)
            {
                string label = removable.Count == 1
                    ? "Remove"
                    : $"Remove {removable.Count} selected tasks";
                options.Add(new FloatMenuOption(label, () => RemoveTasks(removable)));
            }

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

        static void RemoveTasks(List<TaskInfo> tasks)
        {
            if (tasks == null || tasks.Count == 0) return;

            string prompt = tasks.Count == 1
                ? $"Remove task '{tasks[0].Id}'? It will disappear for both participants."
                : $"Remove {tasks.Count} selected tasks? They will disappear for both participants.";
            TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(prompt, () =>
            {
                SessionHub.Instance.RemoveTasks(tasks.Select(task => task.Id), null,
                    SlopWidgets.Fail);
            }, destructive: true));
        }
    }

    public sealed class TaskDetailView : IContentView
    {
        const float AvatarSize = 38f;
        const float AvatarOverlap = 18f;
        const float MessageTextInset = AvatarSize - AvatarOverlap + SlopWidgets.GapS;

        TaskInfo _task;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<DialogueLine> _selectionLines = new List<DialogueLine>();
        readonly List<TextRange> _bodyRanges = new List<TextRange>();
        readonly List<TextRange> _noteRanges = new List<TextRange>();
        readonly Dictionary<char, float> _smallCharWidths =
            new Dictionary<char, float>();
        TaskInfo _layoutTask;
        float _layoutWidth = -1f;
        float _layoutScale = -1f;
        int _layoutFontSize = -1;
        string _layoutFontName = "";
        float _bodyHeight;
        float _noteHeight;
        float _metricsScale = -1f;
        int _metricsFontSize = -1;
        string _metricsFontName = "";
        int _selectionStart, _selectionEnd;
        int _selectionControl;
        bool _draggingSelection;

        struct TextRange
        {
            public int Start, End;

            public TextRange(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        struct DialogueLine
        {
            public int Start, End;
            public float X, Y, Width, Height;
            public string Text;
            public float[] Edges;
        }

        public TaskDetailView(TaskInfo task) { _task = task; }

        public static void Open(TaskInfo task) =>
            TerminalWindow.OpenContent(new TaskDetailView(task));

        public string Title => "Task " + (_task?.Id ?? "");

        public void Opened() { }

        public void Closed()
        {
            _scroll.JumpTo(Vector2.zero);
            ClearSelection();
        }

        public void Draw(Rect body)
        {
            // Use the same centred band as Settings. The fullscreen chrome provides the
            // maximized reader, while the band keeps message lines from stretching across
            // a wide monitor.
            var panel = OptionsView.Band(body);
            Slab.Box(panel, SlopWidgets.WindowBg, SlopWidgets.Edge);
            var rect = panel.ContractedBy(SlopWidgets.GapM);
            SlopWidgets.Title(rect, Title);

            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float bottom = rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS;
            var outer = new Rect(rect.x, top, rect.width, Mathf.Max(0f, bottom - top));
            float width = Mathf.Max(1f, outer.width - SlopWidgets.ScrollbarW);
            string bodyText = _task?.Body ?? "";
            string noteText = _task?.Note ?? "";
            EnsureLayout(width, bodyText, noteText);
            float bodyH = _bodyHeight;
            float noteH = _noteHeight;
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
                    "  ·  created " + Timestamp(_task.CreatedMs) +
                    "  ·  updated " + Timestamp(_task.UpdatedMs));
                GUI.color = Color.white;
                y += SlopWidgets.TinyRowH + SlopWidgets.GapS;

                DrawMessage(new Rect(0f, y, width, bodyH), _task.From, _task.CreatedMs);
                y += bodyH;
                if (!string.IsNullOrEmpty(_task.Note))
                {
                    y += SlopWidgets.GapM;
                    DrawMessage(new Rect(0f, y, width, noteH - SlopWidgets.GapM), _task.To,
                        _task.UpdatedMs, true);
                }
                DrawSelectableText(outer.height);
            }
            finally
            {
                _scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            HandleSelectionInput(outer);

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Copy all", SlopWidgets.Btn.Ghost))
                SlopClipboard.Copy(DialogueText());

            if (_task.Incoming && !_task.Terminal &&
                foot.Left("Status", SlopWidgets.Btn.Default))
                TaskActions.OpenMenu(_task, updated => _task = updated);
            else if (_task.Terminal && foot.Left("Remove", SlopWidgets.Btn.Danger))
                TaskActions.RemoveTask(_task);

            if (foot.Right("Close", SlopWidgets.Btn.Ghost))
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
        }

        static float MessageCardHeight(int lineCount)
        {
            float headerH = SlopWidgets.LineHOf(GameFont.Tiny);
            return Mathf.Max(AvatarSize + SlopWidgets.GapS,
                SlopWidgets.FieldPadY * 2f + headerH + SlopWidgets.GapXS +
                lineCount * SlopWidgets.LineHOf(GameFont.Small));
        }

        void DrawMessage(Rect r, string sender, long timestamp,
                         bool note = false)
        {
            var card = new Rect(r.x + AvatarOverlap, r.y,
                Mathf.Max(1f, r.width - AvatarOverlap), r.height);
            Slab.Box(card, SlopWidgets.Well, SlopWidgets.Edge);

            var icon = new Rect(r.x, r.y + SlopWidgets.GapS, AvatarSize, AvatarSize);
            DrawSenderIcon(icon, sender);

            float textWidth = Mathf.Max(1f, card.width - MessageTextInset -
                SlopWidgets.FieldPadX);
            float y = card.y + SlopWidgets.FieldPadY;
            var wrap = Text.WordWrap;
            var anchor = Text.Anchor;
            var font = Text.Font;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(card.x + MessageTextInset, y, textWidth,
                    SlopWidgets.LineHOf(GameFont.Tiny)), note
                        ? "Latest note from " + SenderLabel(sender) + "  ·  " +
                            Timestamp(timestamp)
                        : "Message from " + SenderLabel(sender) + "  ·  " +
                            Timestamp(timestamp));
            }
            finally
            {
                GUI.color = Color.white;
                Text.WordWrap = wrap;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        void EnsureLayout(float width, string body, string note)
        {
            string fontName = Settings.UIFontName ?? "";
            bool fontChanged = _layoutFontSize != Settings.UIFontSize ||
                _layoutFontName != fontName || !Mathf.Approximately(_layoutScale, Prefs.UIScale);
            if (_layoutTask == _task && Mathf.Approximately(_layoutWidth, width) &&
                !fontChanged) return;

            _layoutTask = _task;
            _layoutWidth = width;
            _layoutScale = Prefs.UIScale;
            _layoutFontSize = Settings.UIFontSize;
            _layoutFontName = fontName;

            int sourceLength = body.Length + (note.Length == 0 ? 0 : note.Length + 2);
            if (_selectionStart > sourceLength || _selectionEnd > sourceLength)
                ClearSelection();

            _bodyRanges.Clear();
            _bodyRanges.AddRange(WrappedRanges(body, TextWidth(width)));
            _bodyHeight = MessageCardHeight(_bodyRanges.Count);

            _noteRanges.Clear();
            _noteHeight = 0f;
            if (note.Length > 0)
            {
                _noteRanges.AddRange(WrappedRanges(note, TextWidth(width)));
                _noteHeight = SlopWidgets.GapM + MessageCardHeight(_noteRanges.Count);
            }

            _selectionLines.Clear();
            float bodyY = SlopWidgets.TinyRowH + SlopWidgets.GapS;
            float textY = bodyY + SlopWidgets.FieldPadY +
                SlopWidgets.LineHOf(GameFont.Tiny) + SlopWidgets.GapXS;
            CollectSelectableText(body, MessageTextInset, textY, _bodyRanges, 0);
            if (note.Length > 0)
            {
                float noteY = bodyY + _bodyHeight + SlopWidgets.GapM;
                textY = noteY + SlopWidgets.FieldPadY +
                    SlopWidgets.LineHOf(GameFont.Tiny) + SlopWidgets.GapXS;
                CollectSelectableText(note, MessageTextInset, textY, _noteRanges,
                    body.Length + 2);
            }
        }

        static float TextWidth(float width)
        {
            float cardWidth = Mathf.Max(1f, width - AvatarOverlap);
            return Mathf.Max(1f, cardWidth - MessageTextInset - SlopWidgets.FieldPadX);
        }

        List<TextRange> WrappedRanges(string text, float width)
        {
            text = text ?? "";
            var ranges = new List<TextRange>();
            if (text.Length == 0)
            {
                ranges.Add(new TextRange(0, 0));
                return ranges;
            }

            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                int start = 0;
                int lastBreak = -1;
                float lineWidth = 0f;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        ranges.Add(new TextRange(start, i));
                        start = i + 1;
                        lastBreak = -1;
                        lineWidth = 0f;
                        continue;
                    }

                    float charWidth = SmallCharWidth(text[i]);
                    if (lineWidth + charWidth <= width || i == start)
                    {
                        lineWidth += charWidth;
                        if (char.IsWhiteSpace(text[i])) lastBreak = i + 1;
                        continue;
                    }

                    int split = lastBreak > start ? lastBreak : i;
                    if (split <= start) split = Mathf.Min(start + 1, text.Length);
                    ranges.Add(new TextRange(start, split));
                    start = split;
                    lastBreak = -1;
                    lineWidth = 0f;
                    i = start - 1;
                }

                if (start <= text.Length) ranges.Add(new TextRange(start, text.Length));
                return ranges;
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        float SmallCharWidth(char value)
        {
            float scale = Prefs.UIScale;
            string fontName = Settings.UIFontName ?? "";
            if (!Mathf.Approximately(_metricsScale, scale) ||
                _metricsFontSize != Settings.UIFontSize || _metricsFontName != fontName)
            {
                _metricsScale = scale;
                _metricsFontSize = Settings.UIFontSize;
                _metricsFontName = fontName;
                _smallCharWidths.Clear();
            }

            if (_smallCharWidths.TryGetValue(value, out var width)) return width;
            width = Text.CalcSize(value.ToString()).x;
            _smallCharWidths[value] = width;
            return width;
        }

        void CollectSelectableText(string text, float x, float y,
                                   List<TextRange> ranges, int sourceOffset)
        {
            text = text ?? "";
            float lineH = SlopWidgets.LineHOf(GameFont.Small);
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                float lineY = y;
                foreach (var range in ranges)
                {
                    string lineText = text.Substring(range.Start, range.End - range.Start);
                    var edges = new float[lineText.Length + 1];
                    for (int i = 1; i < edges.Length; i++)
                        edges[i] = edges[i - 1] + SmallCharWidth(lineText[i - 1]);

                    _selectionLines.Add(new DialogueLine
                    {
                        Start = sourceOffset + range.Start,
                        End = sourceOffset + range.End,
                        X = x,
                        Y = lineY,
                        Width = edges[edges.Length - 1],
                        Height = lineH,
                        Text = lineText,
                        Edges = edges,
                    });
                    lineY += lineH;
                }
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        void DrawSelectableText(float viewportHeight)
        {
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            var wasAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;
                DrawSelectionHighlights(viewportHeight);
                GUI.color = SlopWidgets.Lead;
                int first = FirstVisibleSelectionLine(_scroll.Position.y);
                float bottom = _scroll.Position.y + viewportHeight;
                for (int i = first; i < _selectionLines.Count; i++)
                {
                    var line = _selectionLines[i];
                    if (line.Y >= bottom) break;
                    Widgets.Label(new Rect(line.X, line.Y, line.Width, line.Height), line.Text);
                }
            }
            finally
            {
                GUI.color = Color.white;
                Text.Anchor = wasAnchor;
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        int FirstVisibleSelectionLine(float top)
        {
            int low = 0;
            int high = _selectionLines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var line = _selectionLines[middle];
                if (line.Y + line.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        void DrawSelectionHighlights(float viewportHeight)
        {
            if (_selectionStart == _selectionEnd) return;
            int first = Mathf.Min(_selectionStart, _selectionEnd);
            int last = Mathf.Max(_selectionStart, _selectionEnd);
            int firstLine = FirstVisibleSelectionLine(_scroll.Position.y);
            float bottom = _scroll.Position.y + viewportHeight;
            for (int i = firstLine; i < _selectionLines.Count; i++)
            {
                var line = _selectionLines[i];
                if (line.Y >= bottom) break;
                int start = Mathf.Max(first, line.Start);
                int end = Mathf.Min(last, line.End);
                if (end <= start) continue;

                int from = Mathf.Clamp(start - line.Start, 0, line.Edges.Length - 1);
                int to = Mathf.Clamp(end - line.Start, from, line.Edges.Length - 1);
                Slab.Fill(new Rect(line.X + line.Edges[from], line.Y,
                    Mathf.Max(1f, line.Edges[to] - line.Edges[from]), line.Height),
                    SlopWidgets.Sel);
            }
        }

        void HandleSelectionInput(Rect viewport)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown ||
                (e.type == EventType.Used && e.rawType == EventType.KeyDown))
            {
                if (e.control && !e.alt)
                {
                    if (e.keyCode == KeyCode.C)
                    {
                        CopySelection();
                        e.Use();
                        return;
                    }
                    if (e.keyCode == KeyCode.A)
                    {
                        SelectAll();
                        e.Use();
                        return;
                    }
                }
                return;
            }

            EventType type = e.type == EventType.Used ? e.rawType : e.type;
            if (e.button == 1 && type == EventType.MouseDown &&
                viewport.Contains(e.mousePosition))
            {
                OpenSelectionMenu();
                e.Use();
                return;
            }

            if (e.button != 0) return;
            if (type == EventType.MouseDown && viewport.Contains(e.mousePosition))
            {
                int point = SelectionPointAt(viewport, e.mousePosition);
                if (!e.shift) _selectionStart = point;
                _selectionEnd = point;
                _draggingSelection = true;
                CaptureSelection(viewport);
                e.Use();
            }
            else if (type == EventType.MouseDrag && _draggingSelection)
            {
                _selectionEnd = SelectionPointAt(viewport, e.mousePosition);
                e.Use();
            }
            else if (type == EventType.MouseUp && _draggingSelection)
            {
                _selectionEnd = SelectionPointAt(viewport, e.mousePosition);
                _draggingSelection = false;
                ReleaseSelection();
                e.Use();
            }
        }

        int SelectionPointAt(Rect viewport, Vector2 mouse)
        {
            if (_selectionLines.Count == 0) return 0;

            float y = mouse.y - viewport.y + _scroll.Position.y;
            float x = mouse.x - viewport.x + _scroll.Position.x;
            int insertion = FirstVisibleSelectionLine(y);
            int first = Mathf.Max(0, insertion - 1);
            int last = Mathf.Min(_selectionLines.Count - 1, insertion);
            int lineIndex = first;
            float best = float.MaxValue;
            for (int i = first; i <= last; i++)
            {
                var line = _selectionLines[i];
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Width;
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                float distance = vertical * 10000f + horizontal;
                if (distance < best)
                {
                    best = distance;
                    lineIndex = i;
                }
            }

            var selected = _selectionLines[lineIndex];
            if (x <= selected.X) return selected.Start;
            if (x >= selected.X + selected.Width) return selected.End;

            for (int i = 0; i < selected.Text.Length; i++)
            {
                float left = selected.X + selected.Edges[i];
                float right = selected.X + selected.Edges[i + 1];
                if (x < (left + right) * 0.5f)
                    return selected.Start + i;
            }
            return selected.End;
        }

        void CaptureSelection(Rect viewport)
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = GUIUtility.GetControlID(FocusType.Passive, viewport);
            GUIUtility.hotControl = _selectionControl;
        }

        void ReleaseSelection()
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = 0;
        }

        void ClearSelection()
        {
            _selectionStart = 0;
            _selectionEnd = 0;
            _draggingSelection = false;
            ReleaseSelection();
        }

        void SelectAll()
        {
            _selectionStart = 0;
            _selectionEnd = SelectionLength();
            _draggingSelection = false;
            ReleaseSelection();
        }

        bool HasSelection => _selectionStart != _selectionEnd;

        void CopySelection()
        {
            if (!HasSelection) return;
            int start = Mathf.Min(_selectionStart, _selectionEnd);
            int end = Mathf.Max(_selectionStart, _selectionEnd);
            string source = SelectionSource();
            if (start < 0 || end > source.Length || end <= start) return;
            SlopClipboard.Copy(source.Substring(start, end - start));
        }

        void OpenSelectionMenu()
        {
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !HasSelection;
            options.Add(copy);
            options.Add(new FloatMenuOption("Select all", SelectAll));
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        string SelectionSource()
        {
            string body = _task.Body ?? "";
            if (string.IsNullOrEmpty(_task.Note)) return body;
            return body + "\n\n" + _task.Note;
        }

        int SelectionLength()
        {
            int body = (_task?.Body ?? "").Length;
            string note = _task?.Note;
            return body + (string.IsNullOrEmpty(note) ? 0 : note.Length + 2);
        }

        string DialogueText()
        {
            var messages = new List<string>
            {
                "Message from " + SenderLabel(_task.From) + "  ·  " +
                    Timestamp(_task.CreatedMs),
                _task.Body ?? ""
            };
            if (!string.IsNullOrEmpty(_task.Note))
            {
                messages.Add("Latest note from " + SenderLabel(_task.To) + "  ·  " +
                    Timestamp(_task.UpdatedMs));
                messages.Add(_task.Note);
            }
            return string.Join("\n\n", messages.ToArray());
        }

        static string Timestamp(long milliseconds)
        {
            if (milliseconds <= 0) return "unknown time";
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(milliseconds).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }

        static void DrawSenderIcon(Rect r, string sender)
        {
            var pawn = sender == TaskInfo.Host
                ? PlayerPawn.Current?.Pawn
                : AgentColony.Current?.PawnOf(sender);
            var portrait = Patch_SidebarPortraitDraw.PortraitFor(pawn);
            var old = GUI.color;

            Slab.Box(r, SlopWidgets.Well, SlopWidgets.Edge);
            GUI.color = Color.white;
            if (portrait != null)
                GUI.DrawTexture(r.ContractedBy(2f), portrait, ScaleMode.ScaleToFit, true);
            else
            {
                GUI.color = sender == TaskInfo.Host ? SlopWidgets.Lead : SlopWidgets.Info;
                GUI.DrawTexture(r.ContractedBy(8f),
                    sender == TaskInfo.Host ? Icons.Terminal : Icons.Agents);
            }
            GUI.color = old;

            TooltipHandler.TipRegion(r, "Message from " + SenderLabel(sender));
        }

        static string SenderLabel(string sender) =>
            sender == TaskInfo.Host ? "you" : string.IsNullOrEmpty(sender) ? "unknown" : sender;
    }
}
