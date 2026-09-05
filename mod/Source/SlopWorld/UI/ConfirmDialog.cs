using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The confirmation surface belongs to SlopWorld rather than RimWorld's message box, so
    // destructive actions keep the same frame, typography and buttons as the forms that ask.
    public sealed class ConfirmDialog : MessageDialog
    {
        const float Width = 480f;

        readonly Action _confirmed;
        readonly bool _destructive;

        ConfirmDialog(string message, Action confirmed, bool destructive) : base(message)
        {
            _confirmed = confirmed;
            _destructive = destructive;
            AcceptOnEnter(() =>
            {
                Close();
                _confirmed?.Invoke();
            });
        }

        public static Window Create(string message, Action confirmed, bool destructive = false) =>
            new ConfirmDialog(message, confirmed, destructive);

        protected override string DialogTitle => "Confirm";
        protected override float DialogWidth => Width;

        protected override void DrawActions(UiWidgets.Bar foot)
        {
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost))
            {
                Close();
                return;
            }

            if (foot.Right("Confirm", _destructive ? UiWidgets.Btn.Danger :
                                     UiWidgets.Btn.Primary))
            {
                Close();
                _confirmed?.Invoke();
            }
        }

    }
}
