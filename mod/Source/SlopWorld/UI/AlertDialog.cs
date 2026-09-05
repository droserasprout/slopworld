using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Informational messages use the same surface as confirmations; even the safety alert
    // shown before the profile gate has finished must not fall back to RimWorld chrome.
    public sealed class AlertDialog : MessageDialog
    {
        const float Width = 520f;

        readonly string _title;
        readonly string _primaryLabel;
        readonly string _secondaryLabel;
        readonly Action _primary;
        readonly Action _secondary;
        readonly UiWidgets.Btn _primaryKind;

        AlertDialog(string title, string message, string primaryLabel, Action primary,
                        string secondaryLabel, Action secondary, UiWidgets.Btn primaryKind)
            : base(message)
        {
            _title = title ?? "SlopWorld";
            _primaryLabel = primaryLabel ?? "OK";
            _secondaryLabel = secondaryLabel;
            _primary = primary;
            _secondary = secondary;
            _primaryKind = primaryKind;
            AcceptOnEnter(() =>
            {
                Close();
                _primary?.Invoke();
            });
        }

        public static Window Create(string title, string message, string primaryLabel,
                                    Action primary, string secondaryLabel = null,
                                    Action secondary = null,
                                    UiWidgets.Btn primaryKind = UiWidgets.Btn.Primary) =>
            new AlertDialog(title, message, primaryLabel, primary, secondaryLabel,
                secondary, primaryKind);

        protected override string DialogTitle => _title;
        protected override float DialogWidth => Width;

        protected override void DrawActions(UiWidgets.Bar foot)
        {
            if (!string.IsNullOrEmpty(_secondaryLabel) &&
                foot.Left(_secondaryLabel, UiWidgets.Btn.Ghost))
            {
                Close();
                _secondary?.Invoke();
                return;
            }

            if (foot.Right(_primaryLabel, _primaryKind))
            {
                Close();
                _primary?.Invoke();
            }
        }

    }
}
