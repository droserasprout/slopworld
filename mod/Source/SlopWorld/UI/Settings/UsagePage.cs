using System.Collections.Generic;
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

        // A free-text mirror, so a half-typed number is not clamped out from under the
        // player mid-keystroke.
        string _pollSecs;
        readonly Dictionary<string, string> _itemIntervals =
            new Dictionary<string, string>();

        // Which key's icon picker is open, or null.
        string _pickingKey;
        readonly SmoothScroll _pickScroll = new SmoothScroll();

        protected override bool DrawFieldsWhenOffline => true;
        protected override string SavedMessage => "usage settings saved.";

        protected override void AfterLoad()
        {
            _pollSecs = _cfg.UsagePollSecs.ToString();
            _itemIntervals.Clear();
            EnsureBaseItems();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            UiWidgets.SectionHeading(l, "Usage");
            l.Label("Global poll interval (s)");
            _pollSecs = UiWidgets.Field(l, "usage.poll", _pollSecs);
            UiWidgets.Note(l, "Every row uses this interval unless its interval is set below. " +
                "A failed poll backs off on its own, doubling to half an hour.");
        }

        protected override float DrawTrailingFields(Rect rect, float y)
        {
            return y + DrawTable(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
        }

        protected override void DrawOverlay(Rect rect)
        {
            // Picker overlay, drawn last so it sits on top of everything else.
            if (_pickingKey != null) DrawPicker(rect, _pickingKey);
        }

        static readonly string[] BaseKeys =
        {
            "claude_session", "claude_week", "claude_spend",
            "openrouter_balance", "openai_session", "openai_week",
        };

        void EnsureBaseItems()
        {
            foreach (string key in BaseKeys)
                EnsureItem(key);
        }

        DaemonConfig.UsageItemConfig EnsureItem(string key)
        {
            if (!_cfg.UsageItems.TryGetValue(key, out var item) || item == null)
            {
                item = new DaemonConfig.UsageItemConfig { Poll = DefaultPoll(key) };
                _cfg.UsageItems[key] = item;
            }

            if (!_itemIntervals.ContainsKey(key))
                _itemIntervals[key] = item.IntervalSecs > 0
                    ? item.IntervalSecs.ToString()
                    : "";
            return item;
        }

        bool DefaultPoll(string key)
        {
            if (key.StartsWith("claude_")) return true;
            if (key.StartsWith("openrouter_")) return false;
            if (key.StartsWith("openai_")) return true;
            return false;
        }

        List<string> TableKeys()
        {
            var keys = new List<string>();
            foreach (string key in BaseKeys) keys.Add(key);
            foreach (var pair in _cfg.UsageItems)
                if (!keys.Contains(pair.Key)) keys.Add(pair.Key);
            foreach (var w in SessionHub.Instance.Usage.Windows)
                if (!keys.Contains(w.Key)) keys.Add(w.Key);

            keys.Sort((a, b) => UsageRank(a).CompareTo(UsageRank(b)));
            return keys;
        }

        static int UsageRank(string key)
        {
            if (key == "claude_session") return 0;
            if (key == "claude_week") return 1;
            if (key.StartsWith("claude_week_")) return 2;
            if (key == "claude_spend") return 3;
            if (key == "openrouter_balance") return 4;
            if (key == "openai_session") return 5;
            if (key == "openai_week") return 6;
            return 7;
        }

        float DrawTable(Rect rect)
        {
            var keys = TableKeys();
            float rowH = UiWidgets.RowH;
            float headerH = rowH;
            float intervalW = Mathf.Min(120f, Mathf.Max(92f, rect.width * .16f));
            float pollW = 64f;
            float iconW = 54f;
            float fixedW = intervalW + pollW + iconW;
            float availableW = Mathf.Max(0f, rect.width);
            if (fixedW > availableW && fixedW > 0f)
            {
                // The settings column can be narrower than the desktop layout. Shrink the
                // fixed columns together before assigning the remainder to Name, so every
                // cell still ends at the table's right edge instead of spilling out of it.
                float scale = availableW / fixedW;
                intervalW *= scale;
                pollW *= scale;
                iconW *= scale;
                fixedW = availableW;
            }
            float nameW = Mathf.Max(0f, availableW - fixedW);
            float iconX = rect.x + nameW;
            float pollX = iconX + iconW;
            float intervalX = pollX + pollW;
            float namePad = Mathf.Min(UiWidgets.GapS, nameW);

            var header = new Rect(rect.x, rect.y, rect.width, headerH);
            Slab.Fill(header, UiWidgets.RowBg);
            UiWidgets.RowLabel(new Rect(rect.x + namePad, rect.y,
                Mathf.Max(0f, nameW - namePad), headerH), "Name");
            UiWidgets.RowLabel(new Rect(iconX, rect.y, iconW, headerH), "Icon",
                TextAnchor.MiddleCenter);
            UiWidgets.RowLabel(new Rect(pollX, rect.y, pollW, headerH), "Poll",
                TextAnchor.MiddleCenter);
            UiWidgets.RowLabel(new Rect(intervalX, rect.y, intervalW, headerH), "Interval (s)",
                TextAnchor.MiddleCenter);
            Slab.Hairline(new Rect(rect.x, header.yMax - 1f, rect.width, 1f),
                UiWidgets.Edge);

            float y = header.yMax;
            foreach (string key in keys)
            {
                var item = EnsureItem(key);
                var row = new Rect(rect.x, y, rect.width, rowH);
                RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);

                UiWidgets.RowLabel(new Rect(row.x + namePad, row.y,
                    Mathf.Max(0f, nameW - namePad), row.height), UsageReadout.Long(key));
                DrawIconButton(new Rect(iconX, row.y, iconW, row.height), key);

                var poll = new Rect(pollX, row.y, pollW, row.height);
                item.Poll = ToggleCell.DrawCheck(poll, item.Poll,
                    item.Poll ? "Stop polling this usage window." : "Poll this usage window.",
                    false, RowHoverPolicy.OverlayAware);

                float fieldPad = Mathf.Min(UiWidgets.GapXS, intervalW / 2f);
                var field = new Rect(intervalX + fieldPad,
                    row.y + (row.height - UiWidgets.FieldH) / 2f,
                    Mathf.Max(0f, intervalW - fieldPad * 2f), UiWidgets.FieldH);
                _itemIntervals[key] = UiWidgets.Field(field, "usage.item." + key,
                    _itemIntervals[key], true);

                Slab.Hairline(new Rect(row.x, row.yMax - 1f, row.width, 1f),
                    UiWidgets.Edge);
                y = row.yMax;
            }
            return y - rect.y + UiWidgets.GapS;
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
                else Slab.Fill(r, UiWidgets.Off);
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
                UsageReadout.Long(key), () => _pickingKey = null, Choices.Count + 1,
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

                Slab.Box(cell, UiWidgets.Well, UiWidgets.Edge);
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
                    // Grey, and drawn a little smaller than a thing: it is the one cell
                    // here that is not an item, and it should not read as the loudest.
                    GUI.color = UiWidgets.Dim;
                    GUI.DrawTexture(box.ContractedBy(UiWidgets.IconInset + 1f), Icons.Cross);
                }
                GUI.color = Color.white;

                TooltipHandler.TipRegion(cell, new TipSignal(
                    def != null
                        ? def.LabelCap.ToString()
                        : "Automatic - whichever icon this mod would have picked.",
                    def != null
                        ? 0x51_0F_0002 ^ (key.GetHashCode() * 31 + def.shortHash)
                        : 0x51_0F_0004 ^ key.GetHashCode()));

                if (Widgets.ButtonInvisible(cell))
                {
                    // Null on the automatic cell, which is exactly what clears the line.
                    UsageReadout.Choose(key, def);
                    _pickingKey = null;
                }
            }
        }


        // ------------------------------------------------------------------ palette

        // The picker uses a hand-picked, one-screen subset; resolve names against defs and drop
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
            // Every drug in the game, the two serums included - vanilla files those under
            // Drugs as well, and they are the two best-looking vials on this list.
            "Ambrosia", "Beer", "Flake", "GoJuice", "Luciferium", "Penoxycyline",
            "PsychiteTea", "SmokeleafJoint", "WakeUp", "Yayo",
            "MechSerumHealer", "MechSerumResurrector",
            // Textiles and leather. One wool and three of the twenty leathers - they are all
            // one texture and differ only in color, so these are the three that read apart
            // at this size: brown, elephant grey, thrumbo white.
            "Cloth", "Synthread", "Hyperweave", "DevilstrandCloth", "WoolMegasloth",
            "Leather_Plain", "Leather_Elephant", "Leather_Thrumbo",
            // Food.
            "Pemmican", "Chocolate", "MealSurvivalPack", "MealFine", "MealLavish",
            "RawBerries", "Hay", "InsectJelly", "Milk", "Dye",
            // And the odd ones, which is where anything with a silhouette worth having ends
            // up: a skull is a fine thing for a quota to run out of.
            "WoodLog", "ElephantTusk", "ThrumboHorn", "Skull", "PsychicAmplifier",
            "PsychicSoothePulser", "Shell_HighExplosive", "Shell_AntigrainWarhead",
        };

        static List<ThingDef> _palette;

        // Resolved once and on first use rather than in a field initialiser: the database is
        // filled during startup, and a static touched too early caches a row of nulls. A def
        // this build has not got is one cell fewer, not a hole.
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


        protected override void BeforeSave()
        {
            // Floored at the same 10s the poller enforces, so what the GUI shows after a save
            // is what the daemon is actually doing.
            if (int.TryParse(_pollSecs, out int s))
                _cfg.UsagePollSecs = Mathf.Clamp(s, 10, 3600);

            foreach (var pair in _itemIntervals)
            {
                var item = EnsureItem(pair.Key);
                string text = (pair.Value ?? "").Trim();
                if (text.Length == 0)
                    item.IntervalSecs = 0;
                else if (int.TryParse(text, out int seconds))
                    item.IntervalSecs = Mathf.Clamp(seconds, 10, 3600);
            }
        }

    }
}
