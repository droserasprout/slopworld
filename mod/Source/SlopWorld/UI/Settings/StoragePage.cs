using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The inventory and destructive half of private-state retention. The daemon owns both
    // the paths and the classification. The game draws opaque entries and hands a selected
    // path to Files, so the game never needs to read the host data directory itself.
    public class StoragePage : IOptionPage, IDisposable
    {
        class Entry
        {
            public string Kind;
            public string Key;
            public string Session;
            public string Project;
            public bool Cache => Kind == "cache-managed" || Kind == "cache-external";
            public string Path;
            public long Bytes;
            public long Modified;

            public static Entry FromWire(Wire.StoredState j) =>
                new Entry
                {
                    Kind = j.Kind,
                    Key = j.Key,
                    Session = !j.HasSession ? null : j.Session,
                    Project = !j.HasProject ? null : j.Project,
                    Path = j.Path,
                    Bytes = (long)j.Bytes,
                    Modified = (long)j.Modified,
                };
        }

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly StorageLoadState<List<Entry>> _load = new StorageLoadState<List<Entry>>();
        static readonly List<Entry> EmptyEntries = new List<Entry>();
        bool _disposed;
        string _selectedKind;
        string _selectedKey;
        string _pendingAgent;

        List<Entry> _entries => _load.Value ?? EmptyEntries;
        string _actionError;
        string _error
        {
            get => _actionError ?? _load.Error;
            set => _actionError = value;
        }
        bool _loading => _load.Loading;
        bool _hasTrash => _entries.Any(e => e.Kind == "trash");

        public void Load()
        {
            if (_disposed)
                return;
            _actionError = null;
            _load.Load(RequestInventory);
        }

        static void RequestInventory(
            bool measureSizes,
            Action<List<Entry>> loaded,
            Action<string> failed
        )
        {
            // Large shared caches can take longer than the normal HTTP deadline. The
            // shallow list stays usable while the measured request runs in the background.
            string route = WireProtocol.Routes.State + (measureSizes ? "" : "?sizes=false");
            int timeoutMs = measureSizes ? 60_000 : 5_000;
            DaemonClient.Get<Wire.StoredStates>(
                route,
                inventory => loaded(SortedEntries(inventory)),
                failed,
                timeoutMs: timeoutMs
            );
        }

        static List<Entry> SortedEntries(Wire.StoredStates inventory) =>
            inventory
                .Entries.Select(Entry.FromWire)
                .OrderBy(e => KindRank(e.Kind))
                .ThenByDescending(e => e.Modified)
                .ThenBy(e => e.Session ?? e.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .ToList();

        public void Draw(Rect rect)
        {
            using (WidgetState.Save())
                DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var inner = SettingsPageLayout.Body(rect);

            string total =
                _load.SizesAvailable ? Human(_entries.Sum(e => e.Bytes)) + " total."
                : _loading ? "Calculating storage sizes…"
                : "Storage sizes unavailable.";
            string caption =
                total
                + " Configured agents remain. "
                + "State from deleted or reset agents expires after 14 days. Shared caches remain.";
            float width = UiScrollBody
                .Measure(inner, 0f, UiScrollbarReservation.Always)
                .ContentWidth;
            float captionH = UiText.PlainStatusLabelHeight(caption, width);
            float rowH = StorageRowHeight(width);
            var list = inner;
            var geometry = UiScrollBody.Measure(
                list,
                captionH + UiTheme.GapS + _entries.Count * rowH + UiTheme.GapXS,
                UiScrollbarReservation.Always
            );
            if (_pendingAgent != null)
            {
                int index = _entries.FindIndex(e =>
                    e.Kind == "active" && e.Session == _pendingAgent
                );
                if (index >= 0)
                {
                    Select(_entries[index]);
                    _scroll.Reveal(captionH + UiTheme.GapS + index * rowH, rowH, list.height);
                }
                else if (!_loading)
                {
                    _error = $"private storage for '{_pendingAgent}' is unavailable";
                    _pendingAgent = null;
                }
            }
            using (_scroll.Scope(list, geometry.View))
            {
                UiText.PlainStatusLabel(
                    new Rect(0f, 0f, geometry.View.width, captionH),
                    caption,
                    UiTheme.Dim
                );
                for (int i = 0; i < _entries.Count; i++)
                    DrawRow(
                        new Rect(
                            0f,
                            captionH + UiTheme.GapS + i * rowH,
                            geometry.View.width,
                            rowH - UiTheme.GapXS
                        ),
                        _entries[i]
                    );
            }

            if (_entries.Count == 0)
            {
                UiText.PlainStatusLabel(
                    new Rect(
                        list.x,
                        list.y + captionH + UiTheme.GapS,
                        list.width,
                        Mathf.Max(0f, list.height - captionH - UiTheme.GapS)
                    ),
                    _error ?? (_loading ? "Loading storage…" : "No storage entries."),
                    _error != null ? UiTheme.Bad : UiTheme.Dim
                );
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Refresh", UiTheme.Btn.Ghost, !_loading))
                Load();
            if (foot.Left("Empty trash", UiTheme.Btn.Danger, !_loading && _hasTrash))
                ConfirmEmptyTrash();
            if (_error != null && _entries.Count > 0)
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

        static bool StackActions(float width) =>
            width < UiLayout.BtnW("Explore", 78f) * 3f + UiTheme.GapXS * 2f + 180f;

        static float StorageRowHeight(float width) =>
            UiListRow.TwoLineH + (StackActions(width) ? UiTheme.RowBtnH + UiTheme.GapS : 0f);

        void DrawRow(Rect r, Entry e)
        {
            bool over = RowChrome.Hover(
                r,
                e.Kind == _selectedKind && e.Key == _selectedKey,
                true,
                RowHoverPolicy.OverlayAware
            );
            if (over)
                TooltipHandler.TipRegion(r, e.Path);

            bool stacked = StackActions(r.width);
            int actionCount =
                e.Cache ? 1
                : e.Kind == "trash" && e.Session != null ? 3
                : 2;
            float actionW = Mathf.Min(
                UiLayout.BtnW("Explore", 78f),
                Mathf.Max(
                    0f,
                    (r.width - UiTheme.GapS * 2f - UiTheme.GapXS * (actionCount - 1)) / actionCount
                )
            );
            float right = UiListRow.Right(r);
            float labelW = Mathf.Max(
                0f,
                stacked
                    ? r.width - UiTheme.GapS * 2f
                    : r.width
                        - actionW * actionCount
                        - UiTheme.GapXS * (actionCount - 1)
                        - UiTheme.GapM
            );
            float actionY = stacked
                ? r.yMax - UiTheme.RowBtnH
                : r.y + (r.height - UiTheme.RowBtnH) / 2f;
            var textRect = new Rect(
                r.x,
                r.y,
                r.width,
                stacked ? UiListRow.TwoLineH - UiTheme.GapXS : r.height
            );

            // Leave the action buttons out of the selection hit target. The whole label side
            // is one row, so an entry does not require a tiny click on its name.
            if (UiButtons.RowButton(new Rect(r.x, r.y, labelW, textRect.height)))
                Select(e);

            float line1 = UiListRow.LineY(textRect, 0);
            float line2 = UiListRow.LineY(r, 1);
            GUI.color = UiTheme.Lead;
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, line1, labelW, UiTheme.LineH),
                e.Project ?? e.Session ?? e.Key
            );
            GUI.color = UiTheme.Dim;
            string note =
                e.Kind == "cache-managed" ? "managed shared cache"
                : e.Kind == "cache-external" ? "external shared cache"
                : e.Kind == "active" ? "configured agent"
                : e.Kind == "orphan" ? "unclaimed orphan state"
                : e.Session != null ? $"trash for {e.Session}"
                : "trash (agent removed)";
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, line2, labelW, UiTheme.LineH),
                $"{note}  -  {Size(e)}  -  {When(e.Modified)}"
            );
            GUI.color = Color.white;

            if (
                UiButtons.Button(
                    new Rect(
                        right - actionW * actionCount - UiTheme.GapXS * (actionCount - 1),
                        actionY,
                        actionW,
                        UiTheme.RowBtnH
                    ),
                    "Explore"
                )
            )
                Focus(e);

            if (e.Cache)
                return;

            if (e.Kind == "active")
            {
                if (
                    UiButtons.Button(
                        new Rect(right - actionW, actionY, actionW, UiTheme.RowBtnH),
                        "Reset",
                        UiTheme.Btn.Danger
                    )
                )
                    ConfirmReset(e);
                return;
            }

            if (e.Kind == "trash" && e.Session != null)
            {
                if (
                    UiButtons.Button(
                        new Rect(
                            right - actionW * 2f - UiTheme.GapXS,
                            actionY,
                            actionW,
                            UiTheme.RowBtnH
                        ),
                        "Restore"
                    )
                )
                    Restore(e);
            }
            if (
                UiButtons.Button(
                    new Rect(right - actionW, actionY, actionW, UiTheme.RowBtnH),
                    "Delete",
                    UiTheme.Btn.Danger
                )
            )
                ConfirmDelete(e);
        }

        static void Focus(Entry e)
        {
            if (string.IsNullOrEmpty(e.Path))
            {
                UiLayout.Fail("storage path is unavailable");
                return;
            }
            FilesView.FocusDirectory(e.Path, e.Project ?? e.Session ?? e.Key);
        }

        void Select(Entry entry)
        {
            _selectedKind = entry.Kind;
            _selectedKey = entry.Key;
            _pendingAgent = null;
        }

        public void SelectAgent(string name)
        {
            _selectedKind = null;
            _selectedKey = null;
            _pendingAgent = name;
            Load();
        }

        void ConfirmReset(Entry e)
        {
            Find.WindowStack.Add(
                ConfirmDialog.Create(
                    $"Reset private state for '{e.Session}'? The daemon stops the agent and moves "
                        + $"{SizeForConfirmation(e)} of its state to recoverable trash for 14 days.",
                    () =>
                        DaemonClient.Post(
                            $"{WireProtocol.Routes.Sessions}/{Uri.EscapeDataString(e.Session)}/state/reset",
                            null,
                            _ =>
                            {
                                SessionHub.Instance.SessionStore.Refresh();
                                Load();
                            },
                            msg => _error = msg
                        ),
                    destructive: true
                )
            );
        }

        void ConfirmDelete(Entry e)
        {
            Find.WindowStack.Add(
                ConfirmDialog.Create(
                    $"Permanently delete {SizeForConfirmation(e)} of {e.Kind} private state? This cannot be undone.",
                    () =>
                        DaemonClient.Delete(
                            $"{WireProtocol.Routes.State}/{Uri.EscapeDataString(e.Kind)}/"
                                + Uri.EscapeDataString(e.Key),
                            _ => Load(),
                            msg => _error = msg
                        ),
                    destructive: true
                )
            );
        }

        void ConfirmEmptyTrash()
        {
            Find.WindowStack.Add(
                ConfirmDialog.Create(
                    "Permanently delete all private state in trash? This cannot be undone.",
                    () =>
                        DaemonClient.Delete(
                            WireProtocol.Routes.StateTrash,
                            _ => Load(),
                            msg => _error = msg
                        ),
                    destructive: true
                )
            );
        }

        void Restore(Entry e)
        {
            DaemonClient.Post(
                $"{WireProtocol.Routes.StateTrash}/{Uri.EscapeDataString(e.Key)}/restore",
                null,
                _ =>
                {
                    SessionHub.Instance.SessionStore.Refresh();
                    Load();
                },
                msg => _error = msg
            );
        }

        string Size(Entry entry) =>
            _load.SizesAvailable ? Human(entry.Bytes)
            : _loading ? "Calculating…"
            : "Size unavailable";

        string SizeForConfirmation(Entry entry) =>
            _load.SizesAvailable ? Human(entry.Bytes) : "an unknown amount";

        public void Dispose()
        {
            _disposed = true;
            _load.Invalidate();
            GC.SuppressFinalize(this);
        }

        static string Human(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024d && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }
            return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
        }

        static int KindRank(string kind) =>
            kind == "active" ? 0
            : kind.StartsWith("cache-", StringComparison.Ordinal) ? 1
            : kind == "orphan" ? 2
            : 3;

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static string When(long unixSeconds)
        {
            if (unixSeconds <= 0)
                return "unknown age";
            long now = (long)(DateTime.UtcNow - Epoch).TotalSeconds;
            long elapsed = Math.Max(0L, now - unixSeconds);
            long days = elapsed / 86400L;
            long hours = (elapsed % 86400L) / 3600L;
            long minutes = (elapsed % 3600L) / 60L;
            if (days > 0)
                return $"{days}d {hours}h {minutes}m ago";
            if (hours > 0)
                return $"{hours}h {minutes}m ago";
            return $"{minutes}m ago";
        }
    }
}
