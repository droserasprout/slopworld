using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public class UsagePage : IOptionPage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        // A free-text mirror, so a half-typed number is not clamped out from under the
        // player mid-keystroke.
        string _pollSecs;
        readonly Dictionary<string, string> _itemIntervals =
            new Dictionary<string, string>();

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _fieldsH;

        // Which key's icon picker is open, or null.
        string _pickingKey;
        readonly SmoothScroll _pickScroll = new SmoothScroll();

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    SessionHub.Instance.Config = _cfg;
                    _pollSecs = _cfg.UsagePollSecs.ToString();
                    _itemIntervals.Clear();
                    EnsureBaseItems();
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            var body = SlopWidgets.PageBody(rect);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // The icons are not the daemon's, so the fields are drawn whether or not it
            // answered: a socket that is down is exactly when somebody is in here reading
            // rather than configuring.
            DoFields(inner);

            DoFooter(SlopWidgets.FooterBar(rect));

            // Picker overlay, drawn last so it sits on top of everything else.
            if (_pickingKey != null)
                DrawPicker(rect, _pickingKey);
        }

        float DoFields(Rect r)
        {
            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(r, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
                return 0f;
            }

            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, r.height));
            _scroll.Begin(r, view);

            _fieldsH = DrawUsageFields(view);

            _scroll.End();
            return _fieldsH;
        }

        float DrawUsageFields(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(rect.x, rect.y, rect.width, 4000f));
            SlopWidgets.SectionHeading(l, "Usage");
            l.Label("Global poll interval (s)");
            _pollSecs = SlopWidgets.Field(l, "usage.poll", _pollSecs);
            SlopWidgets.Note(l, "Every row uses this interval unless its interval is set below. " +
                "A failed poll backs off on its own, doubling to half an hour.");
            float y = rect.y + l.CurHeight + SlopWidgets.GapM;
            l.End();

            y += DrawTable(new Rect(rect.x, y, rect.width, 4000f));
            return y - rect.y + SlopWidgets.GapS;
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

        SlopConfig.UsageItemConfig EnsureItem(string key)
        {
            if (!_cfg.UsageItems.TryGetValue(key, out var item) || item == null)
            {
                item = new SlopConfig.UsageItemConfig { Poll = DefaultPoll(key) };
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
            if (key.StartsWith("claude_")) return _cfg.Usage;
            if (key.StartsWith("openrouter_")) return _cfg.Openrouter;
            if (key.StartsWith("openai_")) return _cfg.Openai;
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
            float rowH = SlopWidgets.RowH;
            float headerH = rowH;
            float intervalW = Mathf.Min(120f, Mathf.Max(92f, rect.width * .16f));
            float pollW = 64f;
            float iconW = 54f;
            float nameW = Mathf.Max(120f, rect.width - intervalW - pollW - iconW);
            float iconX = rect.x + nameW;
            float pollX = iconX + iconW;
            float intervalX = pollX + pollW;

            var header = new Rect(rect.x, rect.y, rect.width, headerH);
            Slab.Fill(header, SlopWidgets.RowBg);
            SlopWidgets.RowLabel(new Rect(rect.x + SlopWidgets.GapS, rect.y,
                nameW - SlopWidgets.GapS, headerH), "Name");
            SlopWidgets.RowLabel(new Rect(iconX, rect.y, iconW, headerH), "Icon",
                TextAnchor.MiddleCenter);
            SlopWidgets.RowLabel(new Rect(pollX, rect.y, pollW, headerH), "Poll",
                TextAnchor.MiddleCenter);
            SlopWidgets.RowLabel(new Rect(intervalX, rect.y, intervalW, headerH), "Interval (s)",
                TextAnchor.MiddleCenter);
            Slab.Hairline(new Rect(rect.x, header.yMax - 1f, rect.width, 1f),
                SlopWidgets.Edge);

            float y = header.yMax;
            foreach (string key in keys)
            {
                var item = EnsureItem(key);
                var row = new Rect(rect.x, y, rect.width, rowH);
                if (Mouse.IsOver(row)) Slab.Fill(row, SlopWidgets.Hover);

                SlopWidgets.RowLabel(new Rect(row.x + SlopWidgets.GapS, row.y,
                    nameW - SlopWidgets.GapS, row.height), UsageReadout.Long(key));
                DrawIconButton(new Rect(iconX, row.y, iconW, row.height), key);

                var poll = new Rect(pollX, row.y, pollW, row.height);
                if (Mouse.IsOver(poll)) Slab.Fill(poll, SlopWidgets.Hover);
                SlopWidgets.TickBox(
                    new Rect(poll.center.x - SlopWidgets.TickW / 2f, poll.y,
                        SlopWidgets.TickW, poll.height), item.Poll);
                TooltipHandler.TipRegion(poll, new TipSignal(
                    item.Poll ? "Stop polling this usage window." : "Poll this usage window.",
                    0x51_0F_0010 ^ key.GetHashCode()));
                if (Widgets.ButtonInvisible(poll))
                {
                    item.Poll = !item.Poll;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                var field = new Rect(intervalX + SlopWidgets.GapXS,
                    row.y + (row.height - SlopWidgets.FieldH) / 2f,
                    intervalW - SlopWidgets.GapXS * 2f, SlopWidgets.FieldH);
                _itemIntervals[key] = SlopWidgets.Field(field, "usage.item." + key,
                    _itemIntervals[key], true);

                Slab.Hairline(new Rect(row.x, row.yMax - 1f, row.width, 1f),
                    SlopWidgets.Edge);
                y = row.yMax;
            }
            return y - rect.y + SlopWidgets.GapS;
        }

        void DrawIconButton(Rect area, string key)
        {
            float boxW = Mathf.Min(area.height - 2f, area.width - SlopWidgets.GapS);
            var box = new Rect(area.center.x - boxW / 2f,
                area.y + (area.height - boxW) / 2f, boxW, boxW);
            Slab.Box(box, SlopWidgets.Well, SlopWidgets.Edge);

            var icon = UsageReadout.IconFor(key);
            if (icon != null)
            {
                Widgets.ThingIcon(box.ContractedBy(2f), icon);
                GUI.color = Color.white;
            }
            else
                Slab.Fill(box.ContractedBy(SlopWidgets.GapS - 1f), SlopWidgets.Off);

            if (Mouse.IsOver(box)) Slab.Fill(box, SlopWidgets.Hover);
            TooltipHandler.TipRegion(box, new TipSignal(
                UsageReadout.Chosen(key) != null
                    ? "This row's icon, chosen. Click to change it."
                    : "This row's icon, picked automatically. Click to choose one.",
                0x51_0F_0003 ^ key.GetHashCode()));
            if (Widgets.ButtonInvisible(box)) _pickingKey = key;
        }


        // ------------------------------------------------------------------ picker

        // A floating grid of icons, centred in the page. Opened by an icon row and closed by
        // selecting one or clicking the X. The first cell is the automatic pick, so the two
        // answers - this one, or whichever you would have picked - are the same gesture.
        void DrawPicker(Rect pageRect, string key)
        {
            const float pickW = 380f;
            const float pickH = 360f;

            var pickRect = new Rect(
                pageRect.x + (pageRect.width - pickW) / 2f,
                pageRect.y + 50f,
                pickW, pickH);

            // Clamp to the page so it never spills off the bottom.
            if (pickRect.yMax > pageRect.yMax - 8f)
                pickRect.y = pageRect.yMax - 8f - pickH;
            if (pickRect.y < pageRect.y + 8f)
                pickRect.y = pageRect.y + 8f;

            Find.WindowStack.ImmediateWindow(0x51_0F_1000 ^ key.GetHashCode(),
                pickRect, WindowLayer.Super, () => DrawPickerContents(
                    new Rect(0f, 0f, pickW, pickH), key), true, false, 1f);
        }

        void DrawPickerContents(Rect r, string key)
        {
            // Title bar: the key name and an X button.
            Text.Font = GameFont.Small;
            SlopWidgets.RowLabel(
                new Rect(r.x + 8f, r.y + 4f, r.width - 60f, SlopWidgets.LineH),
                UsageReadout.Long(key));

            if (SlopWidgets.Button(
                    new Rect(r.width - 48f, r.y + 2f, 44f, SlopWidgets.RowBtnH), "X",
                    SlopWidgets.Btn.Ghost))
                _pickingKey = null;

            DrawPickerGrid(r, key);
        }

        float DrawPickerGrid(Rect r, string key)
        {
            // Grid of icons.
            const float Cell = 38f;
            const float IconSize = 30f;
            float gridTop = r.y + 4f + SlopWidgets.LineH + SlopWidgets.GapXS;
            float gridH = r.height - gridTop - 8f;
            int perLine = Mathf.Max(1, Mathf.FloorToInt(
                (r.width - SlopWidgets.GapM) / Cell));
            float gridW = perLine * Cell;

            // The automatic cell, then the palette.
            int count = Choices.Count + 1;
            int rows = Mathf.CeilToInt(count / (float)perLine);
            float totalH = rows * Cell;
            bool scroll = totalH > gridH;
            // If scrolling, shrink the grid by the scrollbar width.
            float gridW2 = scroll ? gridW - SlopWidgets.ScrollbarW : gridW;
            perLine = Mathf.Max(1, Mathf.FloorToInt(gridW2 / Cell));
            gridW2 = perLine * Cell;

            var view = new Rect(0f, 0f, gridW2, Mathf.Max(totalH, gridH));
            _pickScroll.Begin(new Rect(r.x + (r.width - gridW2) / 2f, gridTop, gridW2, gridH), view);

            // Read once for the whole grid rather than per cell: it parses the settings
            // string, and every cell asks the same question of it.
            var chosen = UsageReadout.Chosen(key);

            for (int i = 0; i < count; i++)
            {
                // Null is the automatic cell, and is null all the way through - what it
                // draws, what its tooltip says, and what the click writes.
                var def = i == 0 ? null : Choices[i - 1];
                int col = i % perLine;
                int row = i / perLine;
                var cell = new Rect(view.x + col * Cell, view.y + row * Cell, Cell, Cell);

                if (def == chosen)
                    Slab.Fill(cell, SlopWidgets.RowOn);
                if (Mouse.IsOver(cell))
                    Slab.Fill(cell, SlopWidgets.Hover);

                var box = new Rect(cell.x + (Cell - IconSize) / 2f,
                    cell.y + (Cell - IconSize) / 2f, IconSize, IconSize);

                if (def != null)
                {
                    Widgets.ThingIcon(box, def);
                }
                else
                {
                    // Grey, and drawn a little smaller than a thing: it is the one cell
                    // here that is not an item, and it should not read as the loudest.
                    GUI.color = SlopWidgets.Dim;
                    GUI.DrawTexture(box.ContractedBy(3f), Icons.Cross);
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

            _pickScroll.End();
            return gridH;
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


        // ------------------------------------------------------------------ footer

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);

            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();

            // Greyed and shown rather than hidden: with no config loaded there is nothing to
            // write back, and a button that vanished would read as a page with no save.
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

            if (_error != null && _loaded)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

        void Save()
        {
            if (!_loaded) return;

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

            // Keep the legacy provider switches meaningful for raw-config readers and older
            // daemon versions: they are the aggregate of the row toggles in this table.
            _cfg.Usage = AnyItem("claude_");
            _cfg.Openrouter = AnyItem("openrouter_");
            _cfg.Openai = AnyItem("openai_");

            SlopClient.Put("/api/config/patch", _cfg.ToPatchJson(),
                _ =>
                {
                    _error = null;
                    SlopOptions.Reread();
                    Messages.Message("SlopWorld: usage settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }

        bool AnyItem(string prefix)
        {
            foreach (var pair in _cfg.UsageItems)
                if (pair.Key.StartsWith(prefix) && pair.Value != null && pair.Value.Poll)
                    return true;
            return false;
        }
    }
}
