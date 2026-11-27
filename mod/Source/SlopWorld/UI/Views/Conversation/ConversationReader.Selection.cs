using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    internal sealed partial class ConversationReader
    {
        void DrawSelectableText(float viewportHeight)
        {
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            var wasAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;
                _selection.DrawHighlights(_scroll.Position.y, _scroll.Position.y + viewportHeight);
                GUI.color = UiTheme.Lead;
                int first = _selection.FirstVisibleLine(_scroll.Position.y);
                float bottom = _scroll.Position.y + viewportHeight;
                for (int i = first; i < _selectionLines.Count; i++)
                {
                    var line = _selectionLines[i];
                    if (line.Y >= bottom)
                        break;
                    Widgets.Label(new Rect(line.X, line.Y, line.Width, line.Height), line.Text);
                }
            }
            finally
            {
                GUI.color = Color.white;
                Text.Anchor = wasAnchor;
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        bool HandleSenderClicks(Rect viewport)
        {
            var e = Event.current;
            if (
                e == null
                || e.button != 0
                || UiEvent.RawType(e) != EventType.MouseDown
                || !viewport.Contains(e.mousePosition)
            )
                return false;

            foreach (var hit in _senderHits)
            {
                var avatar = ScreenRect(viewport, hit.Avatar);
                var name = ScreenRect(viewport, hit.Name);
                if (!avatar.Contains(e.mousePosition) && !name.Contains(e.mousePosition))
                    continue;

                _selectionClicks.Reset();
                Sidebar.FocusAgent(hit.Sender);
                e.Use();
                return true;
            }
            return false;
        }

        Rect ScreenRect(Rect viewport, Rect content) =>
            new Rect(
                viewport.x + content.x - _scroll.Position.x,
                viewport.y + content.y - _scroll.Position.y,
                content.width,
                content.height
            );

        void HandleSelectionInput(Rect viewport)
        {
            var e = Event.current;
            if (e == null)
                return;
            var type = UiEvent.RawType(e);
            if (type == EventType.KeyDown)
            {
                if (_inputFocus && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
                    _selection.HandleKey(e);
                return;
            }
            if (e.button == 1 && type == EventType.MouseDown && viewport.Contains(e.mousePosition))
            {
                _selectionClicks.Reset();
                _selection.OpenMenu();
                e.Use();
                return;
            }
            if (e.button != 0)
                return;
            if (type == EventType.MouseDown)
            {
                if (!viewport.Contains(e.mousePosition))
                {
                    _selectionClicks.Reset();
                    return;
                }
                GUI.FocusControl(null);
                int clicks = _selectionClicks.Observe(e, Time.realtimeSinceStartup);
                _selection.BeginMouse(viewport, e, clicks, _scroll.Position);
                if (clicks >= 3)
                    _selectionClicks.Reset();
            }
            else if (type == EventType.MouseDrag)
                _selection.DragMouse(viewport, e, _scroll.Position);
            else if (type == EventType.MouseUp)
                _selection.EndMouse(viewport, e, _scroll.Position);
        }

        internal void ClearSelection()
        {
            _selection.Clear();
            _selectionClicks.Reset();
        }
    }
}
