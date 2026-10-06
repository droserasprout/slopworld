using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;
using TextRange = SlopWorld.TextElementLayout.Range;

namespace SlopWorld
{
    // Full task reader shown as a TerminalWindow content view.
    public sealed partial class TaskDetailView : ContentView
    {
        const float AvatarSize = 38f;
        const float AvatarOverlap = 18f;
        static float MessageTextInset => AvatarSize - AvatarOverlap + UiTheme.GapS;
        static float MessageTextX => AvatarOverlap + MessageTextInset;

        readonly string _taskId;
        TaskInfo _task;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly TaskTextSelection _selection = new TaskTextSelection();
        List<TaskTextSelection.Line> _selectionLines => _selection.Lines;
        readonly List<SenderHit> _senderHits = new List<SenderHit>();
        readonly List<TextRange> _bodyRanges = new List<TextRange>();
        readonly List<TextRange> _noteRanges = new List<TextRange>();
        readonly Dictionary<string, float> _smallElementWidths =
            new Dictionary<string, float>();
        TaskInfo _layoutTask;
        float _layoutWidth = -1f;
        float _layoutScale = -1f;
        int _layoutFontSize = -1;
        string _layoutFontName = "";
        int _layoutRevision = int.MinValue;
        float _bodyHeight;
        float _noteHeight;
        float _metricsScale = -1f;
        int _metricsFontSize = -1;
        string _metricsFontName = "";
        struct SenderHit
        {
            public string Sender;
            public Rect Avatar;
            public Rect Name;
        }

        public TaskDetailView(TaskInfo task) { _task = task; _taskId = task?.Id; }

        public static void Open(TaskInfo task)
        {
            if (task == null) return;
            AgentSidebar.RememberTask(task.Id);
            Open(task, false);
        }

        internal static bool FocusLocation(string id)
        {
            var task = SessionHub.Instance.Tasks.FirstOrDefault(candidate =>
                candidate != null && candidate.Id == id);
            if (task == null) return false;
            Open(task, false);
            return true;
        }

        static void Open(TaskInfo task, bool remember)
        {
            if (task == null) return;
            if (remember) AgentSidebar.RememberTask(task.Id);
            TerminalWindow.OpenContent(new TaskDetailView(task));
        }

        public override string Title => "Task " + (_taskId ?? "");

        public override void Opened() { }

        public override void Closed()
        {
            _scroll.JumpTo(Vector2.zero);
            ClearSelection();
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
                if (current?.Body != _task?.Body || current?.Note != _task?.Note) ClearSelection();
                _task = current;
            }
            if (_task == null)
            {
                _senderHits.Clear();
                ClearSelection();
                UiText.PlainStatusLabel(new Rect(rect.x, rect.y + UiTheme.HeaderH,
                    rect.width, UiTheme.LineH * 2f), "This task is no longer available.", UiTheme.Dim);
                var close = new UiLayout.Bar(UiLayout.FooterBar(rect));
                if (close.Right("Close", UiTheme.Btn.Ghost))
                    Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                return;
            }

            float top = rect.y + UiTheme.HeaderH + UiTheme.GapS;
            float bottom = rect.yMax - UiTheme.BtnH - UiTheme.GapS;
            var outer = new Rect(rect.x, top, rect.width, Mathf.Max(0f, bottom - top));
            float width = Mathf.Max(1f, outer.width - UiTheme.ScrollbarW);
            string bodyText = _task?.Body ?? "";
            string noteText = _task?.Note ?? "";
            EnsureLayout(width, bodyText, noteText);
            float bodyH = _bodyHeight;
            float noteH = _noteHeight;
            float contentH = UiTheme.TinyRowH + UiTheme.GapS + bodyH + noteH +
                UiTheme.GapS;

            using (WidgetState.Save())
            using (_scroll.Scope(outer, new Rect(0f, 0f, width,
                Mathf.Max(outer.height, contentH))))
            {
                _senderHits.Clear();
                float y = 0f;
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(new Rect(0f, y, width, UiTheme.TinyRowH),
                    _task.Direction + "  ·  " + TaskInfo.StatusText(_task.Status) +
                    "  ·  Created: " + Timestamp(_task.CreatedMs) +
                    "  ·  Updated: " + Timestamp(_task.UpdatedMs));
                GUI.color = Color.white;
                y += UiTheme.TinyRowH + UiTheme.GapS;

                DrawMessage(new Rect(0f, y, width, bodyH), _task.From, _task.CreatedMs);
                y += bodyH;
                if (!string.IsNullOrEmpty(_task.Note))
                {
                    y += UiTheme.GapM;
                    DrawMessage(new Rect(0f, y, width, noteH - UiTheme.GapM), _task.To,
                        _task.UpdatedMs, true);
                }
                DrawSelectableText(outer.height);
            }

