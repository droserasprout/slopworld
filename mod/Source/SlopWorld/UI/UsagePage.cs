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

            // The icons are not the daemon's, so they are drawn whether or not it answered:
            // a socket that is down is exactly when somebody is in here reading rather than
            // configuring.
            float rightW = Mathf.Min(380f, inner.width * 0.45f);
            float leftW = inner.width - rightW - 12f;
            DoFields(new Rect(inner.x, inner.y, leftW, inner.height));
            DoIcons(new Rect(inner.xMax - rightW, inner.y, rightW, inner.height));

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));
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

            Heading(l, "Anthropic");
            l.CheckboxLabeled("Poll for what is left of the subscription", ref _cfg.Usage,
                "The daemon reads the OAuth token Claude Code keeps on this machine and " +
                "asks Anthropic. Off means it never touches that file.");

            if (_cfg.Usage)
            {
                l.Gap(2f);
                l.Label("Credentials file");
                _cfg.ClaudeCredentials = l.TextEntry(_cfg.ClaudeCredentials);
            }

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
            }

            // One clock for both. A balance moves slower than a rate limit, never faster,
            // so a second interval would be a knob with nothing to say.
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


        // ------------------------------------------------------------------ icons

        const float Cell = 32f;
        const float IconSize = 26f;

        // Enough to tell a row from its neighbours at a glance and no more; a list of every
        // thing in the game is a search box, and what is being picked is a silhouette.
        static readonly string[] Palette =
        {
            "Silver", "Gold", "Steel", "Plasteel", "Uranium", "Jade",
            "ComponentIndustrial", "ComponentSpacer", "AIPersonaCore", "Chemfuel",
            "Neutroamine", "MedicineIndustrial", "MedicineUltratech", "Beer",
            "Cloth", "WoodLog",
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

        // Every row the daemon is reporting, then the four worth choosing for in advance:
        // a session that has not been opened yet still has an icon waiting for it, and the
        // OpenRouter row can be dressed before the switch above it is even on.
        static List<string> Keys()
        {
            var keys = new List<string>();
            foreach (var w in SessionHub.Instance.Usage.Windows)
                if (!keys.Contains(w.Key)) keys.Add(w.Key);

            foreach (var k in new[] { "session", "week", "spend", "balance" })
                if (!keys.Contains(k)) keys.Add(k);

            return keys;
        }

        void DoIcons(Rect r)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), "Icons");

            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            var note = new Rect(r.x, r.y + 22f, r.width, 40f);
            Widgets.Label(note,
                "What stands for each row along the top. Saved on the click, here rather " +
                "than in the daemon's file: it is about this screen.");
            GUI.color = Color.white;

            float y = note.yMax + 6f;
            foreach (var key in Keys())
            {
                if (y > r.yMax - 24f) break;
                y = Row(new Rect(r.x, y, r.width, r.yMax - y), key) + 10f;
            }
        }

        // One resource: its name, then the palette wrapped to the width there is, with the
        // chosen cell boxed. Answers the y it finished at, the number of lines depending on
        // how wide the column has been dragged.
        static float Row(Rect r, string key)
        {
            var chosen = UsageReadout.Chosen(key);

            Widgets.Label(new Rect(r.x, r.y, r.width - 60f, 22f), UsageReadout.Long(key));

            // Clearing is the row saying "whichever one you would have picked", which is a
            // different answer from any particular thing - hence a button rather than a cell
            // in the palette, and greyed out when there is nothing to clear.
            var auto = new Rect(r.xMax - 56f, r.y, 56f, 22f);
            GUI.color = chosen == null ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            if (Widgets.ButtonText(auto, "auto", true, false, chosen != null))
                UsageReadout.Choose(key, null);
            GUI.color = Color.white;

            int perLine = Mathf.Max(1, Mathf.FloorToInt(r.width / Cell));
            float top = r.y + 24f;

            for (int i = 0; i < Choices.Count; i++)
            {
                var def = Choices[i];
                var cell = new Rect(r.x + (i % perLine) * Cell,
                    top + (i / perLine) * Cell, Cell, Cell);

                if (def == chosen) Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.16f));
                if (Mouse.IsOver(cell)) Widgets.DrawHighlight(cell);

                var box = new Rect(cell.x + (Cell - IconSize) / 2f,
                    cell.y + (Cell - IconSize) / 2f, IconSize, IconSize);
                Widgets.ThingIcon(box, def);
                // ThingIcon leaves GUI.color on the def's own tint.
                GUI.color = Color.white;

                TooltipHandler.TipRegion(cell, new TipSignal(def.LabelCap,
                    0x51_0F_0002 ^ (key.GetHashCode() * 31 + def.shortHash)));

                if (Widgets.ButtonInvisible(cell))
                    UsageReadout.Choose(key, def == chosen ? null : def);
            }

            int lines = Mathf.CeilToInt(Choices.Count / (float)perLine);
            return top + lines * Cell;
        }


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
