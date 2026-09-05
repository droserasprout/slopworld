using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The host's durable task board, kept in the sidebar beside the sessions and Library.
    // Rows are deliberately compact: the full body and actions live in TaskDetailView.
    public static partial class TasksView
    {
        const float Pad = UiWidgets.GapS;
        const float CellX = UiWidgets.GapS;
        static float HeaderH => UiWidgets.TinyRowH;
        static float NameH => UiWidgets.LineHOf(GameFont.Tiny);
        static float NoteH => UiWidgets.LineHOf(GameFont.Tiny);
        static float RowH => NameH + NoteH + UiWidgets.GapXS + 2f;

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

            float height = Pad + HeaderH + UiWidgets.GapXS + tasks.Count * RowH + Pad;
            var list = new Rect(0f, 0f, body.width -
                (height > body.height ? UiWidgets.ScrollbarW : 0f), height);
            // XInput device discovery is disproportionately expensive on some Linux/X11
            // systems. Tasks use ordinary Unity wheel packets and thumb dragging instead;
            // unlike terminal history, this compact list does not need fractional gestures.
            using (WidgetState.Save())
            using (Scroll.Scope(body, list, preciseInput: false))
            {
                float y = Pad;
                DrawHeader(new Rect(0f, y, list.width, HeaderH), tasks, allTasks.Count);
                y += HeaderH + UiWidgets.GapXS;

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
            GUI.color = UiWidgets.Faint;
            UiWidgets.RowLabel(new Rect(r.x + CellX, r.y, r.width - CellX * 2f, r.height), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Slab.Hairline(new Rect(r.x + CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                UiWidgets.Edge);
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
            float left = r.x + CellX + dot + UiWidgets.GapXS;
            string ageText = task.Age(true);
            float ageW = UiWidgets.Wide(ageText);
            var age = new Rect(r.xMax - CellX - ageW, r.y, ageW, NameH);
            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(age, ageText, TextAnchor.MiddleRight);

            string direction = task.Direction;
            string statusText = TaskInfo.StatusText(task.Status);
            float statusW = UiWidgets.Wide(statusText);
            float right = age.x - UiWidgets.GapS;
            var state = new Rect(Mathf.Max(left, right - statusW), r.y, statusW, NameH);
            GUI.color = status;
            UiWidgets.RowLabel(state, statusText, TextAnchor.MiddleRight);

            float directionW = Mathf.Max(0f, state.x - UiWidgets.GapS - left);
            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(new Rect(left, r.y, directionW, NameH), direction);

            GUI.color = UiWidgets.Dim;
            var summary = new Rect(left, r.y + NameH + UiWidgets.GapXS,
                Mathf.Max(0f, r.width - left - CellX), NoteH);
            UiWidgets.RowLabel(summary, FittedSummary(task, summary.width));

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
            if (UiWidgets.Wide(text) <= width)
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
                    if (UiWidgets.Wide(probe) <= width) low = mid;
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
            GUI.color = UiWidgets.Faint;
            UiWidgets.RowLabel(new Rect(CellX, body.y + Pad,
                body.width - CellX * 2f, RowH * 2f), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        public static Color StatusColor(DelegatedTaskStatus status)
        {
            switch (status)
            {
                case DelegatedTaskStatus.Done: return UiWidgets.Yes;
                case DelegatedTaskStatus.Failed: return UiWidgets.Bad;
                case DelegatedTaskStatus.Working: return UiWidgets.StateWorking;
                case DelegatedTaskStatus.Accepted: return UiWidgets.StateWaiting;
                default: return UiWidgets.Info;
            }
        }
    }
}
