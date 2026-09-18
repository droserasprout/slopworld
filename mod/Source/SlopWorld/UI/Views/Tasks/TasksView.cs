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
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;
        static float HeaderH => UiTheme.TinyRowH;
        static float NameH => UiTheme.LineHOf(GameFont.Tiny);
        static float NoteH => UiTheme.LineHOf(GameFont.Tiny);
        static float RowH => NameH + NoteH + UiTheme.GapXS + 2f;

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

        static float _contentHeight = -1f;

        public static void Draw(Rect body)
        {
            if (Scroll.HandleWheel(body, _contentHeight)) return;
            _body = body;
            Lines.Clear();

            var hub = SessionHub.Instance;
            var allTasks = hub.Tasks;
            if (!SmoothScroll.WheelOnly) PruneSelection(allTasks);
            var tasks = Filtered(allTasks);
            if (tasks.Count == 0)
            {
                _contentHeight = 0f;
                Empty(body, allTasks.Count == 0
                    ? hub.Online
                        ? "No delegated tasks yet. Right-click an agent to send one."
                        : $"daemon {hub.Status}"
                    : "No tasks match the current filters.");
                return;
            }

            float height = _contentHeight = Pad + HeaderH + UiTheme.GapXS + tasks.Count * RowH + Pad;
            var geometry = UiScrollBody.Measure(body, height,
                UiScrollbarReservation.WhenNeeded);
            var list = geometry.View;
            using (WidgetState.Save())
            using (Scroll.Scope(body, list))
            {
                if (SmoothScroll.WheelOnly) return;
                float y = Pad;
                DrawHeader(new Rect(0f, y, list.width, HeaderH), tasks, allTasks.Count);
                y += HeaderH + UiTheme.GapXS;

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
            GUI.color = UiTheme.Faint;
            UiText.RowLabel(new Rect(r.x + CellX, r.y, r.width - CellX * 2f, r.height), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Slab.Hairline(new Rect(r.x + CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                UiTheme.Edge);
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
            float left = r.x + CellX + dot + UiTheme.GapXS;
            string ageText = task.Age(true);
            float ageW = UiTheme.Wide(ageText);
            var age = new Rect(r.xMax - CellX - ageW, r.y, ageW, NameH);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(age, ageText, TextAnchor.MiddleRight);

            string direction = task.Direction;
            string statusText = TaskInfo.StatusText(task.Status);
            float statusW = UiTheme.Wide(statusText);
            float right = age.x - UiTheme.GapS;
            var state = new Rect(Mathf.Max(left, right - statusW), r.y, statusW, NameH);
            GUI.color = status;
            UiText.RowLabel(state, statusText, TextAnchor.MiddleRight);

            float directionW = Mathf.Max(0f, state.x - UiTheme.GapS - left);
            GUI.color = UiTheme.Lead;
            UiText.RowLabel(new Rect(left, r.y, directionW, NameH), direction);

            GUI.color = UiTheme.Dim;
            var summary = new Rect(left, r.y + NameH + UiTheme.GapXS,
                Mathf.Max(0f, r.width - left - CellX), NoteH);
            UiText.RowLabel(summary, FittedSummary(task, summary.width));

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
            if (UiTheme.Wide(text) <= width)
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
                    if (UiTheme.Wide(probe) <= width) low = mid;
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
            GUI.color = UiTheme.Faint;
            UiText.RowLabel(new Rect(CellX, body.y + Pad,
                body.width - CellX * 2f, RowH * 2f), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        public static Color StatusColor(DelegatedTaskStatus status)
        {
            switch (status)
            {
                case DelegatedTaskStatus.Done: return UiTheme.Yes;
                case DelegatedTaskStatus.Failed: return UiTheme.Bad;
                case DelegatedTaskStatus.Canceled: return UiTheme.Dim;
                case DelegatedTaskStatus.Working: return UiTheme.StateWorking;
                case DelegatedTaskStatus.Accepted: return UiTheme.StateWaiting;
                default: return UiTheme.Info;
            }
        }

        public static bool FocusLocation(string id) => TaskDetailView.FocusLocation(id);
    }
}
