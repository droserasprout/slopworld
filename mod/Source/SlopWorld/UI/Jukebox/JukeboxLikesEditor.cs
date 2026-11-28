using System;
using UnityEngine;

namespace SlopWorld
{
    internal sealed class JukeboxLikesEditor : UiWindow
    {
        readonly string _original;
        string _text;
        string _error;
        bool _saving;

        internal JukeboxLikesEditor(string original)
        {
            _text = _original = original;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 620f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), "Edit jukebox.toml");
            float top = rect.y + UiTheme.HeaderH + UiTheme.GapS;
            var footer = UiLayout.FooterBar(rect);
            float errorHeight =
                _error == null ? 0f : MessageHeight(_error, rect.width) + UiTheme.GapS;
            var area = new Rect(
                rect.x,
                top,
                rect.width,
                Mathf.Max(0f, footer.y - top - UiTheme.GapS - errorHeight)
            );
            _text = UiText.Area(area, "jukebox-likes", _text, on: !_saving);
            if (_error != null)
                UiText.PlainStatusLabel(
                    new Rect(rect.x, area.yMax, rect.width, errorHeight),
                    _error,
                    UiTheme.Bad
                );
            var buttons = new UiLayout.Bar(footer);
            if (buttons.Left("Cancel", UiTheme.Btn.Ghost, !_saving))
                Close();
            if (buttons.Right("Save", UiTheme.Btn.Primary, !_saving))
                Save();
        }

        async void Save()
        {
            _saving = true;
            try
            {
                await JukeboxLibrary.WriteBrowser(_original, _text);
                Close();
            }
            catch (Exception e)
            {
                _error = "Could not save likes. " + e.Message;
            }
            finally
            {
                _saving = false;
            }
        }
    }
}
