using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TaskDetailView text selection and clipboard actions.
    public sealed partial class TaskDetailView
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
                DrawSelectionHighlights(viewportHeight);
                GUI.color = UiWidgets.Lead;
                int first = FirstVisibleSelectionLine(_scroll.Position.y);
                float bottom = _scroll.Position.y + viewportHeight;
                for (int i = first; i < _selectionLines.Count; i++)
                {
                    var line = _selectionLines[i];
                    if (line.Y >= bottom) break;
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

        int FirstVisibleSelectionLine(float top)
        {
            int low = 0;
            int high = _selectionLines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var line = _selectionLines[middle];
                if (line.Y + line.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        void DrawSelectionHighlights(float viewportHeight)
        {
            if (_selectionStart == _selectionEnd) return;
            int first = Mathf.Min(_selectionStart, _selectionEnd);
            int last = Mathf.Max(_selectionStart, _selectionEnd);
            int firstLine = FirstVisibleSelectionLine(_scroll.Position.y);
            float bottom = _scroll.Position.y + viewportHeight;
            for (int i = firstLine; i < _selectionLines.Count; i++)
            {
                var line = _selectionLines[i];
                if (line.Y >= bottom) break;
                int start = Mathf.Max(first, line.Start);
                int end = Mathf.Min(last, line.End);
                if (end <= start) continue;

                int from = Mathf.Clamp(start - line.Start, 0, line.Edges.Length - 1);
                int to = Mathf.Clamp(end - line.Start, from, line.Edges.Length - 1);
                Slab.Fill(new Rect(line.X + line.Edges[from], line.Y,
                    Mathf.Max(1f, line.Edges[to] - line.Edges[from]), line.Height),
                    UiWidgets.Sel);
            }
        }

        bool HandleSenderClicks(Rect viewport)
        {
            var e = Event.current;
            if (e == null || e.button != 0 ||
                (e.type == EventType.Used ? e.rawType : e.type) != EventType.MouseDown)
                return false;

            foreach (var hit in _senderHits)
            {
                var avatar = ScreenRect(viewport, hit.Avatar);
                var name = ScreenRect(viewport, hit.Name);
                if (!avatar.Contains(e.mousePosition) && !name.Contains(e.mousePosition)) continue;

                AgentSidebar.FocusAgent(hit.Sender);
                e.Use();
                return true;
            }
            return false;
        }

        Rect ScreenRect(Rect viewport, Rect content) => new Rect(
            viewport.x + content.x - _scroll.Position.x,
            viewport.y + content.y - _scroll.Position.y,
            content.width, content.height);

        void HandleSelectionInput(Rect viewport)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown ||
                (e.type == EventType.Used && e.rawType == EventType.KeyDown))
            {
                if (e.control && !e.alt)
                {
                    if (e.keyCode == KeyCode.C)
                    {
                        CopySelection();
                        e.Use();
                        return;
                    }
                    if (e.keyCode == KeyCode.A)
                    {
                        SelectAll();
                        e.Use();
                        return;
                    }
                }
                return;
            }

            EventType type = e.type == EventType.Used ? e.rawType : e.type;
            if (e.button == 1 && type == EventType.MouseDown &&
                viewport.Contains(e.mousePosition))
            {
                OpenSelectionMenu();
                e.Use();
                return;
            }

            if (e.button != 0) return;
            if (type == EventType.MouseDown && viewport.Contains(e.mousePosition))
            {
                int point = SelectionPointAt(viewport, e.mousePosition);
                if (!e.shift) _selectionStart = point;
                _selectionEnd = point;
                _draggingSelection = true;
                CaptureSelection(viewport);
                e.Use();
            }
            else if (type == EventType.MouseDrag && _draggingSelection)
            {
                _selectionEnd = SelectionPointAt(viewport, e.mousePosition);
                e.Use();
            }
            else if (type == EventType.MouseUp && _draggingSelection)
            {
                _selectionEnd = SelectionPointAt(viewport, e.mousePosition);
                _draggingSelection = false;
                ReleaseSelection();
                e.Use();
            }
        }

        int SelectionPointAt(Rect viewport, Vector2 mouse)
        {
            if (_selectionLines.Count == 0) return 0;

            float y = mouse.y - viewport.y + _scroll.Position.y;
            float x = mouse.x - viewport.x + _scroll.Position.x;
            int insertion = FirstVisibleSelectionLine(y);
            int first = Mathf.Max(0, insertion - 1);
            int last = Mathf.Min(_selectionLines.Count - 1, insertion);
            int lineIndex = first;
            float best = float.MaxValue;
            for (int i = first; i <= last; i++)
            {
                var line = _selectionLines[i];
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Width;
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                float distance = vertical * 10000f + horizontal;
                if (distance < best)
                {
                    best = distance;
                    lineIndex = i;
                }
            }

            var selected = _selectionLines[lineIndex];
            if (x <= selected.X) return selected.Start;
            if (x >= selected.X + selected.Width) return selected.End;

            for (int i = 0; i < selected.Text.Length; i++)
            {
                float left = selected.X + selected.Edges[i];
                float right = selected.X + selected.Edges[i + 1];
                if (x < (left + right) * 0.5f)
                    return selected.Start + i;
            }
            return selected.End;
        }

        void CaptureSelection(Rect viewport)
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = GUIUtility.GetControlID(FocusType.Passive, viewport);
            GUIUtility.hotControl = _selectionControl;
        }

        void ReleaseSelection()
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = 0;
        }

        void ClearSelection()
        {
            _selectionStart = 0;
            _selectionEnd = 0;
            _draggingSelection = false;
            ReleaseSelection();
        }

        void SelectAll()
        {
            _selectionStart = 0;
            _selectionEnd = SelectionLength();
            _draggingSelection = false;
            ReleaseSelection();
        }

        bool HasSelection => _selectionStart != _selectionEnd;

        void CopySelection()
        {
            if (!HasSelection) return;
            int start = Mathf.Min(_selectionStart, _selectionEnd);
            int end = Mathf.Max(_selectionStart, _selectionEnd);
            string source = SelectionSource();
            if (start < 0 || end > source.Length || end <= start) return;
            DaemonClipboard.Copy(source.Substring(start, end - start));
        }

        void OpenSelectionMenu()
        {
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !HasSelection;
            options.Add(copy);
            options.Add(new FloatMenuOption("Select all", SelectAll));
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        string SelectionSource()
        {
            string body = _task.Body ?? "";
            if (string.IsNullOrEmpty(_task.Note)) return body;
            return body + "\n\n" + _task.Note;
        }

        int SelectionLength()
        {
            int body = (_task?.Body ?? "").Length;
            string note = _task?.Note;
            return body + (string.IsNullOrEmpty(note) ? 0 : note.Length + 2);
        }

        string DialogueText()
        {
            var messages = new List<string>
            {
                "Message from " + SenderLabel(_task.From) + "  ·  " +
                    Timestamp(_task.CreatedMs),
                _task.Body ?? ""
            };
            if (!string.IsNullOrEmpty(_task.Note))
            {
                messages.Add("Latest note from " + SenderLabel(_task.To) + "  ·  " +
                    Timestamp(_task.UpdatedMs));
                messages.Add(_task.Note);
            }
            return string.Join("\n\n", messages.ToArray());
        }
    }
}
