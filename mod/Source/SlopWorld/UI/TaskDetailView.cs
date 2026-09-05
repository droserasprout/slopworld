using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Full task reader shown as a TerminalWindow content view.
    public sealed partial class TaskDetailView : IContentView
    {
        const float AvatarSize = 38f;
        const float AvatarOverlap = 18f;
        const float MessageTextInset = AvatarSize - AvatarOverlap + SlopWidgets.GapS;
        const float MessageTextX = AvatarOverlap + MessageTextInset;

        TaskInfo _task;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<DialogueLine> _selectionLines = new List<DialogueLine>();
        readonly List<SenderHit> _senderHits = new List<SenderHit>();
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

        struct SenderHit
        {
            public string Sender;
            public Rect Avatar;
            public Rect Name;
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

            using (WidgetState.Save())
            using (_scroll.Scope(outer, new Rect(0f, 0f, width,
                Mathf.Max(outer.height, contentH))))
            {
                _senderHits.Clear();
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

            if (HandleSenderClicks(outer)) return;
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
            var header = new Rect(card.x + MessageTextInset, y, textWidth,
                SlopWidgets.LineHOf(GameFont.Tiny));
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
                float prefixW = SlopWidgets.Wide(prefix);
                float labelW = SlopWidgets.Wide(label);
                float suffixW = SlopWidgets.Wide(suffix);
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
                            "Focus " + sender + " in the agents sidebar");
                }

                if (prefixW + labelW + suffixW <= header.width)
                {
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(new Rect(header.x, header.y, prefixW, header.height),
                        prefix);
                    GUI.color = IsAgentSender(sender) ? SlopWidgets.Lead : SlopWidgets.Dim;
                    SlopWidgets.RowLabel(new Rect(nameX, header.y, labelW, header.height), label);
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(new Rect(nameX + labelW, header.y, suffixW,
                        header.height), suffix);
                }
                else
                {
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(header, prefix + label + suffix);
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

            TooltipHandler.TipRegion(r, IsAgentSender(sender)
                ? "Message from " + SenderLabel(sender) + "\nClick to focus in the agents sidebar"
                : "Message from " + SenderLabel(sender));
        }

        static string SenderLabel(string sender) =>
            sender == TaskInfo.Host ? "you" : string.IsNullOrEmpty(sender) ? "unknown" : sender;

        static bool IsAgentSender(string sender) => !string.IsNullOrEmpty(sender) &&
            sender != TaskInfo.Host && SessionHub.Instance.Get(sender) != null;
    }
}
