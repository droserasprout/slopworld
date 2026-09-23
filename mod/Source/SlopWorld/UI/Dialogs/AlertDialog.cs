using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Informational messages use the same surface as confirmations. Even the safety alert
    // shown before the profile gate has finished must not fall back to RimWorld chrome.
    public sealed class AlertDialog : UiWindow
    {
        readonly string _message;
        readonly float _width;

        readonly string _title;
        readonly string _primaryLabel;
        readonly string _secondaryLabel;
        readonly Action _primary;
        readonly Action _secondary;
        readonly UiTheme.Btn _primaryKind;

        AlertDialog(string title, string message, string primaryLabel, Action primary,
                        string secondaryLabel, Action secondary, UiTheme.Btn primaryKind, float width)
        {
            _message = message ?? "";
            _width = width;
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
                                    UiTheme.Btn primaryKind = UiTheme.Btn.Primary, float width = 520f) =>
            new AlertDialog(title, message, primaryLabel, primary, secondaryLabel,
                secondary, primaryKind, width);
        public override Vector2 InitialSize => new Vector2(_width,
            4f * UiTheme.GapM + UiTheme.HeaderH +
            MessageHeight(_message, _width - 2f * UiTheme.GapM) + UiTheme.BtnH);

        protected override bool Closable => false;

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _title);

            float messageY = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            float messageH = MessageHeight(_message, rect.width);
            var message = new Rect(rect.x, messageY, rect.width, messageH);

            UiText.StatusLabel(message, _message, UiTheme.Name);

            DrawActions(new UiLayout.Bar(UiLayout.FooterBar(rect)));
        }

        void DrawActions(UiLayout.Bar foot)
        {
            if (!string.IsNullOrEmpty(_secondaryLabel) &&
                foot.Left(_secondaryLabel, UiTheme.Btn.Ghost))
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
