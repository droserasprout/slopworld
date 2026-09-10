using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Full-screen browsing surface for the likes file. The file parser lives in JukeboxHistory
    // so this stays about rendering: search, selection, per-field copy, and a detail panel for
    // the long original metadata that a table row can only hint at. The file remains editable
    // through the button below; the table is read-only.
    public sealed class JukeboxHistoryView : ContentView
    {
        struct Columns
        {
            public float At;
            public float Source;
            public float Artist;
            public float Title;
            public float Original;
        }

        const float CellPad = 8f;
        const float HeaderH = 28f;
        static float RowH => Mathf.Max(UiWidgets.LineH + UiWidgets.GapXS, 28f);

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<JukeboxHistory.Entry> _all = new List<JukeboxHistory.Entry>();
        readonly List<JukeboxHistory.Entry> _view = new List<JukeboxHistory.Entry>();
        string _path = "";
        string _error;
        string _query = "";
        JukeboxHistory.Entry _selected;

        public override string Title => "Jukebox History";

        public static void Open()
        {
            TerminalWindow.ToggleContent(() => new JukeboxHistoryView());
        }

        public override void Opened() => Reload();

        public override void Closed() { }

        public override void Draw(Rect body)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;

                // Toolbar: the search field takes the middle, actions sit on the right.
                var toolbar = new Rect(body.x, body.y, body.width, UiWidgets.BtnH);
                var bar = new UiWidgets.Bar(toolbar);
                if (bar.Right("Edit file", UiWidgets.Btn.Ghost)) StoragePage.EditLikes();
                if (bar.Right("Refresh", UiWidgets.Btn.Ghost)) Reload();
                string query = UiWidgets.Field(bar.Rest(), "jukebox-history-search", _query);
                if (query != _query)
                {
                    _query = query;
                    ApplyFilter();
                }

                var status = new Rect(body.x, toolbar.yMax + UiWidgets.GapXS,
                    body.width, UiWidgets.LineH);
                DrawStatus(status);

                // The detail panel reserves space at the bottom only while a row is selected.
                float detailH = _selected == null
                    ? 0f : Mathf.Clamp(body.height * 0.32f, 150f, 220f);
                var detail = new Rect(body.x, body.yMax - detailH, body.width, detailH);

                float tableTop = status.yMax + UiWidgets.GapS;
                float tableBottom = detailH > 0f ? detail.y - UiWidgets.GapS : body.yMax;
                DrawTable(new Rect(body.x, tableTop, body.width, Mathf.Max(0f, tableBottom - tableTop)));

                if (detailH > 0f) DrawDetail(detail);
            }
        }

        void DrawStatus(Rect r)
        {
            string note;
            Color color = UiWidgets.Dim;
            if (_error != null)
            {
                note = _error;
                color = UiWidgets.Bad;
            }
            else if (_all.Count == 0)
            {
                note = "No likes yet  -  " + _path;
            }
            else if (_view.Count == _all.Count)
            {
                note = _all.Count + " tracks  -  " + _path;
            }
            else
            {
                note = _view.Count + " of " + _all.Count + " tracks";
            }

            GUI.color = color;
            UiWidgets.RowLabel(r, note);
            GUI.color = Color.white;
        }

        void DrawTable(Rect table)
        {
            Slab.Box(table, UiWidgets.Well, UiWidgets.Edge);
            var columns = Layout(table.width - 2f);
            var header = new Rect(table.x + 1f, table.y + 1f, table.width - 2f, HeaderH);
            Slab.Fill(header, UiWidgets.RowOn);
            DrawHeader(header, columns);
            Slab.Hairline(new Rect(header.x, header.yMax, header.width, 1f), UiWidgets.Edge);

            var list = new Rect(table.x + 1f, header.yMax + 1f,
                table.width - 2f, Mathf.Max(0f, table.yMax - header.yMax - 2f));

            if (_view.Count == 0)
            {
                DrawEmpty(list);
                return;
            }

            float contentH = Mathf.Max(list.height, _view.Count * RowH);
            var geometry = UiScrollBody.Measure(list, contentH,
                UiScrollbarReservation.WhenNeeded);
            using (_scroll.Scope(list, geometry.View))
            {
                for (int i = 0; i < _view.Count; i++)
                    DrawRow(new Rect(0f, i * RowH, geometry.View.width, RowH), _view[i], columns);
            }
        }

        // Three empty states read differently: a file that has never been liked into, a filter
        // that hides everything, and a read error already shown in the status line.
        void DrawEmpty(Rect list)
        {
            string message = _error != null
                ? "Could not read jukebox history."
                : _all.Count == 0
                    ? "No jukebox history yet."
                    : "No tracks match \"" + _query.Trim() + "\".";
            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(new Rect(list.x + CellPad, list.y + CellPad,
                list.width - CellPad * 2f, UiWidgets.LineH), message);
            GUI.color = Color.white;
        }

        static Columns Layout(float width)
        {
            float at = Mathf.Clamp(width * 0.17f, 120f, 190f);
            float source = Mathf.Clamp(width * 0.16f, 100f, 180f);
            float artist = Mathf.Clamp(width * 0.19f, 120f, 220f);
            float title = Mathf.Clamp(width * 0.25f, 150f, 320f);
            float original = Mathf.Max(80f, width - at - source - artist - title);
            return new Columns
            {
                At = at,
                Source = source,
                Artist = artist,
                Title = title,
                Original = original,
            };
        }

        static void DrawHeader(Rect r, Columns c)
        {
            float x = r.x;
            HeaderCell(new Rect(x, r.y, c.At, r.height), "When"); x += c.At;
            HeaderCell(new Rect(x, r.y, c.Source, r.height), "Source"); x += c.Source;
            HeaderCell(new Rect(x, r.y, c.Artist, r.height), "Artist"); x += c.Artist;
            HeaderCell(new Rect(x, r.y, c.Title, r.height), "Title"); x += c.Title;
            HeaderCell(new Rect(x, r.y, c.Original, r.height), "Original");
        }

        static void HeaderCell(Rect r, string label)
        {
            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(r.ContractedBy(CellPad, 0f), label);
            GUI.color = Color.white;
        }

        void DrawRow(Rect r, JukeboxHistory.Entry e, Columns c)
        {
            bool selected = e == _selected;
            bool over = RowChrome.Hover(r, selected, true, RowHoverPolicy.OverlayAware);

            float x = r.x;
            Cell(new Rect(x, r.y, c.At, r.height), DisplayAt(e.At), UiWidgets.Dim); x += c.At;
            Cell(new Rect(x, r.y, c.Source, r.height), e.Source, UiWidgets.Name); x += c.Source;
            Cell(new Rect(x, r.y, c.Artist, r.height), e.Artist, UiWidgets.Name); x += c.Artist;
            Cell(new Rect(x, r.y, c.Title, r.height), e.Title, UiWidgets.Lead); x += c.Title;
            Cell(new Rect(x, r.y, c.Original, r.height), e.Original, UiWidgets.Faint);

            if (over)
                TooltipHandler.TipRegion(r, DetailText(e) + "\n\nClick to inspect and copy.");

            // Clicking a row opens its detail; clicking the open one closes it again.
            if (UiWidgets.RowButton(r))
                _selected = selected ? null : e;
        }

        static void Cell(Rect r, string text, Color color)
        {
            GUI.color = string.IsNullOrEmpty(text) ? UiWidgets.Faint : color;
            UiWidgets.RowLabel(r.ContractedBy(CellPad, 0f),
                string.IsNullOrEmpty(text) ? "-" : text);
            GUI.color = Color.white;
        }

        // The selected row in full: every field copyable, and the original station metadata -
        // which the table can only truncate - given room to wrap.
        void DrawDetail(Rect r)
        {
            var e = _selected;
            if (e == null) return;

            Slab.Box(r, UiWidgets.Well, UiWidgets.Edge);
            var inner = r.ContractedBy(CellPad + 2f, CellPad);

            var head = new Rect(inner.x, inner.y, inner.width, UiWidgets.BtnH);
            var headBar = new UiWidgets.Bar(head);
            if (headBar.Right("Copy line", UiWidgets.Btn.Ghost)) Copy(e.Line, "\"" + e.Line + "\"");
            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(headBar.Rest(), "Details  -  click a field to copy it");
            GUI.color = Color.white;

            float y = head.yMax + UiWidgets.GapS;
            y = CopyField(inner, y, "When", DisplayAt(e.At), e.At);
            y = CopyField(inner, y, "Source", e.Source, e.Source);
            y = CopyField(inner, y, "Artist", e.Artist, e.Artist);
            y = CopyField(inner, y, "Title", e.Title, e.Title);

            // The original gets whatever height is left, wrapped, so a long ICY string stays
            // readable rather than clipping at the column edge.
            float originalH = Mathf.Max(RowH, inner.yMax - y);
            CopyField(inner, y, "Original", e.Original, e.Original, originalH, wrap: true);
        }

        // One labelled, click-to-copy value. Returns the y below it so the caller can stack.
        float CopyField(Rect area, float y, string label, string display, string value,
                        float height = -1f, bool wrap = false)
        {
            if (height < 0f) height = RowH;
            var row = new Rect(area.x, y, area.width, height);
            bool over = RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);

            const float LabelW = 78f;
            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(new Rect(row.x, row.y, LabelW, RowH), label);
            GUI.color = Color.white;

            var valueRect = new Rect(row.x + LabelW, row.y, row.width - LabelW, height);
            string text = string.IsNullOrEmpty(display) ? "-" : display;
            GUI.color = string.IsNullOrEmpty(display) ? UiWidgets.Faint : UiWidgets.Name;
            if (wrap)
            {
                UiWidgets.StatusLabel(valueRect, text,
                    string.IsNullOrEmpty(display) ? UiWidgets.Faint : UiWidgets.Name);
            }
            else
            {
                UiWidgets.RowLabel(valueRect, text);
            }
            GUI.color = Color.white;

            if (!string.IsNullOrEmpty(value))
            {
                if (over) TooltipHandler.TipRegion(row, "Click to copy");
                if (UiWidgets.RowButton(row)) Copy(value, label.ToLowerInvariant());
            }
            return row.yMax + UiWidgets.GapXS;
        }

        static string DetailText(JukeboxHistory.Entry e)
        {
            var lines = new List<string> { DisplayAt(e.At), e.Source, e.Line };
            if (!string.IsNullOrEmpty(e.Original) && e.Original != e.Line)
                lines.Add("Original: " + e.Original);
            lines.RemoveAll(string.IsNullOrEmpty);
            return string.Join("\n", lines.ToArray());
        }

        // Timestamps are stored as UTC ISO 8601; show them in local time so a browsing player
        // reads familiar wall-clock values. Anything unparseable falls back to a tidied string.
        static string DisplayAt(string at)
        {
            if (string.IsNullOrEmpty(at)) return "-";
            if (DateTime.TryParse(at, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return at.Replace("T", " ").TrimEnd('Z');
        }

        static void Copy(string text, string what)
        {
            if (string.IsNullOrEmpty(text)) return;
            DaemonClipboard.Copy(text,
                () => Messages.Message($"Jukebox: copied {what}", MessageTypeDefOf.SilentInput,
                    false), UiWidgets.Fail);
        }

        void Reload()
        {
            _path = Radio.LikesPath();
            _all.Clear();
            _error = null;
            try
            {
                if (File.Exists(_path))
                    _all.AddRange(JukeboxHistory.Parse(File.ReadAllText(_path)));
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] jukebox: could not read history: " + e);
                _error = "Could not read jukebox history";
            }
            ApplyFilter();
        }

        void ApplyFilter()
        {
            _view.Clear();
            string q = (_query ?? "").Trim();
            foreach (var e in _all)
                if (q.Length == 0 || Matches(e, q)) _view.Add(e);
            if (_selected != null && !_view.Contains(_selected)) _selected = null;
        }

        static bool Matches(JukeboxHistory.Entry e, string q) =>
            Has(e.Artist, q) || Has(e.Title, q) || Has(e.Source, q)
            || Has(e.At, q) || Has(e.Original, q);

        static bool Has(string text, string q) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
