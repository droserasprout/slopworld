using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using static SlopWorld.ConversationReader;

namespace SlopWorld
{
    // Full task reader shown as a WorkspaceWindow content view.
    public sealed class TaskDetailView : ContentView
    {
        readonly string _taskId;
        TaskInfo _task;
        IDisposable _pollingDemand;
        readonly ConversationReader _reader = new ConversationReader();

        public TaskDetailView(TaskInfo task)
        {
            _task = task;
            _taskId = task?.Id;
        }

        public static void Open(TaskInfo task, IContentView returnTo = null)
        {
            if (task == null)
                return;
            Sidebar.RememberTask(task.Id);
            Open(task, false, returnTo);
        }

        internal static bool FocusLocation(string id)
        {
            var task = SessionHub.Instance.Tasks.FirstOrDefault(candidate =>
                candidate != null && candidate.Id == id
            );
            if (task == null)
                return false;
            Open(task, false);
            return true;
        }

        static void Open(TaskInfo task, bool remember, IContentView returnTo = null)
        {
            if (task == null)
                return;
            if (remember)
                Sidebar.RememberTask(task.Id);
            WorkspaceWindow.OpenContent(new TaskDetailView(task), returnTo);
        }

        public override string Title => "Task " + (_taskId ?? "");

        public override void Opened()
        {
            _pollingDemand ??= SessionHub.Instance.TaskStore.OpenReader();
        }

        public override void Closed()
        {
            _pollingDemand?.Dispose();
            _pollingDemand = null;
            _reader.Reset();
        }

        public override void Draw(Rect body)
        {
            // Use the same centred band as Settings. The fullscreen chrome provides the
            // maximized reader, while the band keeps message lines from stretching across
            // a wide monitor.
            var panel = UiLayout.CenteredBand(body);
            Slab.Box(panel, UiTheme.WindowBg, UiTheme.Edge);
            var rect = panel.ContractedBy(UiTheme.GapM);
            UiLayout.Title(rect, Title);

            var current = SessionHub.Instance.Tasks.FirstOrDefault(task => task.Id == _taskId);
            if (!ReferenceEquals(current, _task))
            {
                if (
                    current?.Body != _task?.Body
                    || current?.Note != _task?.Note
                    || current?.History.Count != _task?.History.Count
                )
                    _reader.ClearSelection();
                _task = current;
            }
            if (_task == null)
            {
                _reader.Reset();
                UiText.PlainStatusLabel(
                    new Rect(rect.x, rect.y + UiTheme.HeaderH, rect.width, UiTheme.LineH * 2f),
                    "This task is no longer available.",
                    UiTheme.Dim
                );
                var close = new UiLayout.Bar(UiLayout.FooterBar(rect));
                if (close.Right("Close", UiTheme.Btn.Ghost))
                    Find.WindowStack?.WindowOfType<WorkspaceWindow>()?.Leave();
                return;
            }

            float top = rect.y + UiTheme.HeaderH + UiTheme.GapS;
            float bottom = rect.yMax - UiTheme.BtnH - UiTheme.GapS;
            var outer = new Rect(rect.x, top, rect.width, Mathf.Max(0f, bottom - top));
            _reader.Draw(
                outer,
                _task,
                Messages(),
                _task.Direction
                    + "  ·  "
                    + TaskInfo.StatusText(_task.Status)
                    + "  ·  Created: "
                    + Timestamp(_task.CreatedMs)
                    + "  ·  Updated: "
                    + Timestamp(_task.UpdatedMs)
            );
            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Copy task text", UiTheme.Btn.Ghost))
                DesktopClipboard.Copy(DialogueText());

            if (
                _task.Incoming
                && !_task.Terminal
                && foot.Left("Change status", UiTheme.Btn.Default)
            )
                TaskActions.OpenMenu(_task);

            if (_task.CanCancel && foot.Left("Cancel", UiTheme.Btn.Danger))
                TaskActions.CancelTask(_task);
            else if (_task.Terminal && foot.Left("Remove", UiTheme.Btn.Danger))
                TaskActions.RemoveTask(_task);

            if (foot.Right("Close", UiTheme.Btn.Ghost))
                Find.WindowStack?.WindowOfType<WorkspaceWindow>()?.Leave();
        }

        IEnumerable<(string, string, long, string)> Messages()
        {
            yield return (_task.Body ?? "", _task.From, _task.CreatedMs, null);
            foreach (var update in _task.History)
                yield return (
                    update.Note ?? "",
                    update.Sender,
                    update.TimestampMs,
                    TaskInfo.StatusText(update.Status)
                );
            if (_task.History.Count == 0 && !string.IsNullOrEmpty(_task.Note))
                yield return (
                    _task.Note,
                    _task.To,
                    _task.UpdatedMs,
                    TaskInfo.StatusText(_task.Status)
                );
        }

        string DialogueText()
        {
            var messages = new List<string>
            {
                "Message from " + SenderLabel(_task.From) + "  ·  " + Timestamp(_task.CreatedMs),
                _task.Body ?? "",
            };
            foreach (var update in _task.History)
            {
                messages.Add(
                    TaskInfo.StatusText(update.Status)
                        + "  ·  Message from "
                        + SenderLabel(update.Sender)
                        + "  ·  "
                        + Timestamp(update.TimestampMs)
                );
                if (!string.IsNullOrEmpty(update.Note))
                    messages.Add(update.Note);
            }
            if (_task.History.Count == 0 && !string.IsNullOrEmpty(_task.Note))
            {
                messages.Add(
                    "Message from " + SenderLabel(_task.To) + "  ·  " + Timestamp(_task.UpdatedMs)
                );
                messages.Add(_task.Note);
            }
            return string.Join("\n\n", messages.ToArray());
        }
    }
}
