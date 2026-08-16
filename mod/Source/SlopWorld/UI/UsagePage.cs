using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

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
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect,
                "What the daemon asks about, and what it looks like up there.");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
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
            float y = rect.y;
            y += DrawAnthropic(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawOpenRouter(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawOpenAI(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawBoth(new Rect(rect.x, y, rect.width, 4000f));
            return y - rect.y + SlopWidgets.GapS;
        }

        float DrawAnthropic(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            SlopWidgets.SectionHeading(l, "Anthropic");
            _cfg.Usage = SlopWidgets.Checkbox(l, "Poll Claude usage",
                _cfg.Usage,
                "The daemon reads the OAuth token Claude Code keeps on this machine and " +
                "asks Anthropic. Off means it never touches that file.");
            if (_cfg.Usage)
            {
                l.Gap(SlopWidgets.GapM);
                // The extra-usage row is here rather than under OpenRouter because it is the
                // one Claude Code's own /usage answers with.
                IconRow(l, "claude_session", "5-hour window");
                IconRow(l, "claude_week", "Weekly limit");
                // Any per-model weekly limits the daemon reports.
                foreach (var key in LiveKeys())
                    if (key.StartsWith("claude_week_"))
                        IconRow(l, key, null);
                IconRow(l, "claude_spend", "Extra usage");
            }
            l.Gap(SlopWidgets.GapL);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawOpenRouter(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            SlopWidgets.SectionHeading(l, "OpenRouter");
            _cfg.Openrouter = SlopWidgets.Checkbox(l, "Poll credit balance",
                _cfg.Openrouter,
                "Credits bought less credits spent, which is what the pi agent draws down. " +
                "Off means the daemon never reads the key and never calls OpenRouter.");
            if (_cfg.Openrouter)
            {
                l.Gap(SlopWidgets.GapM);
                IconRow(l, "openrouter_balance", "Credit balance");
            }
            l.Gap(SlopWidgets.GapL);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawOpenAI(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            SlopWidgets.SectionHeading(l, "OpenAI / Codex");
            _cfg.Openai = SlopWidgets.Checkbox(l, "Poll Codex usage", _cfg.Openai,
                "The daemon reads Codex's ChatGPT login and asks for the primary and " +
                "secondary usage windows. Off means it never touches that file.");
            if (_cfg.Openai)
            {
                l.Gap(SlopWidgets.GapM);
                IconRow(l, "openai_session", "Primary window");
                IconRow(l, "openai_week", "Secondary window");
            }
            l.Gap(SlopWidgets.GapL);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawBoth(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            SlopWidgets.SectionHeading(l, "All providers");
            l.Label("Poll interval (s)");
            _pollSecs = SlopWidgets.Field(l, "usage.poll", _pollSecs);
            SlopWidgets.Note(l, "A failed poll backs off on its own, doubling to half an hour, and each " +
                    "seller keeps its own place in that queue: one being down never takes " +
                    "the other's numbers off the screen.");

            float used = l.CurHeight;
            l.End();
            return used;
        }


        // ------------------------------------------------------------------ icon row

        // Each provider row uses the icon itself as the button; the picker starts with Auto.
        void IconRow(Listing_Standard l, string key, string hint)
        {
            var row = l.GetRect(SlopWidgets.RowH);

            float boxW = SlopWidgets.RowH - 2f;
            // A column rather than hard against the label, so the buttons line up down the
            // page the way the sidebar's times do. Clamped, a narrow page being one where the
            // label gives way rather than the thing that is clicked.
            float col = Mathf.Min(230f, row.width - boxW - SlopWidgets.GapXS);

            // If hint is given it is a fallback short label, so the row works even when the
            // daemon is not reporting this key yet.
            SlopWidgets.RowLabel(new Rect(row.x, row.y, col - SlopWidgets.GapXS, row.height),
                UsageReadout.Long(key, hint ?? key));

            var box = new Rect(row.x + col, row.y + (row.height - boxW) / 2f, boxW, boxW);

            // A plate under it, or an icon on the page's own background is a picture rather
            // than something to press. The same well every other box here sits in.
            Slab.Box(box, SlopWidgets.Well, SlopWidgets.Edge);

            var icon = UsageReadout.IconFor(key);
            if (icon != null)
            {
                Widgets.ThingIcon(box.ContractedBy(2f), icon);
                GUI.color = Color.white;
            }
            else
            {
                // No icon at all: a key past the end of the pool, which draws its number and
                // nothing else up there. Said as an empty plate rather than as the cross,
                // which in the grid below means "let the mod choose" and not "nothing".
                Slab.Fill(box.ContractedBy(SlopWidgets.GapS - 1f), SlopWidgets.Off);
            }

            if (Mouse.IsOver(box)) Slab.Fill(box, SlopWidgets.Hover);

            // Whose pick this is, is the one thing the row no longer says by itself.
            TooltipHandler.TipRegion(box, new TipSignal(
                UsageReadout.Chosen(key) != null
                    ? "This row's icon, chosen. Click to change it."
                    : "This row's icon, picked automatically. Click to choose one.",
                0x51_0F_0003 ^ key.GetHashCode()));

            if (Widgets.ButtonInvisible(box))
                _pickingKey = key;
        }

        // The keys the daemon is currently reporting, so we can show icon rows for any
        // per-model windows that appear.
        static List<string> LiveKeys()
        {
            var keys = new List<string>();
            foreach (var w in SessionHub.Instance.Usage.Windows)
                if (!keys.Contains(w.Key)) keys.Add(w.Key);
            return keys;
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
    }
}
