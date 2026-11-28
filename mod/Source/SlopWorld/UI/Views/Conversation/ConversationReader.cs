using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Task and board readers share message geometry, portraits and selectable source text.
    internal sealed partial class ConversationReader
    {
        const float AvatarSize = 38f;
        const float AvatarOverlap = 18f;
        static float MessageTextInset => AvatarSize - AvatarOverlap + UiTheme.GapS;
        static float MessageTextX => AvatarOverlap + MessageTextInset;

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly TaskTextSelection _selection = new TaskTextSelection();
        readonly MouseClickSequence _selectionClicks = new MouseClickSequence();
        List<TaskTextSelection.Line> _selectionLines => _selection.Lines;
        readonly List<SenderHit> _senderHits = new List<SenderHit>();
        readonly List<MessageLayout> _messages = new List<MessageLayout>();
        readonly Dictionary<string, float> _smallElementWidths = new Dictionary<string, float>();
        object _layoutSource;
        bool _inputFocus;
        float _layoutWidth = -1f;
        float _layoutScale = -1f;
        int _layoutFontSize = -1;
        string _layoutFontName = "";
        int _layoutRevision = int.MinValue;
        float _contentHeight;
        long _layoutMinute;

        sealed class MessageLayout
        {
            public string Sender;
            public long Timestamp;
            public string Age;
            public string Status;
            public bool TimestampBelow;
            public float Y;
            public float Height;
        }

        float _metricsScale = -1f;
        int _metricsFontSize = -1;
        string _metricsFontName = "";

        struct SenderHit
        {
            public string Sender;
            public Rect Avatar;
            public Rect Name;
        }

        internal void Reset()
        {
            _scroll.JumpTo(Vector2.zero);
            _layoutSource = null;
            _inputFocus = false;
            ClearSelection();
        }

        internal void Draw(
            Rect outer,
            object source,
            IEnumerable<(string Body, string Sender, long Time, string Status)> entries,
            string heading = null,
            Action<int> openMenu = null,
            Func<int, bool> hasMenu = null
        )
        {
            if (outer.height <= 0 || outer.width <= 0)
                return;
            if (heading != null)
                _inputFocus = true;
            else if (Event.current.rawType == EventType.MouseDown)
                _inputFocus = outer.Contains(Event.current.mousePosition);
            float width = Mathf.Max(1f, outer.width - UiTheme.ScrollbarW);
            float messageWidth = openMenu == null ? width : Mathf.Max(1, width - 28);
            EnsureLayout(messageWidth, source, entries, heading != null);
            float contentH = _contentHeight;

            using (WidgetState.Save())
            using (_scroll.Scope(outer, new Rect(0f, 0f, width, Mathf.Max(outer.height, contentH))))
            {
                _senderHits.Clear();
                if (heading != null)
                    UiText.PlainStatusLabel(
                        new Rect(0, 0, width, UiTheme.TinyRowH),
                        heading,
                        UiTheme.Dim,
                        GameFont.Tiny
                    );
                for (int i = 0; i < _messages.Count; i++)
                {
                    var message = _messages[i];
                    if (message.Y + message.Height < _scroll.Position.y)
                        continue;
                    if (message.Y > _scroll.Position.y + outer.height)
                        break;
                    DrawMessage(
                        new Rect(0f, message.Y, messageWidth, message.Height),
                        message.Sender,
                        message.Timestamp,
                        message.Age,
                        message.Status,
                        message.TimestampBelow,
                        width
                    );
                    if (openMenu != null && hasMenu(i))
                    {
                        var button = new Rect(width - 26, message.Y + 2, 26, 24);
                        if (UiControls.Button(button, "···"))
                            openMenu(i);
                    }
                }
                DrawSelectableText(outer.height);
            }

            if (HandleSenderClicks(outer))
                return;
            HandleSelectionInput(outer);
        }

        static float MessageCardHeight(int lineCount, float headerH)
        {
            return Mathf.Max(
                AvatarSize + UiTheme.GapS,
                UiTheme.FieldPadY * 2f
                    + headerH
                    + UiTheme.GapXS
                    + lineCount * UiTheme.LineHOf(GameFont.Small)
            );
        }

        void DrawMessage(
            Rect r,
            string sender,
            long timestamp,
            string age,
            string status,
            bool timestampBelow,
            float frameWidth
        )
        {
            var card = new Rect(
                r.x + AvatarOverlap,
                r.y,
                Mathf.Max(1f, frameWidth - AvatarOverlap),
                r.height
            );
            Slab.Box(card, UiTheme.Well, UiTheme.Edge);

            var icon = new Rect(r.x, r.y + UiTheme.GapS, AvatarSize, AvatarSize);
            DrawSenderIcon(icon, sender);

            float textWidth = Mathf.Max(
                1f,
                r.width - AvatarOverlap - MessageTextInset - UiTheme.FieldPadX
            );
            float y = card.y + UiTheme.FieldPadY;
            var header = new Rect(
                card.x + MessageTextInset,
                y,
                textWidth,
                UiTheme.LineHOf(GameFont.Tiny)
            );
            var wrap = Text.WordWrap;
            var anchor = Text.Anchor;
            var font = Text.Font;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                string prefix = Prefix(status);
                string label = SenderLabel(sender);
                string suffix = timestamp > 0 && !timestampBelow ? "  ·  " + age : "";
                if (timestampBelow)
                {
                    GUI.color = UiTheme.Dim;
                    var timeRow = new Rect(header.x, header.yMax, header.width, header.height);
                    UiText.RowLabel(timeRow, age);
                    TooltipHandler.TipRegion(timeRow, Timestamp(timestamp));
                }
                float prefixW = UiTheme.Wide(prefix);
                float labelW = UiTheme.Wide(label);
                float suffixW = UiTheme.Wide(suffix);
                float nameX = header.x + prefixW;
                if (timestamp > 0 && !timestampBelow)
                    TooltipHandler.TipRegion(
                        new Rect(nameX + labelW, header.y, suffixW, header.height),
                        Timestamp(timestamp)
                    );
                float nameW = Mathf.Min(labelW, Mathf.Max(0f, header.xMax - nameX));
                if (IsAgentSender(sender))
                {
                    var name = new Rect(nameX, header.y, nameW, header.height);
                    _senderHits.Add(
                        new SenderHit
                        {
                            Sender = sender,
                            Avatar = icon,
                            Name = name,
                        }
                    );
                    if (nameW > 0f)
                        TooltipHandler.TipRegion(name, "Click to open " + sender + "'s terminal.");
                }

                if (prefixW + labelW + suffixW <= header.width)
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(header.x, header.y, prefixW, header.height), prefix);
                    GUI.color = IsAgentSender(sender) ? UiTheme.Lead : UiTheme.Dim;
                    UiText.RowLabel(new Rect(nameX, header.y, labelW, header.height), label);
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(
                        new Rect(nameX + labelW, header.y, suffixW, header.height),
                        suffix
                    );
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

        static string Prefix(string status) =>
            status == null ? "Message from "
            : status.Length == 0 ? ""
            : status + "  ·  ";

        internal static string RelativeTime(long timestamp, long now)
        {
            long minutes = (now - timestamp) / 60000;
            if (minutes < 1)
                return "just now";
            long count =
                minutes < 60 ? minutes
                : minutes < 1440 ? minutes / 60
                : minutes / 1440;
            string unit =
                minutes < 60 ? "minute"
                : minutes < 1440 ? "hour"
                : "day";
            return count + " " + unit + (count == 1 ? " ago" : "s ago");
        }

        internal static string Timestamp(long milliseconds)
        {
            if (milliseconds <= 0)
                return "unknown time";
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var local = Settings.InTimeZone(epoch.AddMilliseconds(milliseconds));
            return DateFormat.Short(local, Settings.DateFormat)
                + " "
                + TimeFormat.Long(local, Settings.TimeFormat)
                + " "
                + local.ToString("zzz", CultureInfo.InvariantCulture);
        }

        static void DrawSenderIcon(Rect r, string sender)
        {
            var pawn =
                sender == WireProtocol.HostIdentity
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
                GUI.color = sender == WireProtocol.HostIdentity ? UiTheme.Lead : UiTheme.Info;
                GUI.DrawTexture(
                    r.ContractedBy(8f),
                    sender == WireProtocol.HostIdentity ? Icons.Terminal : Icons.Agents
                );
            }
            GUI.color = old;

            TooltipHandler.TipRegion(
                r,
                IsAgentSender(sender)
                    ? "Message from "
                        + SenderLabel(sender)
                        + "\nClick to open this agent's terminal."
                    : "Message from " + SenderLabel(sender)
            );
        }

        internal static string SenderLabel(string sender) =>
            sender == WireProtocol.HostIdentity ? "you"
            : string.IsNullOrEmpty(sender) ? "unknown"
            : sender;

        static bool IsAgentSender(string sender) =>
            !string.IsNullOrEmpty(sender)
            && sender != WireProtocol.HostIdentity
            && SessionHub.Instance.Get(sender) != null;
    }
}
