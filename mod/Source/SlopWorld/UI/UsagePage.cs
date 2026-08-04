using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What this machine is allowed to ask about, and what the answers look like along the
    // top of the screen. A page of its own rather than a paragraph on ConfigPage: there are
    // two sellers now, each with a switch and a key, and the row they draw is the only thing
    // in this mod the player is invited to choose the *look* of.
    //
    // The two halves are saved by different roads and that is the point of the split. The
    // switches and paths are `config.toml`, so they go over HTTP and need a daemon; the
    // icons are mod settings, written on the click, so they can be set with the socket down
    // and are about this install rather than this machine. See SlopSettings.
    //
    // A page rather than a Window, hung off an OptionCategoryDef by SlopOptions.
    // Icon rows are grouped by provider in the fields list, and picking one opens a
    // floating grid rather than drawing the whole palette inline.
    public class UsagePage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        // A free-text mirror, so a half-typed number is not clamped out from under the
        // player mid-keystroke.
        string _pollSecs;

        Vector2 _scroll;
        float _fieldsH;

        // Which key's icon picker is open, or null.
        string _pickingKey;
        Vector2 _pickScroll;

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    _pollSecs = _cfg.UsagePollSecs.ToString();
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                "What the daemon asks about, and what it looks like up there.");
            GUI.color = Color.white;

            var body = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f - 40f);
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(12f);

            // The icons are not the daemon's, so the fields are drawn whether or not it
            // answered: a socket that is down is exactly when somebody is in here reading
            // rather than configuring.
            DoFields(inner);

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));

            // Picker overlay, drawn last so it sits on top of everything else.
            if (_pickingKey != null)
                DrawPicker(rect, _pickingKey);
        }

        void DoFields(Rect r)
        {
            if (!_loaded)
            {
                GUI.color = _error != null ? new Color(0.95f, 0.45f, 0.45f) : Color.gray;
                Widgets.Label(r, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
                return;
            }

            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(_fieldsH, r.height));
            Widgets.BeginScrollView(r, ref _scroll, view);

            // Begun far taller than it is, so a control that would cross the bottom does not
            // start a second column and drop the rest of the form on top of itself.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            // ---------------------------------------------------------------- Anthropic
            Heading(l, "Anthropic");
            l.CheckboxLabeled("Poll for what is left of the subscription", ref _cfg.Usage,
                "The daemon reads the OAuth token Claude Code keeps on this machine and " +
                "asks Anthropic. Off means it never touches that file.");

            if (_cfg.Usage)
            {
                l.Gap(2f);
                l.Label("Credentials file");
                _cfg.ClaudeCredentials = l.TextEntry(_cfg.ClaudeCredentials);

                l.Gap(4f);
                // Icon rows for the Anthropic windows. The extra-usage ("spend") row is
                // here rather than under OpenRouter because it is the one Claude Code's
                // own /usage answers with.
                IconRow(l, "session", "5-hour window");
                IconRow(l, "week", "Weekly limit");
                // Any per-model weekly limits the daemon reports.
                foreach (var key in LiveKeys())
                    if (key.StartsWith("week_"))
                        IconRow(l, key, null);
                IconRow(l, "spend", "Extra usage");
            }

            // ---------------------------------------------------------------- OpenRouter
            l.Gap(14f);
            Heading(l, "OpenRouter");
            l.CheckboxLabeled("Poll for the credit balance", ref _cfg.Openrouter,
                "Credits bought less credits spent, which is what the pi agent draws down. " +
                "Off means the daemon never reads the key and never calls OpenRouter.");

            if (_cfg.Openrouter)
            {
                l.Gap(2f);
                l.Label("Key file (blank reads $OPENROUTER_API_KEY)");
                _cfg.OpenrouterKeyFile = l.TextEntry(_cfg.OpenrouterKeyFile);
                Note(l, "Blank is the key out of the daemon's own environment - the same one " +
                        "the pi preset forwards into that agent's sandbox, so a machine that " +
                        "can run pi needs no second copy of it here.");

                l.Gap(4f);
                IconRow(l, "balance", "Credit balance");
            }

            // ---------------------------------------------------------------- Both
            l.Gap(14f);
            Heading(l, "Both");
            l.Label("Seconds between polls");
            _pollSecs = l.TextEntry(_pollSecs);
            Note(l, "A failed poll backs off on its own, doubling to half an hour, and each " +
                    "seller keeps its own place in that queue: one being down never takes " +
                    "the other's numbers off the screen.");

            _fieldsH = l.CurHeight + 8f;
            l.End();

            Widgets.EndScrollView();
        }

        static void Heading(Listing_Standard l, string text)
        {
            Text.Font = GameFont.Medium;
            l.Label(text);
            Text.Font = GameFont.Small;
            l.GapLine(2f);
        }

        static void Note(Listing_Standard l, string text)
        {
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label(text);
            GUI.color = Color.white;
        }


        // ------------------------------------------------------------------ icon row

        // One line in the fields list: the long name, the current icon, and two buttons.
        //
        //   [Long name               ] [icon] [auto] [pick...]
        //
        // "auto" clears the choice and lets UsageReadout fall back to its own pick; the
        // button is greyed when there is nothing to clear. "pick..." opens the grid.
        void IconRow(Listing_Standard l, string key, string hint)
        {
            var row = l.GetRect(28f);

            // Label, left-aligned. If hint is given, use it as a fallback short label
            // so the row works even when the daemon is not reporting this key yet.
            string label = UsageReadout.Long(key, hint ?? key);
            float labelW = Text.CalcSize(label).x + 6f;
            float maxLabel = row.width - 172f;
            if (labelW > maxLabel) labelW = maxLabel;

            Widgets.Label(new Rect(row.x, row.y, labelW, row.height), label);

            // Current icon, drawn as a small ThingIcon.
            var icon = UsageReadout.IconFor(key);
            const float iconBox = 22f;
            float iconX = row.x + labelW + 4f;
            var iconRect = new Rect(iconX, row.y + (row.height - iconBox) / 2f,
                iconBox, iconBox);

            if (icon != null)
            {
                Widgets.ThingIcon(iconRect, icon);
                GUI.color = Color.white;
            }
            else
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0.3f, 0.3f, 0.3f));
            }

            // "auto" button, greyed out when there is no custom choice to clear.
            var chosen = UsageReadout.Chosen(key);
            float autoX = iconX + iconBox + 4f;
            var autoRect = new Rect(autoX, row.y, 40f, row.height);
            GUI.color = chosen == null ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            if (Widgets.ButtonText(autoRect, "auto", true, false, chosen != null))
                UsageReadout.Choose(key, null);
            GUI.color = Color.white;

            // "pick..." button, always enabled.
            float pickX = autoX + 44f;
            var pickRect = new Rect(pickX, row.y, 52f, row.height);
            if (Widgets.ButtonText(pickRect, "pick...", true, false, true))
                _pickingKey = key;

            // Tooltip over the whole row.
            if (Mouse.IsOver(new Rect(row.x, row.y, pickX + 52f - row.x, row.height)))
            {
                string tip = "Current icon for this row. Click 'pick...' to choose one " +
                             "from the grid, or 'auto' to let the mod pick for you.";
                TooltipHandler.TipRegion(row, new TipSignal(tip, 0x51_0F_0003 ^ key.GetHashCode()));
            }
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

        // A floating grid of icons, centred in the page. Opened by "pick..." on an icon
        // row and closed by selecting one or clicking the X.
        void DrawPicker(Rect pageRect, string key)
        {
            const float pickW = 340f;
            const float pickH = 320f;

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
                pickRect, WindowLayer.Super, () =>
            {
                var r = new Rect(0f, 0f, pickW, pickH);

                // Title bar: the key name and an X button.
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 60f, 22f),
                    UsageReadout.Long(key));

                if (Widgets.ButtonText(new Rect(r.width - 48f, r.y + 2f, 44f, 22f), "X",
                        true, false, true))
                    _pickingKey = null;

                // Grid of icons.
                const float Cell = 38f;
                const float IconSize = 30f;
                float gridTop = r.y + 30f;
                float gridH = r.height - gridTop - 8f;
                int perLine = Mathf.Max(1, Mathf.FloorToInt((r.width - 16f) / Cell));
                float gridW = perLine * Cell;
                var gridRect = new Rect(r.x + (r.width - gridW) / 2f, gridTop, gridW, gridH);

                int rows = Mathf.CeilToInt(Choices.Count / (float)perLine);
                float totalH = rows * Cell;
                bool scroll = totalH > gridH;
                // If scrolling, shrink the grid by the scrollbar width.
                float gridW2 = scroll ? gridW - 18f : gridW;
                perLine = Mathf.Max(1, Mathf.FloorToInt(gridW2 / Cell));
                gridW2 = perLine * Cell;

                var view = new Rect(0f, 0f, gridW2, Mathf.Max(totalH, gridH));
                Widgets.BeginScrollView(
                    new Rect(r.x + (r.width - gridW2) / 2f, gridTop, gridW2, gridH),
                    ref _pickScroll, view);

                for (int i = 0; i < Choices.Count; i++)
                {
                    var def = Choices[i];
                    int col = i % perLine;
                    int row = i / perLine;
                    var cell = new Rect(view.x + col * Cell, view.y + row * Cell, Cell, Cell);

                    var chosen = UsageReadout.Chosen(key);
                    if (def == chosen)
                        Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.16f));
                    if (Mouse.IsOver(cell))
                        Widgets.DrawHighlight(cell);

                    var box = new Rect(cell.x + (Cell - IconSize) / 2f,
                        cell.y + (Cell - IconSize) / 2f, IconSize, IconSize);
                    Widgets.ThingIcon(box, def);
                    GUI.color = Color.white;

                    TooltipHandler.TipRegion(cell, new TipSignal(def.LabelCap,
                        0x51_0F_0002 ^ (key.GetHashCode() * 31 + def.shortHash)));

                    if (Widgets.ButtonInvisible(cell))
                    {
                        UsageReadout.Choose(key, def == chosen ? null : def);
                        _pickingKey = null;
                    }
                }

                Widgets.EndScrollView();
            }, true, false, 1f);
        }


        // ------------------------------------------------------------------ palette

        // The full set of icons the picker offers. More than the old inline palette, and
        // grouped by no category: the grid is small enough that scrolling is fast and a
        // search box is not needed. Resolved lazily and only those this build knows about
        // appear.
        static readonly string[] Palette =
        {
            "Silver", "Gold", "Steel", "Plasteel", "Uranium", "Jade",
            "ComponentIndustrial", "ComponentSpacer", "AIPersonaCore", "Chemfuel",
            "Neutroamine", "MedicineIndustrial", "MedicineUltratech", "Beer",
            "Cloth", "WoodLog", "Pemmican", "Chocolate",
            "Luciferium", "GoJuice", "WakeUp", "Yayo",
            "MechSerum", "HealerMechSerum", "ResurrectorMechSerum",
            "Synthread", "Hyperweave", "DevilstrandCloth",
            "CarpetRed", "CarpetGreen", "CarpetBlue",
            "Leather_Plain", "ElephantLeather", "Thrumbofur",
            "PackedSurvivalMeal", "FineMeal", "LavishMeal",
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
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 110f, 30f), "Reload"))
                Load();

            if (_error != null && _loaded)
            {
                GUI.color = new Color(0.95f, 0.45f, 0.45f);
                Widgets.Label(new Rect(bar.x + 118f, bar.y + 4f, bar.width - 260f, 24f), _error);
                GUI.color = Color.white;
            }

            // Greyed and shown rather than hidden: with no config loaded there is nothing to
            // write back, and a button that vanished would read as a page with no save.
            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 30f), "Save",
                    true, false, _loaded))
                Save();
        }

        void Save()
        {
            if (!_loaded) return;

            // Floored at the same 10s the poller enforces, so what the GUI shows after a save
            // is what the daemon is actually doing.
            if (int.TryParse(_pollSecs, out int s))
                _cfg.UsagePollSecs = Mathf.Clamp(s, 10, 3600);

            SlopClient.Put("/api/config/values", _cfg.ToJson(),
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