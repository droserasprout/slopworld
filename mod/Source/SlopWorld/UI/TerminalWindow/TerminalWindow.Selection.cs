using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TerminalWindow selection, clipboard, and context-menu helpers.
    public partial class TerminalWindow
    {
        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
            _selectionMoved = false;
            _wordDragging = false;
        }

        // Both ends inclusive, the way a dragged selection states them.
        void SelectSpan(int row, int c0, int c1)
        {
            _selA = new Vector2Int(c0, row);
            _selB = new Vector2Int(c1, row);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopySelection();
        }

        // A word, or the run of identical characters a non-word cell sits in.
        void DoubleClickSelect(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            var cells = TerminalColumns.Cells(buf.Runs[cell.y]);
            int len = TerminalColumns.ContentColumns(cells);
            if (cell.x < 0 || cell.x >= len) { ClearSelection(); return; }

            char anchor = TerminalColumns.Glyph(cells, cell.x);
            bool word = IsWordChar(anchor);
            int c0 = cell.x, c1 = cell.x;
            while (c0 > 0 && SameClass(TerminalColumns.Glyph(cells, c0 - 1), anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(TerminalColumns.Glyph(cells, c1 + 1), anchor, word)) c1++;
            _wordStart = new Vector2Int(c0, cell.y);
            _wordEnd = new Vector2Int(c1, cell.y);
            _selA = _wordStart;
            _selB = _wordEnd;
            _hasSel = true;
            _dragging = true;
            _wordDragging = true;
            CopySelection();
        }

        void UpdateWordSelection(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            var cells = TerminalColumns.Cells(buf.Runs[cell.y]);
            int len = TerminalColumns.ContentColumns(cells);
            if (len == 0) return;
            int x = Mathf.Clamp(cell.x, 0, len - 1);
            char anchor = TerminalColumns.Glyph(cells, x);
            bool word = IsWordChar(anchor);
            int c0 = x, c1 = x;
            while (c0 > 0 && SameClass(TerminalColumns.Glyph(cells, c0 - 1), anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(TerminalColumns.Glyph(cells, c1 + 1), anchor, word)) c1++;

            var destinationStart = new Vector2Int(c0, cell.y);
            var destinationEnd = new Vector2Int(c1, cell.y);
            if (Before(cell, _wordStart))
            {
                _selA = destinationStart;
                _selB = _wordEnd;
            }
            else
            {
                _selA = _wordStart;
                _selB = destinationEnd;
            }
            _hasSel = true;
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        // The row, not the logical line: the daemon does not mark where one wrapped.
        void TripleClickSelect(int row)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (row < 0 || row >= buf.Runs.Length) return;

            int len = TerminalColumns.ContentColumns(TerminalColumns.Cells(buf.Runs[row]));
            if (len == 0) { ClearSelection(); return; }
            SelectSpan(row, 0, len - 1);
        }

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';

        Vector2Int CellAt(Rect body, Vector2 m)
        {
            SyncSnap();
            float cw = DisplayCellW(), ch = TerminalFont.CellH;
            if (cw <= 0.01f || ch <= 0.01f) return Vector2Int.zero;
            int col = Mathf.FloorToInt((m.x - body.x) / cw);
            int row = Mathf.FloorToInt((m.y - body.y) / ch);
            return new Vector2Int(col, row);
        }

        ScreenBuf DisplayedBuf() => DisplayedScreen();

        void CopySelection()
        {
            var buf = DisplayedBuf();
            if (buf != null) CopyText(SelectionText(buf));
        }

        // Trailing newlines go: a screen is padded to its row count, so an app half a screen
        // tall would copy the blank half with it.
        void SelectAll()
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return;

            EnsureRuns(buf);
            _selA = Vector2Int.zero;
            _selB = new Vector2Int(buf.Cols, buf.Runs.Length - 1);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopyText(SelectionText(buf).TrimEnd('\n'));
        }

        // The *host's* clipboard, through the daemon: on this Unity player
        // GUIUtility.systemCopyBuffer is as often the process's own buffer as the desktop's.
        // An agent copying on its own behalf goes via OSC 52 instead, never through here.
        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            GUIUtility.systemCopyBuffer = text;
            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}", null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        // The clipboard errands, which never had a button anywhere. Nothing that ends an agent
        // is here - a menu opened to copy a line is the wrong place to find it.
        void OpenMenu(string url)
        {
            var options = new List<FloatMenuOption>();

            // First when there is one: the pointer is already on it, and the right button is
            // the road for anyone who never learned Ctrl+click.
            if (url != null)
            {
                options.Add(new FloatMenuOption("Open " + url.Truncate(360f), () => OpenUrl(url)));
                options.Add(new FloatMenuOption("Copy link", () => CopyText(url)));
            }

            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSel;
            options.Add(copy);
            options.Add(new FloatMenuOption("Paste", () => { JumpToLive(); PasteClipboard(); }));
            var info = SessionHub.Instance.Get(_name);
            var breadcrumbs = AllBreadcrumbs();
            var breadcrumbMenu = new SlopSubmenu("Breadcrumbs",
                () => BreadcrumbOptions(breadcrumbs));
            breadcrumbMenu.Disabled = info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            options.Add(new FloatMenuOption("Select all", SelectAll));

            if (_scrollOff > 0)
                options.Add(new FloatMenuOption("Back to the live view", () =>
                {
                    JumpToLive();
                    ClearSelection();
                }));

            OpenOverPane(new SlopMenu(options));
        }

        static List<string> AllBreadcrumbs() => SessionHub.Instance.Shortcuts
            .Where(s => s.Kind == ShortcutKind.Breadcrumb)
            .Select(s => s.Name)
            .ToList();

        List<FloatMenuOption> BreadcrumbOptions(List<string> names)
        {
            var options = new List<FloatMenuOption>();
            foreach (string name in names)
            {
                string picked = name;
                options.Add(new FloatMenuOption(picked, () =>
                {
                    JumpToLive();
                    SessionHub.Instance.PasteBreadcrumb(_name, picked,
                        Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
                }));
            }
            return options;
        }

        // Falls back to the game's own buffer. A round trip, so the paste lands a frame or
        // two later.
        void PasteClipboard()
        {
            string name = _name;
            SlopClient.Get("/api/clipboard",
                j => Deliver(name, j["text"].AsString()),
                _ => Deliver(name, null));
        }

        static void Deliver(string name, string text)
        {
            if (string.IsNullOrEmpty(text)) text = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Paste(name, text);
        }

        void OrderedSel(out Vector2Int a, out Vector2Int b)
        {
            a = _selA;
            b = _selB;
            if (b.y < a.y || (b.y == a.y && b.x < a.x)) { var t = a; a = b; b = t; }
        }

        string SelectionText(ScreenBuf buf)
        {
            EnsureRuns(buf);
            OrderedSel(out var a, out var b);
            int rows = buf.Runs.Length;
            if (rows == 0) return "";

            var sb = new StringBuilder();
            int r0 = Mathf.Clamp(a.y, 0, rows - 1);
            int r1 = Mathf.Clamp(b.y, 0, rows - 1);
            for (int row = r0; row <= r1; row++)
            {
                var cells = TerminalColumns.Cells(buf.Runs[row]);
                int len = TerminalColumns.ContentColumns(cells);
                int startCol = row == a.y ? Mathf.Max(0, a.x) : 0;
                // The head cell is inclusive, matching the highlight.
                int endCol = row == b.y ? b.x + 1 : len;
                startCol = Mathf.Clamp(startCol, 0, len);
                endCol = Mathf.Clamp(endCol, 0, len);
                // Slice takes an inclusive last column and drops reserved wide-char columns.
                if (endCol > startCol) sb.Append(TerminalColumns.Slice(cells, startCol, endCol - 1));
                if (row < r1) sb.Append('\n');
            }
            return sb.ToString();
        }

        // Colors are resolved into the runs at parse time, so a scheme change is a re-parse:
        // without it an idle pane keeps the old palette until the agent next writes, which on
        // an idle agent is never.
        static void EnsureRuns(ScreenBuf buf)
        {
            if (buf.Runs != null && buf.RunsRev == TerminalTheme.Rev) return;
            buf.Runs = Sgr.ParseLines(buf.Lines, buf.Cols);
            buf.RunsRev = TerminalTheme.Rev;
        }

    }
}
