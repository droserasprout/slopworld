using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The confirmation surface belongs to SlopWorld rather than RimWorld's message box, so
    // destructive actions keep the same frame, typography and buttons as the forms that ask.
    public sealed class SlopConfirmDialog : MessageDialog
    {
        const float Width = 480f;

        readonly Action _confirmed;
        readonly bool _destructive;

        SlopConfirmDialog(string message, Action confirmed, bool destructive) : base(message)
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
            new SlopConfirmDialog(message, confirmed, destructive);

        protected override string DialogTitle => "Confirm";
        protected override float DialogWidth => Width;

        protected override void DrawActions(SlopWidgets.Bar foot)
        {
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
