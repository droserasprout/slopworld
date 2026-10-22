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
        readonly SmoothScroll _scroll = new SmoothScroll();

        readonly string _title;
        readonly string _primaryLabel;
        readonly string _secondaryLabel;
        readonly Action _primary;
        readonly Action _secondary;
        readonly UiTheme.Btn _primaryKind;
        readonly string _checkboxLabel;
        readonly Action<bool> _checkboxChanged;
        bool _checked;

        float CheckboxHeight =>
            string.IsNullOrEmpty(_checkboxLabel) ? 0f : UiTheme.RowH + UiTheme.GapM;

        AlertDialog(
            string title,
            string message,
            string primaryLabel,
            Action primary,
            string secondaryLabel,
            Action secondary,
            UiTheme.Btn primaryKind,
            float width,
            string checkboxLabel,
            Action<bool> checkboxChanged
        )
        {
            resizeable = true;
            _message = message ?? "";
            _width = width;
            _title = title ?? "SlopWorld";
            _primaryLabel = primaryLabel ?? "OK";
            _secondaryLabel = secondaryLabel;
            _primary = primary;
            _secondary = secondary;
            _primaryKind = primaryKind;
            _checkboxLabel = checkboxLabel;
            _checkboxChanged = checkboxChanged;
            AcceptOnEnter(AcceptPrimary);
        }

        public static Window Create(
            string title,
            string message,
            string primaryLabel,
            Action primary,
            string secondaryLabel = null,
            Action secondary = null,
            UiTheme.Btn primaryKind = UiTheme.Btn.Primary,
            float width = 520f,
            string checkboxLabel = null,
            Action<bool> checkboxChanged = null
        ) =>
            new AlertDialog(
                title,
                message,
                primaryLabel,
                primary,
                secondaryLabel,
                secondary,
                primaryKind,
                width,
                checkboxLabel,
                checkboxChanged
            );

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(_width, UI.screenWidth);
                float height =
                    4f * UiTheme.GapM
                    + UiTheme.HeaderH
                    + MessageHeight(_message, width - 2f * UiTheme.GapM - UiTheme.ScrollbarW)
                    + UiTheme.BtnH
                    + CheckboxHeight;
                return new Vector2(width, Mathf.Min(height, UI.screenHeight));
            }
        }

        protected override bool Closable => false;

        protected override Vector2 MinimumSize =>
            new Vector2(Mathf.Min(360f, InitialSize.x), Mathf.Min(180f, InitialSize.y));

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _title);

            float messageY = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            var message = new Rect(
                rect.x,
                messageY,
                rect.width,
                Mathf.Max(0f, UiLayout.FooterBar(rect).y - CheckboxHeight - UiTheme.GapM - messageY)
            );
            float width = Mathf.Max(0f, message.width - UiTheme.ScrollbarW);
            var view = new Rect(0f, 0f, width, MessageHeight(_message, width));
            using (_scroll.Scope(message, view))
                UiText.PlainStatusLabel(view, _message, UiTheme.Name);

            // Keep the optional control beside the actions even when the message scrolls.
            if (!string.IsNullOrEmpty(_checkboxLabel))
            {
                var checkbox = new Rect(
                    rect.x,
                    UiLayout.FooterBar(rect).y - CheckboxHeight,
                    rect.width,
                    UiTheme.RowH
                );
                bool value = UiControls.Checkbox(checkbox, _checkboxLabel, _checked);
                if (value != _checked)
                {
                    _checked = value;
                    _checkboxChanged?.Invoke(value);
                }
            }

            DrawActions(new UiLayout.Bar(UiLayout.FooterBar(rect)));
        }

        void DrawActions(UiLayout.Bar foot)
        {
            if (
                !string.IsNullOrEmpty(_secondaryLabel)
                && foot.Left(_secondaryLabel, UiTheme.Btn.Ghost)
            )
            {
                Close();
                _secondary?.Invoke();
                return;
            }

            if (foot.Right(_primaryLabel, _primaryKind))
                AcceptPrimary();
        }

        void AcceptPrimary()
        {
            Close();
            _primary?.Invoke();
        }
    }
}