            if (HandleSenderClicks(outer)) return;
            HandleSelectionInput(outer);

            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Copy task text", UiTheme.Btn.Ghost))
                DaemonClipboard.Copy(DialogueText());

            if (_task.Incoming && !_task.Terminal &&
                foot.Left("Change status", UiTheme.Btn.Default))
                TaskActions.OpenMenu(_task);

            if ((_task.Status == DelegatedTaskStatus.Queued ||
                 _task.Status == DelegatedTaskStatus.Accepted) &&
                foot.Left("Cancel", UiTheme.Btn.Danger))
                TaskActions.CancelTask(_task);
            else if (_task.Terminal && foot.Left("Remove", UiTheme.Btn.Danger))
                TaskActions.RemoveTask(_task);

            if (foot.Right("Close", UiTheme.Btn.Ghost))
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
        }

        static float MessageCardHeight(int lineCount)
        {
            float headerH = UiTheme.LineHOf(GameFont.Tiny);
            return Mathf.Max(AvatarSize + UiTheme.GapS,
                UiTheme.FieldPadY * 2f + headerH + UiTheme.GapXS +
                lineCount * UiTheme.LineHOf(GameFont.Small));
        }

        void DrawMessage(Rect r, string sender, long timestamp,
                         bool note = false)
        {
            var card = new Rect(r.x + AvatarOverlap, r.y,
                Mathf.Max(1f, r.width - AvatarOverlap), r.height);
            Slab.Box(card, UiTheme.Well, UiTheme.Edge);

            var icon = new Rect(r.x, r.y + UiTheme.GapS, AvatarSize, AvatarSize);
            DrawSenderIcon(icon, sender);

            float textWidth = Mathf.Max(1f, card.width - MessageTextInset -
                UiTheme.FieldPadX);
            float y = card.y + UiTheme.FieldPadY;
            var header = new Rect(card.x + MessageTextInset, y, textWidth,
                UiTheme.LineHOf(GameFont.Tiny));
            var wrap = Text.WordWrap;
            var anchor = Text.Anchor;
            var font = Text.Font;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                string prefix = note ? "Latest note from " : "Message from ";
                string label = SenderLabel(sender);
                string suffix = "  ·  " + Timestamp(timestamp);
                float prefixW = UiTheme.Wide(prefix);
                float labelW = UiTheme.Wide(label);
                float suffixW = UiTheme.Wide(suffix);
                float nameX = header.x + prefixW;
                float nameW = Mathf.Min(labelW, Mathf.Max(0f, header.xMax - nameX));
                if (IsAgentSender(sender))
                {
                    var name = new Rect(nameX, header.y, nameW, header.height);
                    _senderHits.Add(new SenderHit
                    {
                        Sender = sender,
                        Avatar = icon,
                        Name = name,
                    });
                    if (nameW > 0f)
                        TooltipHandler.TipRegion(name,
                            "Click to open " + sender + "'s terminal.");
                }

                if (prefixW + labelW + suffixW <= header.width)
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(header.x, header.y, prefixW, header.height),
                        prefix);
                    GUI.color = IsAgentSender(sender) ? UiTheme.Lead : UiTheme.Dim;
                    UiText.RowLabel(new Rect(nameX, header.y, labelW, header.height), label);
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(nameX + labelW, header.y, suffixW,
                        header.height), suffix);
                }
                else
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(header, prefix + label + suffix);
                }
            }
            finally
            {
                GUI.color = Color.white;
                Text.WordWrap = wrap;
                Text.Anchor = anchor;
                Text.Font = font;
            }
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

            Slab.Box(r, UiTheme.Well, UiTheme.Edge);
            GUI.color = Color.white;
            if (portrait != null)
                GUI.DrawTexture(r.ContractedBy(2f), portrait, ScaleMode.ScaleToFit, true);
            else
            {
                GUI.color = sender == TaskInfo.Host ? UiTheme.Lead : UiTheme.Info;
                GUI.DrawTexture(r.ContractedBy(8f),
                    sender == TaskInfo.Host ? Icons.Terminal : Icons.Agents);
            }
            GUI.color = old;

            TooltipHandler.TipRegion(r, IsAgentSender(sender)
                ? "Message from " + SenderLabel(sender) +
                    "\nClick to open this agent's terminal."
                : "Message from " + SenderLabel(sender));
        }

        static string SenderLabel(string sender) =>
            sender == TaskInfo.Host ? "you" : string.IsNullOrEmpty(sender) ? "unknown" : sender;

        static bool IsAgentSender(string sender) => !string.IsNullOrEmpty(sender) &&
            sender != TaskInfo.Host && SessionHub.Instance.Get(sender) != null;
    }
}
