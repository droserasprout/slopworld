using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Inventory and the explicit destructive half of private-state retention. The daemon owns
    // both the paths and the classification; the game only draws opaque keys and asks for an
    // operation on one, so Wine never needs access to the host data directory.
    public class StateStorageDialog : SlopWindow
    {
        class Entry
        {
            public string Kind;
            public string Key;
            public string Session;
            public long Bytes;

            public static Entry FromJson(JVal j) => new Entry
            {
                Kind = j["kind"].AsString(),
                Key = j["key"].AsString(),
                Session = j["session"].IsNull ? null : j["session"].AsString(),
                Bytes = j["bytes"].AsLong(),
            };
        }

        readonly SmoothScroll _scroll = new SmoothScroll();
        List<Entry> _entries = new List<Entry>();
        string _error;
        bool _loading;

        const float Pitch = 58f;

        public StateStorageDialog() => Load();

        public override Vector2 InitialSize => new Vector2(720f, 620f);

        void Load()
        {
            _loading = true;
            SlopClient.Get("/api/state", j =>
            {
                _entries = j["entries"].Items.Select(Entry.FromJson).ToList();
                _loading = false;
                _error = null;
            }, msg => { _loading = false; _error = msg; });
        }

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, "Private state storage");
            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;

            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(rect.x, top, rect.width, SlopWidgets.LineH),
                $"{Human(_entries.Sum(e => e.Bytes))} total. Configured agents are retained; " +
                "deleted/reset state expires after 14 days.");
            GUI.color = Color.white;
            top += SlopWidgets.LineH + SlopWidgets.GapS;

            var body = new Rect(rect.x, top, rect.width,
                rect.yMax - SlopWidgets.BtnH - SlopWidgets.GapS - top);
            var view = new Rect(0f, 0f, body.width - SlopWidgets.ScrollbarW,
                Mathf.Max(body.height, _entries.Count * Pitch));
            _scroll.Begin(body, view);
            for (int i = 0; i < _entries.Count; i++) DrawRow(new Rect(0f, i * Pitch, view.width, Pitch - 4f), _entries[i]);
            _scroll.End();

            if (_entries.Count == 0)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(body, _error ?? (_loading ? "Scanning..." : "No private state on disk."));
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Refresh", SlopWidgets.Btn.Ghost, !_loading)) Load();
            if (foot.Right("Close", SlopWidgets.Btn.Primary)) Close();
            if (_error != null && _entries.Count > 0)
            {
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

        void DrawRow(Rect r, Entry e)
        {
            SlopWidgets.RowChrome(r);
            float actionW = 78f;
            float right = r.xMax - 6f;
            float labelW = Mathf.Max(80f, r.width - 190f);

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f, labelW, SlopWidgets.LineH),
                e.Session ?? e.Key);
            GUI.color = SlopWidgets.Dim;
            string note = e.Kind == "active" ? "configured agent" :
                e.Kind == "orphan" ? "unclaimed orphan state" :
                e.Session != null ? $"trash for {e.Session}" : "trash (agent removed)";
            SlopWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f + SlopWidgets.LineH,
                labelW, SlopWidgets.LineH), $"{note}  -  {Human(e.Bytes)}");
            GUI.color = Color.white;

            if (e.Kind == "active")
            {
                if (SlopWidgets.Button(new Rect(right - actionW, r.y + 10f, actionW,
                        SlopWidgets.RowBtnH), "Reset", SlopWidgets.Btn.Ghost))
                    ConfirmReset(e);
                return;
            }

            if (e.Kind == "trash" && e.Session != null)
            {
                if (SlopWidgets.Button(new Rect(right - actionW * 2f - SlopWidgets.GapXS,
                        r.y + 10f, actionW, SlopWidgets.RowBtnH), "Restore"))
                    Restore(e);
            }
            if (SlopWidgets.Button(new Rect(right - actionW, r.y + 10f, actionW,
                    SlopWidgets.RowBtnH), "Delete", SlopWidgets.Btn.Danger))
                ConfirmDelete(e);
        }

        void ConfirmReset(Entry e)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                $"Reset private state for '{e.Session}'? The agent stops and this {Human(e.Bytes)} " +
                "copy moves to recoverable trash for 14 days.",
                () => SlopClient.Post($"/api/sessions/{Uri.EscapeDataString(e.Session)}/state/reset",
                    null, _ => { SessionHub.Instance.Refresh(); Load(); }, msg => _error = msg),
                destructive: true));
        }

        void ConfirmDelete(Entry e)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                $"Permanently delete {Human(e.Bytes)} of {e.Kind} private state? This cannot be undone.",
                () => SlopClient.Delete($"/api/state/{Uri.EscapeDataString(e.Kind)}/{Uri.EscapeDataString(e.Key)}",
                    _ => Load(), msg => _error = msg), destructive: true));
        }

        void Restore(Entry e)
        {
            SlopClient.Post($"/api/state/trash/{Uri.EscapeDataString(e.Key)}/restore", null,
                _ => { SessionHub.Instance.Refresh(); Load(); }, msg => _error = msg);
        }

        static string Human(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024d && unit < units.Length - 1) { value /= 1024d; unit++; }
            return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
        }
    }
}
