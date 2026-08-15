using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The inventory and destructive half of private-state retention. The daemon owns both
    // the paths and the classification; the game draws opaque entries and hands a selected
    // path to Files, so the game never needs to read the host data directory itself.
    public class StoragePage : IOptionPage
    {
        class Entry
        {
            public string Kind;
            public string Key;
            public string Session;
            public string Path;
            public long Bytes;
            public long Modified;

            public static Entry FromJson(JVal j) => new Entry
            {
                Kind = j["kind"].AsString(),
                Key = j["key"].AsString(),
                Session = j["session"].IsNull ? null : j["session"].AsString(),
                Path = j["path"].AsString(),
                Bytes = j["bytes"].AsLong(),
                Modified = j["modified"].AsLong(),
            };
        }

        readonly SmoothScroll _scroll = new SmoothScroll();
        List<Entry> _entries = new List<Entry>();
        string _error;
        bool _loading;

        const float Pitch = 58f;
        const float LikesIconW = 22f;

        public void Load()
        {
            _loading = true;
            SlopClient.Get("/api/state", j =>
            {
                _entries = j["entries"].Items.Select(Entry.FromJson)
                    .OrderBy(e => KindRank(e.Kind))
                    .ThenByDescending(e => e.Modified)
                    .ThenBy(e => e.Session ?? e.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Key, StringComparer.Ordinal)
                    .ToList();
                _loading = false;
                _error = null;
            }, msg => { _loading = false; _error = msg; });
        }

        public void Draw(Rect rect)
        {
            var caption = new Rect(rect.x, rect.y,
                Mathf.Max(0f, rect.width - LikesIconW - SlopWidgets.GapS), SlopWidgets.RowH);
            SlopWidgets.PageCaption(caption, "Private state storage");

            var likes = new Rect(rect.xMax - LikesIconW,
                rect.y + (SlopWidgets.RowH - LikesIconW) / 2f, LikesIconW, LikesIconW);
            if (Mouse.IsOver(likes)) Slab.Fill(likes, SlopWidgets.Hover);
            var was = GUI.color;
            GUI.color = Mouse.IsOver(likes) ? Color.white : SlopWidgets.Dim;
            Widgets.ThingIcon(likes, SlopDefOf.SlopJukebox);
            GUI.color = was;
            TooltipHandler.TipRegion(likes, "Open liked songs in an editor.");
            if (Widgets.ButtonInvisible(likes)) EditLikes();

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(inner.x, inner.y, inner.width, SlopWidgets.LineH),
                $"{Human(_entries.Sum(e => e.Bytes))} total. Configured agents are retained; " +
                "deleted/reset state expires after 14 days.");
            GUI.color = Color.white;

            var list = new Rect(inner.x, inner.y + SlopWidgets.LineH + SlopWidgets.GapS,
                inner.width, inner.yMax - inner.y - SlopWidgets.LineH - SlopWidgets.GapS);
            var view = new Rect(0f, 0f, list.width - SlopWidgets.ScrollbarW,
                Mathf.Max(list.height, _entries.Count * Pitch));
            _scroll.Begin(list, view);
            for (int i = 0; i < _entries.Count; i++)
                DrawRow(new Rect(0f, i * Pitch, view.width, Pitch - 4f), _entries[i]);
            _scroll.End();

            if (_entries.Count == 0)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(list, _error ?? (_loading ? "Scanning..." : "No private state on disk."));
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Refresh", SlopWidgets.Btn.Ghost, !_loading)) Load();
            if (_error != null && _entries.Count > 0)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

        public static void EditLikes()
        {
            try
            {
                string path = Radio.LikesPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                FilesView.EditFile(null, path, "edit-jukebox.toml");
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] jukebox: could not open liked songs: " + e);
                SlopWidgets.Fail("could not open liked songs");
            }
        }

        void DrawRow(Rect r, Entry e)
        {
            bool over = SlopWidgets.HoverRow(r);
            if (over)
                TooltipHandler.TipRegion(r, "Open this private directory in the Files sidebar.");

            float actionW = 78f;
            float right = r.xMax - 6f;
            float labelW = Mathf.Max(80f, r.width - 190f);

            // Leave the action buttons out of the selection hit target. The whole label side
            // is one row, so an entry does not require a tiny click on its name.
            if (Widgets.ButtonInvisible(new Rect(r.x, r.y, labelW + 20f, r.height)))
                Focus(e);

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f, labelW, SlopWidgets.LineH),
                e.Session ?? e.Key);
            GUI.color = SlopWidgets.Dim;
            string note = e.Kind == "active" ? "configured agent" :
                e.Kind == "orphan" ? "unclaimed orphan state" :
                e.Session != null ? $"trash for {e.Session}" : "trash (agent removed)";
            SlopWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f + SlopWidgets.LineH,
                labelW, SlopWidgets.LineH), $"{note}  -  {Human(e.Bytes)}  -  {When(e.Modified)}");
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

        static void Focus(Entry e)
        {
            if (string.IsNullOrEmpty(e.Path))
            {
                SlopWidgets.Fail("private-state path is unavailable");
                return;
            }
            FilesView.FocusDirectory(e.Path, e.Session ?? e.Key);
        }

        void ConfirmReset(Entry e)
        {
            Find.WindowStack.Add(SlopConfirmDialog.Create(
                $"Reset private state for '{e.Session}'? The agent stops and this {Human(e.Bytes)} " +
                "copy moves to recoverable trash for 14 days.",
                () => SlopClient.Post($"/api/sessions/{Uri.EscapeDataString(e.Session)}/state/reset",
                    null, _ => { SessionHub.Instance.Refresh(); Load(); }, msg => _error = msg),
                destructive: true));
        }

        void ConfirmDelete(Entry e)
        {
            Find.WindowStack.Add(SlopConfirmDialog.Create(
                $"Permanently delete {Human(e.Bytes)} of {e.Kind} private state? This cannot be undone.",
                () => SlopClient.Delete($"/api/state/{Uri.EscapeDataString(e.Kind)}/" +
                        Uri.EscapeDataString(e.Key), _ => Load(), msg => _error = msg),
                destructive: true));
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

        static int KindRank(string kind) => kind == "active" ? 0 : kind == "orphan" ? 1 : 2;

        static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static string When(long unixSeconds)
        {
            if (unixSeconds <= 0) return "unknown age";
            long now = (long)(DateTime.UtcNow - Epoch).TotalSeconds;
            long elapsed = Math.Max(0L, now - unixSeconds);
            long days = elapsed / 86400L;
            long hours = (elapsed % 86400L) / 3600L;
            long minutes = (elapsed % 3600L) / 60L;
            if (days > 0) return $"{days}d {hours}h {minutes}m ago";
            if (hours > 0) return $"{hours}h {minutes}m ago";
            return $"{minutes}m ago";
        }
    }
}
