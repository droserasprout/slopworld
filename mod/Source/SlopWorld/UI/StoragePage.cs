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
        readonly AsyncLoadState<List<Entry>> _load =
            new AsyncLoadState<List<Entry>>();
        static readonly List<Entry> EmptyEntries = new List<Entry>();

        List<Entry> _entries => _load.Value ?? EmptyEntries;
        string _error { get => _load.Error; set => _load.SetError(value); }
        bool _loading => _load.Loading;

        const float Pitch = 58f;
        const float LikesIconW = 22f;

        public void Load()
        {
            _load.Load((ok, fail) => DaemonClient.Get("/api/state", j => ok(
                j["entries"].Items.Select(Entry.FromJson)
                    .OrderBy(e => KindRank(e.Kind))
                    .ThenByDescending(e => e.Modified)
                    .ThenBy(e => e.Session ?? e.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Key, StringComparer.Ordinal)
                    .ToList()), fail));
        }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var likes = new Rect(rect.xMax - LikesIconW,
                rect.y + (UiWidgets.RowH - LikesIconW) / 2f, LikesIconW, LikesIconW);
            if (Mouse.IsOver(likes)) Slab.Fill(likes, UiWidgets.Hover);
            var was = GUI.color;
            GUI.color = Mouse.IsOver(likes) ? Color.white : UiWidgets.Dim;
            Widgets.ThingIcon(likes, ModDefOf.SlopJukebox);
            GUI.color = was;
            TooltipHandler.TipRegion(likes, "Open jukebox history.");
            if (Widgets.ButtonInvisible(likes)) JukeboxHistoryView.Open();

            var body = UiWidgets.PageBody(rect);
            var inner = body.ContractedBy(UiWidgets.GapM);

            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(new Rect(inner.x, inner.y, inner.width, UiWidgets.LineH),
                $"{Human(_entries.Sum(e => e.Bytes))} total. Configured agents are retained; " +
                "deleted/reset state expires after 14 days.");
            GUI.color = Color.white;

            var list = new Rect(inner.x, inner.y + UiWidgets.LineH + UiWidgets.GapS,
                inner.width, inner.yMax - inner.y - UiWidgets.LineH - UiWidgets.GapS);
            var view = new Rect(0f, 0f, list.width - UiWidgets.ScrollbarW,
                Mathf.Max(list.height, _entries.Count * Pitch));
            using (_scroll.Scope(list, view))
                for (int i = 0; i < _entries.Count; i++)
                    DrawRow(new Rect(0f, i * Pitch, view.width, Pitch - 4f), _entries[i]);

            if (_entries.Count == 0)
            {
                GUI.color = _error != null ? UiWidgets.Bad : UiWidgets.Dim;
                Widgets.Label(list, _error ?? (_loading ? "Scanning..." : "No private state on disk."));
                GUI.color = Color.white;
            }

            var foot = new UiWidgets.Bar(UiWidgets.FooterBar(rect));
            if (foot.Left("Refresh", UiWidgets.Btn.Ghost, !_loading)) Load();
            if (_error != null && _entries.Count > 0)
            {
                GUI.color = UiWidgets.Bad;
                UiWidgets.RowLabel(foot.Rest(), _error);
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
                UiWidgets.Fail("could not open liked songs");
            }
        }

        void DrawRow(Rect r, Entry e)
        {
            bool over = RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);
            if (over)
                TooltipHandler.TipRegion(r, "Open this private directory in the Files sidebar.");

            float actionW = 78f;
            float right = r.xMax - 6f;
            float labelW = Mathf.Max(80f, r.width - 190f);

            // Leave the action buttons out of the selection hit target. The whole label side
            // is one row, so an entry does not require a tiny click on its name.
            if (Widgets.ButtonInvisible(new Rect(r.x, r.y, labelW + 20f, r.height)))
                Focus(e);

            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f, labelW, UiWidgets.LineH),
                e.Session ?? e.Key);
            GUI.color = UiWidgets.Dim;
            string note = e.Kind == "active" ? "configured agent" :
                e.Kind == "orphan" ? "unclaimed orphan state" :
                e.Session != null ? $"trash for {e.Session}" : "trash (agent removed)";
            UiWidgets.RowLabel(new Rect(r.x + 10f, r.y + 5f + UiWidgets.LineH,
                labelW, UiWidgets.LineH), $"{note}  -  {Human(e.Bytes)}  -  {When(e.Modified)}");
            GUI.color = Color.white;

            if (e.Kind == "active")
            {
                if (UiWidgets.Button(new Rect(right - actionW, r.y + 10f, actionW,
                        UiWidgets.RowBtnH), "Reset", UiWidgets.Btn.Ghost))
                    ConfirmReset(e);
                return;
            }

            if (e.Kind == "trash" && e.Session != null)
            {
                if (UiWidgets.Button(new Rect(right - actionW * 2f - UiWidgets.GapXS,
                        r.y + 10f, actionW, UiWidgets.RowBtnH), "Restore"))
                    Restore(e);
            }
            if (UiWidgets.Button(new Rect(right - actionW, r.y + 10f, actionW,
                    UiWidgets.RowBtnH), "Delete", UiWidgets.Btn.Danger))
                ConfirmDelete(e);
        }

        static void Focus(Entry e)
        {
            if (string.IsNullOrEmpty(e.Path))
            {
                UiWidgets.Fail("private-state path is unavailable");
                return;
            }
            FilesView.FocusDirectory(e.Path, e.Session ?? e.Key);
        }

        public static void FocusAgent(string name)
        {
            DaemonClient.Get("/api/state", j =>
            {
                var entry = j["entries"].Items
                    .Select(Entry.FromJson)
                    .FirstOrDefault(e => e.Kind == "active" && e.Session == name);
                if (entry == null)
                {
                    UiWidgets.Fail($"private storage for '{name}' is unavailable");
                    return;
                }
                Focus(entry);
            }, UiWidgets.Fail);
        }

        void ConfirmReset(Entry e)
        {
            Find.WindowStack.Add(ConfirmDialog.Create(
                $"Reset private state for '{e.Session}'? The agent stops and this {Human(e.Bytes)} " +
                "copy moves to recoverable trash for 14 days.",
                () => DaemonClient.Post($"/api/sessions/{Uri.EscapeDataString(e.Session)}/state/reset",
                    null, _ => { SessionHub.Instance.Refresh(); Load(); }, msg => _error = msg),
                destructive: true));
        }

        void ConfirmDelete(Entry e)
        {
            Find.WindowStack.Add(ConfirmDialog.Create(
                $"Permanently delete {Human(e.Bytes)} of {e.Kind} private state? This cannot be undone.",
                () => DaemonClient.Delete($"/api/state/{Uri.EscapeDataString(e.Kind)}/" +
                        Uri.EscapeDataString(e.Key), _ => Load(), msg => _error = msg),
                destructive: true));
        }

        void Restore(Entry e)
        {
            DaemonClient.Post($"/api/state/trash/{Uri.EscapeDataString(e.Key)}/restore", null,
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
