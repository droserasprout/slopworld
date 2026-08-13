using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The confirmation surface belongs to SlopWorld rather than RimWorld's message box, so
    // destructive actions keep the same frame, typography and buttons as the forms that ask.
    public sealed class SlopConfirmDialog : SlopWindow
    {
        const float Width = 480f;

        readonly string _message;
        readonly Action _confirmed;
        readonly bool _destructive;

        SlopConfirmDialog(string message, Action confirmed, bool destructive)
        {
            _message = message ?? "";
            _confirmed = confirmed;
            _destructive = destructive;
            closeOnCancel = true;
        }

        public static Window Create(string message, Action confirmed, bool destructive = false) =>
            new SlopConfirmDialog(message, confirmed, destructive);

        public override Vector2 InitialSize => new Vector2(Width,
            4f * SlopWidgets.GapM + SlopWidgets.HeaderH +
            MessageHeight(_message, Width - 2f * SlopWidgets.GapM) + SlopWidgets.BtnH);

        protected override bool Closable => false;

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, "Confirm");

            float messageY = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM;
            float messageH = MessageHeight(_message, rect.width);
            var message = new Rect(rect.x, messageY, rect.width, messageH);

            var wasFont = Text.Font;
            var wasAnchor = Text.Anchor;
            var wasWrap = Text.WordWrap;
            var wasColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = SlopWidgets.Name;
                Widgets.Label(message, _message);
            }
            finally
            {
                GUI.color = wasColor;
                Text.WordWrap = wasWrap;
                Text.Anchor = wasAnchor;
                Text.Font = wasFont;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost))
            {
                Close();
                return;
            }

            if (foot.Right("Confirm", _destructive ? SlopWidgets.Btn.Danger :
                                     SlopWidgets.Btn.Primary))
            {
                Close();
                _confirmed?.Invoke();
            }
        }

        static float MessageHeight(string text, float width)
        {
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Mathf.Max(SlopWidgets.LineHOf(GameFont.Small),
                    Text.CalcHeight(string.IsNullOrEmpty(text) ? " " : text, width));
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }
    }
}
