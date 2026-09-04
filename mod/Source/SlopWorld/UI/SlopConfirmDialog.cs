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
            AcceptOnEnter(() =>
            {
                Close();
                _confirmed?.Invoke();
            });
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

            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = SlopWidgets.Name;
                Widgets.Label(message, _message);
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

    }
}
