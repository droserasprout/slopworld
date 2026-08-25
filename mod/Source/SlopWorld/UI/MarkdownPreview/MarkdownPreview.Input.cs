using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mechanical split: MarkdownPreview.Input methods.
    public sealed partial class MarkdownPreview
    {
        void ClickLinks(Rect body)
        {
            var e = Event.current;
            if (e == null) return;

            foreach (var hit in _links)
            {
                var screen = new Rect(body.x + hit.Rect.x,
                    body.y + hit.Rect.y - _scroll.Position.y, hit.Rect.width, hit.Rect.height);
                if (Mouse.IsOver(screen))
                    TooltipHandler.TipRegion(screen, (hit.Url ?? hit.LocalPath) +
                        "\n\nCtrl+click to open it");
                if (e.rawType == EventType.MouseDown && e.button == 0 && e.control &&
                    screen.Contains(e.mousePosition))
                {
                    if (hit.LocalPath != null) OpenLocalLink(hit.LocalPath);
                    else if (!string.IsNullOrEmpty(hit.Url)) Application.OpenURL(hit.Url);
                    e.Use();
                    return;
                }
            }
        }

        void OpenLocalLink(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            if (!FilesView.IsText(name))
            {
                SlopWidgets.Fail("binary local links are not previewable");
                return;
            }
            if (FilesView.IsMarkdown(name))
            {
                MarkdownPreview.Open(_project, path, name);
                return;
            }

            FilesView.ViewFile(_project, path, "link-" + name);
        }

        void HandleInput(Rect body)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown ||
                (e.type == EventType.Used && e.rawType == EventType.KeyDown))
            {
                HandleKey(e);
                return;
            }

            if (e.button == 1 && e.type == EventType.MouseDown && body.Contains(e.mousePosition))
            {
                OpenMenu();
                e.Use();
                return;
            }

            if (e.button != 0) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    HandleMouseDown(body, e);
                    return;

                case EventType.MouseDrag:
                    HandleMouseDrag(body, e);
                    return;

                case EventType.MouseUp:
                    HandleMouseUp(body, e);
                    return;
            }
        }

        void HandleMouseDown(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;
            var point = SelectionPointAt(body, e.mousePosition);
            if (e.clickCount >= 3)
            {
                SelectLine(point.y);
                e.Use();
                return;
            }
            if (e.clickCount >= 2)
            {
                DoubleClickSelect(point);
                e.Use();
                return;
            }

            bool extend = e.shift && _hasSel;
            _selA = extend ? _selA : point;
            _selB = point;
            _dragging = true;
            _wordDragging = false;
            _hasSel = extend && _selA != _selB;
            e.Use();
            return;
        }

        void HandleMouseDrag(Rect body, Event e)
        {
            if (!_dragging) return;
            var drag = SelectionPointAt(body, e.mousePosition);
            if (_wordDragging) UpdateWordSelection(drag);
            else
            {
                _selB = drag;
                _hasSel = _selA != _selB;
            }
            e.Use();
            return;
        }

        void HandleMouseUp(Rect body, Event e)
        {
            if (!_dragging) return;
            var up = SelectionPointAt(body, e.mousePosition);
            if (_wordDragging)
            {
                UpdateWordSelection(up);
                _wordDragging = false;
                _dragging = false;
                if (_hasSel) CopySelection();
            }
            else
            {
                _dragging = false;
                _selB = up;
                if (_selA != _selB)
                {
                    _hasSel = true;
                    CopySelection();
                }
                else _hasSel = false;
            }
            e.Use();
            return;
        }

        void HandleKey(Event e)
        {
            if (!e.control || e.alt) return;

            if (e.keyCode == KeyCode.C)
            {
                if (_hasSel) CopySelection();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.A)
            {
                SelectAll();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.V)
            {
                TerminalWindow.PasteClipboardToAgent();
                e.Use();
            }
        }

        Vector2Int SelectionPointAt(Rect body, Vector2 mouse)
        {
            if (_selectionLines.Count == 0) return Vector2Int.zero;

            float y = mouse.y - body.y + _scroll.Position.y;
            float x = mouse.x - body.x + _scroll.Position.x;
            int insertion = 0;
            int low = 0;
            int high = _selectionLines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_selectionLines[middle].Y < y) low = middle + 1;
                else high = middle;
            }
            insertion = low;

            int first = Mathf.Max(0, insertion - 1);
            int last = Mathf.Min(_selectionLines.Count - 1, insertion);
            if (first < _selectionLines.Count)
            {
                while (first > 0 && Mathf.Approximately(
                    _selectionLines[first - 1].Y, _selectionLines[first].Y)) first--;
                while (last + 1 < _selectionLines.Count && Mathf.Approximately(
                    _selectionLines[last + 1].Y, _selectionLines[last].Y)) last++;
            }

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
            EnsureEdges(selected);
            if (x <= selected.X) return new Vector2Int(0, lineIndex);
            if (x >= selected.X + selected.Edges[selected.Text.Length])
                return new Vector2Int(selected.Text.Length, lineIndex);

            for (int i = 0; i < selected.Text.Length; i++)
            {
                float left = selected.X + selected.Edges[i];
                float right = selected.X + selected.Edges[i + 1];
                if (x < (left + right) * 0.5f)
                    return new Vector2Int(i, lineIndex);
            }
            return new Vector2Int(selected.Text.Length, lineIndex);
        }

        void DoubleClickSelect(Vector2Int point)
        {
            if (point.y < 0 || point.y >= _selectionLines.Count) return;
            var line = _selectionLines[point.y];
            if (line.Text.Length == 0) { ClearSelection(); return; }

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            _wordStart = new Vector2Int(start, point.y);
            _wordEnd = new Vector2Int(end, point.y);
            _selA = _wordStart;
            _selB = _wordEnd;
            _hasSel = true;
            _dragging = true;
            _wordDragging = true;
            CopySelection();
        }

        void UpdateWordSelection(Vector2Int point)
        {
            if (_selectionLines.Count == 0) return;
            int lineIndex = Mathf.Clamp(point.y, 0, _selectionLines.Count - 1);
            var line = _selectionLines[lineIndex];
            if (line.Text.Length == 0) return;

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            var destinationStart = new Vector2Int(start, lineIndex);
            var destinationEnd = new Vector2Int(end, lineIndex);
            if (Before(point, _wordStart))
            {
                _selA = destinationStart;
                _selB = _wordEnd;
            }
            else
            {
                _selA = _wordStart;
                _selB = destinationEnd;
            }
            _hasSel = _selA != _selB;
        }

        void SelectLine(int line)
        {
            if (line < 0 || line >= _selectionLines.Count) return;
            int length = _selectionLines[line].Text.Length;
            if (length == 0) { ClearSelection(); return; }
            _selA = new Vector2Int(0, line);
            _selB = new Vector2Int(length, line);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopySelection();
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';

        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
            _wordDragging = false;
        }

        void SelectAll()
        {
            if (_selectionLines.Count == 0) return;
            _selA = Vector2Int.zero;
            int last = _selectionLines.Count - 1;
            _selB = new Vector2Int(_selectionLines[last].Text.Length, last);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopyText(SelectionText().TrimEnd('\n'));
        }

        void CopySelection()
        {
            if (_hasSel) CopyText(SelectionText());
        }

        string SelectionText()
        {
            if (_selectionLines.Count == 0 || !_hasSel) return "";
            OrderedSelection(out var a, out var b);
            int first = Mathf.Clamp(a.y, 0, _selectionLines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _selectionLines.Count - 1);
            var output = new System.Text.StringBuilder();
            for (int i = first; i <= last; i++)
            {
                string text = _selectionLines[i].Text;
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : text.Length;
                start = Mathf.Clamp(start, 0, text.Length);
                end = Mathf.Clamp(end, start, text.Length);
                if (end > start) output.Append(text.Substring(start, end - start));
                if (i < last) output.Append('\n');
            }
            return output.ToString();
        }

        void OrderedSelection(out Vector2Int a, out Vector2Int b)
        {
            a = _selA;
            b = _selB;
            if (Before(b, a))
            {
                var temp = a;
                a = b;
                b = temp;
            }
        }

        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            SlopClipboard.Copy(text, null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        void OpenMenu()
        {
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSel;
            options.Add(copy);

            var paste = new FloatMenuOption("Paste", TerminalWindow.PasteClipboardToAgent);
            paste.Disabled = !TerminalWindow.CanPasteClipboardToAgent;
            options.Add(paste);
            options.Add(new FloatMenuOption("Select all", SelectAll));
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        bool TryResolveLink(string source, out string external, out string local)
        {
            external = null;
            local = null;
            source = HtmlDecode(source).Trim();
            if (source.Length == 0 || source.StartsWith("#", StringComparison.Ordinal)) return false;

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                !string.IsNullOrEmpty(uri.Scheme))
            {
                if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, "mailto", StringComparison.OrdinalIgnoreCase))
                {
                    external = source;
                    return true;
                }
                return false;
            }

            // Relative links are useful in project README files. Keep them inside the owning
            // project so a document cannot turn a Ctrl+click into an arbitrary root-file read.
            if (string.IsNullOrEmpty(_project)) return false;
            string root = SessionHub.Instance.Project(_project)?.Dir;
            if (string.IsNullOrEmpty(root)) return false;

            int fragment = source.IndexOf('#');
            if (fragment >= 0) source = source.Substring(0, fragment);
            int query = source.IndexOf('?');
            if (query >= 0) source = source.Substring(0, query);
            if (source.Length == 0) return false;

            try
            {
                string relative = source.Replace('/', System.IO.Path.DirectorySeparatorChar);
                string candidate = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(relative)
                    ? relative : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_path) ?? root,
                        relative));
                if (!IsInsideProject(candidate)) return false;

                local = candidate;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        bool IsInsideProject(string candidate)
        {
            if (string.IsNullOrEmpty(_project)) return false;
            string root = SessionHub.Instance.Project(_project)?.Dir;
            if (string.IsNullOrEmpty(root)) return false;
            try
            {
                string normalizedRoot = System.IO.Path.GetFullPath(root).TrimEnd(
                    System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

    }
}
