using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public class UsagePage : DaemonConfigPage
    {
        const float PickerWidth = 380f;
        const float PickerHeight = 360f;

        // Rows discovered by the live usage snapshot are display state until the player
        // changes them. Keeping them out of DaemonConfig prevents opening this page from
        // creating a new poll override.
        readonly Dictionary<string, DaemonConfig.UsageItemConfig> _implicitItems =
            new Dictionary<string, DaemonConfig.UsageItemConfig>();

        // Which key's icon picker is open, or null.
        string _pickingKey;
        readonly SmoothScroll _pickScroll = new SmoothScroll();

        protected override bool DrawFieldsWhenOffline => true;
        protected override string SavedMessage => "usage settings saved.";

        protected override void AfterLoad()
        {
            _implicitItems.Clear();
            EnsureBaseItems();
        }

        protected override void AfterDiscard()
        {
            _implicitItems.Clear();
            EnsureBaseItems();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            // A page can be reopened while its shared draft is finishing an in-flight save.
            // the old page's load callback is intentionally not replayed into this instance.
            UiLayout.SectionHeading(l, "Usage");
            l.Label("Global polling interval (seconds)");
            string pollSecs = _configState.DraftText("usage.poll", "daemon.usage_poll_secs",
                _cfg.UsagePollSecs.ToString());
            pollSecs = UiControls.Field(l, "usage.poll", pollSecs,
                defaultValue: _cfg.FactoryDefaults?.UsagePollSecs?.ToString());
            _configState.SetDraftText("usage.poll", "daemon.usage_poll_secs", pollSecs);
            UiLayout.Validation(l, PollError(pollSecs, false));
            UiLayout.Note(l, "Every row uses this interval unless you set a separate interval below. " +
                "When a poll fails, the daemon doubles the delay up to 30 minutes.");
        }

        protected override float DrawTrailingFields(Rect rect, float y)
        {
            y += UiTheme.GapS;
            return y + DrawTable(new Rect(rect.x, y, rect.width, UiLayout.ListingHeight));
        }

        protected override void DrawOverlay(Rect rect)
        {
            // Picker overlay, drawn last so it sits on top of everything else.
            if (_pickingKey != null) DrawPicker(rect, _pickingKey);
        }

        List<UsageCatalogInfo> _catalog = new List<UsageCatalogInfo>();
        Dictionary<string, UsageCatalogInfo> _catalogByKey =
            new Dictionary<string, UsageCatalogInfo>();

        public override void Draw(Rect rect)
        {
            if (_cfg != null) RefreshCatalog();
            base.Draw(rect);
        }

        void RefreshCatalog()
        {
            _catalog = DaemonConfig.MergeUsageCatalogs(_cfg.UsageCatalog,
                SessionHub.Instance.Usage.Catalog);
            _catalogByKey = _catalog.ToDictionary(entry => entry.Key);
        }

        void EnsureBaseItems()
        {
            RefreshCatalog();
            foreach (var entry in _catalog) EnsureItem(entry.Key);
        }

        UsageCatalogInfo Entry(string key) =>
            _catalogByKey.TryGetValue(key, out var entry) ? entry : null;

        string LabelFor(string key)
        {
            string label = Entry(key)?.Label;
            return string.IsNullOrWhiteSpace(label) ? UsageReadout.Long(key) : label;
        }

        DaemonConfig.UsageItemConfig EnsureItem(string key)
        {
            if (_cfg.UsageItems.TryGetValue(key, out var configured) && configured != null)
            {
                DraftInterval(key, configured.IntervalSecs);
                return configured;
            }

            if (!_implicitItems.TryGetValue(key, out var item) || item == null)
            {
                item = new DaemonConfig.UsageItemConfig { Poll = DefaultPoll(key) };
                _implicitItems[key] = item;
            }

            DraftInterval(key, item.IntervalSecs);
            return item;
        }

        string DraftInterval(string key, int interval)
        {
            return _configState.DraftText("usage.item." + key,
                "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs",
                interval > 0 ? interval.ToString() : "", zeroMeansBlank: true);
        }

        void SetDraftInterval(string key, string value)
        {
            _configState.SetDraftText("usage.item." + key,
                "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs", value);
        }

        void Promote(string key, DaemonConfig.UsageItemConfig item)
        {
            if (_cfg.UsageItems.ContainsKey(key)) return;
            _cfg.UsageItems[key] = new DaemonConfig.UsageItemConfig
            {
                Poll = item.Poll,
                IntervalSecs = item.IntervalSecs,
            };
            _implicitItems.Remove(key);
        }

        void SetPoll(string key, bool value)
        {
            var item = EnsureItem(key);
            bool changed = item.Poll != value;
            item.Poll = value;
            if (changed && _implicitItems.ContainsKey(key)) Promote(key, item);
        }

        bool DefaultPoll(string key)
        {
            return Entry(key)?.DefaultPoll ?? false;
        }

        List<string> TableKeys()
        {
            var keys = new List<string>();
            foreach (var entry in _catalog)
                if (!keys.Contains(entry.Key)) keys.Add(entry.Key);
            foreach (var pair in _cfg.UsageItems)
                if (!keys.Contains(pair.Key)) keys.Add(pair.Key);
            foreach (var w in SessionHub.Instance.Usage.Windows)
                if (!keys.Contains(w.Key)) keys.Add(w.Key);

            keys.Sort((a, b) =>
            {
                int rank = UsageRank(a).CompareTo(UsageRank(b));
                return rank != 0 ? rank : System.StringComparer.Ordinal.Compare(a, b);
            });
            return keys;
        }

        int UsageRank(string key) => Entry(key)?.Rank ?? int.MaxValue;

        static string PollTooltip(bool polling) => polling
            ? "Stop polling this usage window." : "Poll this usage window.";

        float DrawTable(Rect rect)
        {
            var keys = TableKeys();
            if (rect.width < 430f) return DrawStackedTable(rect, keys);
            // The interval editor is a full control, not a one-line label. Give the table
            // enough height for the shared vertical inset around it.
            float rowH = UiTheme.FieldH + UiTheme.GapS + UiTheme.LineH + UiTheme.GapXS;
            float intervalW = Mathf.Min(120f, Mathf.Max(92f, rect.width * .16f));
            float pollW = 64f;
            float iconW = 54f;
            var columns = new List<UiTable.Column>
            {
                new UiTable.Column("Name", 0f, true, TextAnchor.MiddleLeft, UiTheme.GapS),
                new UiTable.Column("Icon", iconW, false, TextAnchor.MiddleCenter),
                new UiTable.Column("Polling", pollW, false, TextAnchor.MiddleCenter),
                new UiTable.Column("Interval (seconds)", intervalW, false, TextAnchor.MiddleCenter),
            };
            return UiTable.Draw(rect, keys, rowH, columns, (key, row, cells) =>
            {
                var item = EnsureItem(key);
                var name = cells[0];
                float namePad = Mathf.Min(UiTheme.GapS, name.width);
                UiText.RowLabel(new Rect(name.x + namePad, name.y,
                    Mathf.Max(0f, name.width - namePad), name.height), LabelFor(key));
                DrawIconButton(cells[1], key);

                SetPoll(key, ToggleCell.DrawCheck(cells[2], item.Poll,
                    PollTooltip(item.Poll),
                    false, RowHoverPolicy.OverlayAware));

                var interval = cells[3];
                float fieldPad = Mathf.Min(UiTheme.GapXS, interval.width / 2f);
                float fieldBlockH = UiTheme.FieldH + UiTheme.GapXS + UiTheme.LineH;
                float fieldY = interval.y + Mathf.Max(0f, (interval.height - fieldBlockH) / 2f);
                var field = new Rect(interval.x + fieldPad, fieldY,
                    Mathf.Max(0f, interval.width - fieldPad * 2f), UiTheme.FieldH);
                string intervalText = DraftInterval(key, item.IntervalSecs);
                intervalText = UiText.Field(field, "usage.item." + key,
                    intervalText, defaultValue: "");
                SetDraftInterval(key, intervalText);
                UiLayout.ValidationLabel(new Rect(interval.x + fieldPad,
                    field.yMax + UiTheme.GapXS,
                    Mathf.Max(0f, interval.width - fieldPad * 2f), UiTheme.LineH),
                    PollError(intervalText, true));
            });
        }

        float DrawStackedTable(Rect rect, List<string> keys)
        {
            float y = rect.y;
            foreach (string key in keys)
            {
                var item = EnsureItem(key);
                UiText.RowLabel(new Rect(rect.x, y, rect.width, UiTheme.RowH), LabelFor(key));
                y += UiTheme.RowH;
                float iconW = Mathf.Min(54f, rect.width / 4f);
                float pollW = Mathf.Min(64f, rect.width / 4f);
                DrawIconButton(new Rect(rect.x, y, iconW, UiTheme.FieldH), key);
                SetPoll(key, ToggleCell.DrawCheck(new Rect(rect.x + iconW, y, pollW, UiTheme.FieldH),
                    item.Poll, PollTooltip(item.Poll), false, RowHoverPolicy.OverlayAware));

                var field = new Rect(rect.x + iconW + pollW, y,
                    Mathf.Max(0f, rect.width - iconW - pollW), UiTheme.FieldH);
                string intervalText = DraftInterval(key, item.IntervalSecs);
                intervalText = UiText.Field(field, "usage.item." + key,
                    intervalText, defaultValue: "");
                SetDraftInterval(key, intervalText);
                TooltipHandler.TipRegion(field, "Polling interval in seconds.");
                UiLayout.ValidationLabel(new Rect(field.x, field.yMax + UiTheme.GapXS,
                    field.width, UiTheme.LineH), PollError(intervalText, true));
                y += UiTheme.FieldH + UiTheme.GapS + UiTheme.LineH + UiTheme.GapXS;
            }
            return y - rect.y;
        }

        void DrawIconButton(Rect area, string key)
        {
            var icon = UsageReadout.IconFor(key);
            var tip = new TipSignal(UsageReadout.Chosen(key) != null
                ? "This row's icon, chosen. Click to change it."
                : "This row's icon, picked automatically. Click to choose one.",
                0x51_0F_0003 ^ key.GetHashCode());
            if (IconPickerCell.Draw(area, r =>
            {
                if (icon != null) Widgets.ThingIcon(r, icon);
                else Slab.Fill(r, UiTheme.Off);
            }, tip, RowHoverPolicy.Local))
                _pickingKey = key;
        }


        // ------------------------------------------------------------------ picker

        // A floating grid of icons, centred in the page. Opened by an icon row and closed by
        // selecting one or clicking the X. The first cell is the automatic pick, so the two
        // answers - this one, or whichever you would have picked - are the same gesture.
        void DrawPicker(Rect pageRect, string key)
        {
            UiPickerWindow.Show(0x51_0F_1000 ^ key.GetHashCode(), pageRect,
                PickerWidth, PickerHeight,
                LabelFor(key), () => _pickingKey = null, Choices.Count + 1,
                _pickScroll, grid => DrawPickerGrid(grid, key));
        }

        void DrawPickerGrid(UiPickerWindow.Grid grid, string key)
        {
            // The automatic cell, then the palette.
            int count = Choices.Count + 1;
            // Read once for the whole grid rather than per cell: it parses the settings
            // string, and every cell asks the same question of it.
            var chosen = UsageReadout.Chosen(key);
            for (int i = 0; i < count; i++)
            {
                // Null is the automatic cell, and is null all the way through - what it
                // draws, what its tooltip says, and what the click writes.
                var def = i == 0 ? null : Choices[i - 1];
                int col = i % grid.Columns;
                int row = i / grid.Columns;
                var cell = new Rect(grid.View.x + col * grid.Cell,
                    grid.View.y + row * grid.Cell, grid.Cell, grid.Cell);

                Slab.Box(cell, UiTheme.Well, UiTheme.Edge);
                RowChrome.Hover(cell, def == chosen, true, RowHoverPolicy.Local,
                    RowSelectionStyle.Palette);

                var box = new Rect(cell.x + (grid.Cell - grid.IconSize) / 2f,
                    cell.y + (grid.Cell - grid.IconSize) / 2f,
                    grid.IconSize, grid.IconSize);

                if (def != null)
                {
                    Widgets.ThingIcon(box, def);
                }
                else
                {
                    // Grey, and drawn a little smaller than a thing. It is the one cell here that
                    // is not an item, and it should not read as the loudest.
                    GUI.color = UiTheme.Dim;
                    GUI.DrawTexture(box.ContractedBy(UiTheme.IconInset + 1f), Icons.Cross);
                }
                GUI.color = Color.white;

                TooltipHandler.TipRegion(cell, new TipSignal(
                    def != null
                        ? def.LabelCap.ToString()
                        : "Automatic - whichever icon this mod would have picked.",
                    def != null
                        ? 0x51_0F_0002 ^ (key.GetHashCode() * 31 + def.shortHash)
                        : 0x51_0F_0004 ^ key.GetHashCode()));

                if (UiButtons.RowButton(cell))
                {
                    // Null on the automatic cell, which is exactly what clears the line.
                    UsageReadout.Choose(key, def);
                    _pickingKey = null;
                }
            }
        }


        // ------------------------------------------------------------------ palette

        // The picker uses a hand-picked, one-screen subset. Resolve names against defs and drop
        // missing entries. Carpet is a TerrainDef in 1.6, not a ThingDef, so it is excluded.
        static readonly string[] Palette =
        {
            // Metals and stone.
            "Silver", "Gold", "Steel", "Plasteel", "Uranium", "Jade", "BlocksGranite",
            // Manufactured.
            "ComponentIndustrial", "ComponentSpacer", "AIPersonaCore", "TechprofSubpersonaCore",
            "Chemfuel", "Neutroamine", "ReinforcedBarrel", "Wort",
            // Medicine.
            "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech",
            // Every drug in the game, the two serums included - vanilla files those under Drugs as
            // well. They are the two best-looking vials on this list.
            "Ambrosia", "Beer", "Flake", "GoJuice", "Luciferium", "Penoxycyline",
            "PsychiteTea", "SmokeleafJoint", "WakeUp", "Yayo",
            "MechSerumHealer", "MechSerumResurrector",
            // Textiles and leather. One wool and three of the twenty leathers - they are all one
            // texture and differ only in color. Therefore, these are the three that read apart at
            // this size: brown, elephant grey, thrumbo white.
            "Cloth", "Synthread", "Hyperweave", "DevilstrandCloth", "WoolMegasloth",
            "Leather_Plain", "Leather_Elephant", "Leather_Thrumbo",
            // Food.
            "Pemmican", "Chocolate", "MealSurvivalPack", "MealFine", "MealLavish",
            "RawBerries", "Hay", "InsectJelly", "Milk", "Dye",
            // And the odd ones, which is where anything with a silhouette worth having ends up. A
            // skull is a fine thing for a quota to run out of.
            "WoodLog", "ElephantTusk", "ThrumboHorn", "Skull", "PsychicAmplifier",
            "PsychicSoothePulser", "Shell_HighExplosive", "Shell_AntigrainWarhead",
        };

        static List<ThingDef> _palette;

        // Resolved once and on first use rather than in a field initialiser. The database is filled
        // during startup, and a static touched too early caches a row of nulls. A def this build
        // has not got is one cell fewer, not a hole.
        static List<ThingDef> Choices
        {
            get
            {
                if (_palette != null) return _palette;

                _palette = new List<ThingDef>();
                foreach (var name in Palette)
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (def != null) _palette.Add(def);
                }
                return _palette;
            }
        }


        protected override string ValidationError
        {
            get
            {
                if (!_loaded || _cfg == null) return null;
                string error = PollError(_configState.DraftText("usage.poll",
                    "daemon.usage_poll_secs", _cfg.UsagePollSecs.ToString()), false);
                if (!string.IsNullOrEmpty(error)) return error;
                foreach (string fieldKey in _configState.DraftFieldKeys("usage.item."))
                {
                    string key = fieldKey.Substring("usage.item.".Length);
                    error = PollError(_configState.DraftText(fieldKey,
                        "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs", "", true), true);
                    if (!string.IsNullOrEmpty(error)) return error;
                }
                return null;
            }
        }

        protected override bool PrepareSave(out string error)
        {
            _configState.ClearQueuedNormalizations();
            string pollText = _configState.DraftText("usage.poll", "daemon.usage_poll_secs",
                _cfg.UsagePollSecs.ToString());
            if (!DaemonConfigValidation.WholeSeconds(pollText, false, out int seconds,
                                                      out error)) return false;
            _cfg.UsagePollSecs = seconds;
            _configState.SetDraftText("usage.poll", "daemon.usage_poll_secs", pollText);
            _configState.QueueDraftTextNormalization("usage.poll", "daemon.usage_poll_secs",
                seconds.ToString());

            foreach (string fieldKey in _configState.DraftFieldKeys("usage.item."))
            {
                string key = fieldKey.Substring("usage.item.".Length);
                string text = (_configState.DraftText(fieldKey,
                    "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs", "", true) ?? "").Trim();
                if (text.Length == 0)
                {
                    if (_cfg.UsageItems.TryGetValue(key, out var configured) && configured != null)
                        configured.IntervalSecs = 0;
                    SetDraftInterval(key, "");
                    _configState.QueueDraftTextNormalization(fieldKey,
                        "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs", "");
                }
                else if (DaemonConfigValidation.WholeSeconds(text, true, out seconds,
                                                              out error))
                {
                    var item = EnsureItem(key);
                    if (_implicitItems.ContainsKey(key)) Promote(key, item);
                    item = EnsureItem(key);
                    item.IntervalSecs = seconds;
                    SetDraftInterval(key, text);
                    _configState.QueueDraftTextNormalization(fieldKey,
                        "daemon.usage_items." + ProtoFields.Escape(key) + ".interval_secs",
                        seconds.ToString());
                }
                else
                    return false;
            }
            error = null;
            return true;
        }

        static string PollError(string text, bool inheritance)
        {
            DaemonConfigValidation.WholeSeconds(text, inheritance, out _, out string error);
            return error;
        }

    }
}
