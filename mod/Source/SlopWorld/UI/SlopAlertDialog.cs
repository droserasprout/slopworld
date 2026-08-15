using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Informational messages use the same surface as confirmations; even the safety alert
    // shown before the profile gate has finished must not fall back to RimWorld chrome.
    public sealed class SlopAlertDialog : SlopWindow
    {
        const float Width = 520f;

        readonly string _title;
        readonly string _message;
        readonly string _primaryLabel;
        readonly string _secondaryLabel;
        readonly Action _primary;
        readonly Action _secondary;
        readonly SlopWidgets.Btn _primaryKind;

        SlopAlertDialog(string title, string message, string primaryLabel, Action primary,
                        string secondaryLabel, Action secondary, SlopWidgets.Btn primaryKind)
        {
            _title = title ?? "SlopWorld";
            _message = message ?? "";
            _primaryLabel = primaryLabel ?? "OK";
            _secondaryLabel = secondaryLabel;
            _primary = primary;
            _secondary = secondary;
            _primaryKind = primaryKind;
            closeOnCancel = true;
        }

        public static Window Create(string title, string message, string primaryLabel,
                                    Action primary, string secondaryLabel = null,
                                    Action secondary = null,
                                    SlopWidgets.Btn primaryKind = SlopWidgets.Btn.Primary) =>
            new SlopAlertDialog(title, message, primaryLabel, primary, secondaryLabel,
                secondary, primaryKind);

        public override Vector2 InitialSize => new Vector2(Width,
            4f * SlopWidgets.GapM + SlopWidgets.HeaderH +
            MessageHeight(_message, Width - 2f * SlopWidgets.GapM) + SlopWidgets.BtnH);

        protected override bool Closable => false;

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, _title);

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
            if (!string.IsNullOrEmpty(_secondaryLabel) &&
                foot.Left(_secondaryLabel, SlopWidgets.Btn.Ghost))
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
