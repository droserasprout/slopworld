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

        void DoFields(Rect r)
        {
            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(r, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
                return;
            }

            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, r.height));
            _scroll.Begin(r, view);

            // Begun far taller than it is, so a control that would cross the bottom does not
            // start a second column and drop the rest of the form on top of itself.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            // ---------------------------------------------------------------- Anthropic
            SlopWidgets.SectionHeading(l, "Anthropic");
            _cfg.Usage = SlopWidgets.Checkbox(l, "Poll for what is left of the subscription",
                _cfg.Usage,
                "The daemon reads the OAuth token Claude Code keeps on this machine and " +
                "asks Anthropic. Off means it never touches that file.");

            if (_cfg.Usage)
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Credentials file");
                _cfg.ClaudeCredentials =
                    SlopWidgets.Field(l, "usage.creds", _cfg.ClaudeCredentials);

                l.Gap(SlopWidgets.GapM);
                // Icon rows for the Anthropic windows. The extra-usage row is
                // here rather than under OpenRouter because it is the one Claude Code's
                // own /usage answers with.
                IconRow(l, "claude_session", "5-hour window");
                IconRow(l, "claude_week", "Weekly limit");
                // Any per-model weekly limits the daemon reports.
                foreach (var key in LiveKeys())
                    if (key.StartsWith("claude_week_"))
                        IconRow(l, key, null);
                IconRow(l, "claude_spend", "Extra usage");
            }

            // ---------------------------------------------------------------- OpenRouter
            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenRouter");
            _cfg.Openrouter = SlopWidgets.Checkbox(l, "Poll for the credit balance",
                _cfg.Openrouter,
                "Credits bought less credits spent, which is what the pi agent draws down. " +
                "Off means the daemon never reads the key and never calls OpenRouter.");

            l.Gap(SlopWidgets.GapS);
            l.Label("Key file (blank reads $OPENROUTER_API_KEY)");
            _cfg.OpenrouterKeyFile =
                SlopWidgets.Field(l, "usage.orkey", _cfg.OpenrouterKeyFile);
            Note(l, "Used by credit polling and generated titles. The daemon reads it directly; " +
                    "title requests never expose the key inside an agent sandbox.");

            if (_cfg.Openrouter)
            {
                l.Gap(SlopWidgets.GapM);
                IconRow(l, "openrouter_balance", "Credit balance");
            }

            // ---------------------------------------------------------------- OpenAI
            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenAI / Codex");
            _cfg.Openai = SlopWidgets.Checkbox(l, "Poll for Codex usage limits",
                _cfg.Openai,
                "The daemon reads Codex's ChatGPT login and asks for the primary and " +
                "secondary usage windows. Off means it never touches that file.");

            if (_cfg.Openai)
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Codex credentials file");
                _cfg.OpenaiCredentials =
                    SlopWidgets.Field(l, "usage.openai.creds", _cfg.OpenaiCredentials);

                l.Gap(SlopWidgets.GapM);
                IconRow(l, "openai_session", "Primary window");
                IconRow(l, "openai_week", "Secondary window");
            }

            // ---------------------------------------------------- automatic titles
            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Automatic task titles");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    "Codex titles: " + TitlePolicyLabel(_cfg.AgentTitles)))
                OpenTitlePolicyMenu(false);
            Note(l, "Names a Codex session from its submitted prompt. The request uses " +
                    "OpenRouter; it is independent of the credit-balance poll above.");

            if (_cfg.AgentTitles != "never")
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Title model");
                _cfg.TitleModel = SlopWidgets.Field(l, "usage.title.model", _cfg.TitleModel);
                Note(l, "At most 2,000 characters of each eligible prompt are sent to " +
                        "OpenRouter. Use the key file or $OPENROUTER_API_KEY above.");
            }

            l.Gap(SlopWidgets.GapM);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    "Pi titles: " + TitlePolicyLabel(_cfg.PiTitles)))
                OpenTitlePolicyMenu(true);
            Note(l, "Pi defaults to every prompt. The daemon applies this setting before input " +
                    "reaches Pi, so it takes effect in the current session.");

            if (_cfg.PiTitles != "never")
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Pi title model");
                _cfg.PiTitleModel = SlopWidgets.Field(l, "usage.pi.title.model",
                    _cfg.PiTitleModel);
                Note(l, "At most 2,000 characters of each eligible prompt are sent to " +
                        "OpenRouter. Use the key file or $OPENROUTER_API_KEY above.");
            }

            // ---------------------------------------------------------------- Both
            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Both");
            l.Label("Seconds between polls");
            _pollSecs = SlopWidgets.Field(l, "usage.poll", _pollSecs);
            Note(l, "A failed poll backs off on its own, doubling to half an hour, and each " +
                    "seller keeps its own place in that queue: one being down never takes " +
                    "the other's numbers off the screen.");

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();
        }

        static void Note(Listing_Standard l, string text)
        {
            GUI.color = SlopWidgets.Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        static string TitlePolicyLabel(string policy)
        {
            switch (policy)
            {
                case "once": return "First prompt in each conversation";
                case "always": return "Every prompt";
                default: return "Off";
            }
        }

        void OpenTitlePolicyMenu(bool pi)
        {
            Find.WindowStack.Add(new SlopMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Off", () => SetTitlePolicy(pi, "never")),
                new FloatMenuOption("First prompt in each conversation", () =>
                    SetTitlePolicy(pi, "once")),
                new FloatMenuOption("Every prompt", () => SetTitlePolicy(pi, "always")),
            }));
        }

        void SetTitlePolicy(bool pi, string policy)
        {
            if (pi) _cfg.PiTitles = policy;
            else _cfg.AgentTitles = policy;
        }


        // ------------------------------------------------------------------ icon row

        // One line in the fields list: the long name, and the icon, which is the button.
        //
        //   [Long name               ] [icon]
        //
        // One target rather than an icon and two buttons beside it. Clearing a choice is the
        // first cell of the grid this opens, not a control out here, and that is the whole
        // argument: automatic is something you *pick*, the same way silver is, where a greyed
        // "auto" button next to the icon said it was a different kind of thing.
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
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x, row.y, col - SlopWidgets.GapXS, row.height),
                UsageReadout.Long(key, hint ?? key));
            Text.Anchor = wasAnchor;

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
                pickRect, WindowLayer.Super, () =>
            {
                var r = new Rect(0f, 0f, pickW, pickH);

                // Title bar: the key name and an X button.
                Text.Font = GameFont.Small;
                SlopWidgets.RowLabel(
                    new Rect(r.x + 8f, r.y + 4f, r.width - 60f, SlopWidgets.LineH),
                    UsageReadout.Long(key));

                if (SlopWidgets.Button(
                        new Rect(r.width - 48f, r.y + 2f, 44f, SlopWidgets.RowBtnH), "X",
                        SlopWidgets.Btn.Ghost))
                    _pickingKey = null;

                // Grid of icons.
                const float Cell = 38f;
                const float IconSize = 30f;
                float gridTop = r.y + 4f + SlopWidgets.LineH + SlopWidgets.GapXS;
                float gridH = r.height - gridTop - 8f;
                int perLine = Mathf.Max(1, Mathf.FloorToInt(
                    (r.width - SlopWidgets.GapM) / Cell));
                float gridW = perLine * Cell;
                var gridRect = new Rect(r.x + (r.width - gridW) / 2f, gridTop, gridW, gridH);

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
            }, true, false, 1f);
        }


        // ------------------------------------------------------------------ palette

        // The icons the picker offers. A hand-picked subset and not the database: vanilla
        // counts a hundred and twenty-odd things as resources and generates a meat def per
        // animal on top of that, and most of the difference is one texture tinted - twenty
        // leathers, twenty eggs, five stone blocks. Those are cells, not choices, and past a
        // screenful this grid owes a search box it has no room for.
        //
        // Grouped by no category, only laid out in runs so the order reads: the grid is one
        // screenful and scanning it is faster than any heading would be.
        //
        // Names are checked against the game's own defs - eleven of the originals here were
        // typed rather than looked up (`FineMeal` for `MealFine`, `Thrumbofur` for
        // `Leather_Thrumbo`) and Choices drops what it cannot resolve, so the picker had been
        // quietly drawing twenty-five cells of thirty-six. Carpet is gone entirely: it is one
        // stuffed TerrainDef in 1.6 and never was a ThingDef.
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
            // one texture and differ only in colour, so these are the three that read apart
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
                var was = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(foot.Rest(), _error);
                GUI.color = Color.white;
                Text.Anchor = was;
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
