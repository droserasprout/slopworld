using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What this machine does, drawn as the first page of the options menu rather than as a
    // window of its own. The daemon, the defaults, and the game. The sandbox base and
    // presets moved to their own tab because that is a different question: what the ground
    // under every agent is, and what adds to it.
    //
    // The connection is not here either. Where the daemon listens and where the game dials
    // are one question - they are the same machine - and only one of the two ends can be
    // edited with the socket down, so mod settings owns it and this page states it. State
    // rules keep to the raw editor behind "Edit as TOML": they are regexes, and a text box
    // is the honest widget for a regex.
    //
    // A page rather than a Window because SlopOptions hangs it off an OptionCategoryDef;
    // it owns no chrome and closes with the dialog around it. See SlopOptions.
    public class ConfigPage
    {
        SlopConfig _cfg;
        string _path = "";
        string _error;
        bool _loaded;

        // Free-text mirrors of the typed fields, so a half-typed number is not clamped
        // out from under the player mid-keystroke.
        string _pollMs, _history;

        readonly SmoothScroll _scroll = new SmoothScroll();
        // Last frame's measured height for the field column. The listing is begun on a
        // rect far taller than it needs, so it never breaks to a second column, and what
        // it actually used is what the scroll view is sized from next frame.
        float _fieldsH;

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    _path = j["path"].AsString();
                    _pollMs = _cfg.PollMs.ToString();
                    _history = _cfg.HistoryLimit.ToString();
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        // The category row is the title, so all this needs of the top of its rect is to
        // say which file is being edited.
        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect, _loaded ? _path : "loading...");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                DoFields(inner);
            }

            DoFooter(SlopWidgets.FooterBar(rect));
        }


        void DoFields(Rect r)
        {
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, r.height));
            _scroll.Begin(r, view);

            // Begun far taller than it is, so a control that would cross the bottom does
            // not start a second column and drop the rest of the form on top of itself.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            DoConnectionNote(l);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Commands");
            Note(l, "What an agent or errand runs when it names none of its own. Both name a " +
                    "command preset; what one runs is a TOML file beside this one, and the " +
                    "agent dialog lists them.");

            l.Gap(SlopWidgets.GapS);
            l.Label("Agent");
            _cfg.Agent = SlopWidgets.Field(l, "cfg.agent", _cfg.Agent);
            l.Gap(SlopWidgets.GapS);
            // What a shell errand runs. tmux hands it a pty, so it is interactive without
            // being told to be.
            l.Label("Shell");
            _cfg.Shell = SlopWidgets.Field(l, "cfg.shell", _cfg.Shell);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "The game");
            l.Label("Game command (blank disables restarting the game from here)");
            _cfg.GameCmd = SlopWidgets.Field(l, "cfg.gamecmd", _cfg.GameCmd);
            l.Gap(SlopWidgets.GapXS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    "Save the colony and restart the game"))
                ConfirmRestartGame();

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "The daemon");
            l.Label("tmux socket");
            _cfg.TmuxSocket = SlopWidgets.Field(l, "cfg.tmux", _cfg.TmuxSocket);
            l.Gap(SlopWidgets.GapS);
            l.Label("State tick, ms (how often a quiet session decays to idle)");
            _pollMs = SlopWidgets.Field(l, "cfg.pollms", _pollMs);
            l.Gap(SlopWidgets.GapS);
            l.Label("Scrollback lines kept per session");
            _history = SlopWidgets.Field(l, "cfg.history", _history);
            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Private state storage..."))
                Find.WindowStack.Add(new StateStorageDialog());

            l.Gap(SlopWidgets.GapS);
            GUI.color = SlopWidgets.Warn;
            l.Label("Socket, tick and scrollback are read once at startup: they are " +
                    "saved now and take hold when slopd restarts.");
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "This install");
            var s = SlopWorldMod.Instance.settings;
            s.grandmaMode = SlopWidgets.Checkbox(l, "Grandma's visiting", s.grandmaMode);
            Note(l, "No fun allowed! Disable gore, vomit, and offensive/harmful tips. " +
                    "Put it back when she leaves.");

            l.Gap(SlopWidgets.GapS);
            s.ecoMode = SlopWidgets.Checkbox(l, "Eco mode", s.ecoMode);
            Note(l, "Stop the colony and stop drawing it: the clock is held, the map and " +
                    "its weather are not rendered, and the frames are capped. The agents " +
                    "are the daemon's and keep running. With no terminal up you get the " +
                    "menu's background instead of the map.");

            // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
            // does nothing, and the note above is what says so. Stepped to twentieths because
            // the value keys a material - see Eco.Shade.
            if (s.ecoMode)
            {
                l.Gap(SlopWidgets.GapS);
                s.ecoDim = Mathf.Round(SlopWidgets.Slider(l, "Backdrop dimming", s.ecoDim,
                    0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                Note(l, "How far the picture behind the agents is taken down. At zero it is " +
                        "the menu's own background at full strength.");
            }

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();
        }

        // The two doors off this page, and the only two things on it that are not
        // `config.toml`. Both are mod settings - RimWorld's own file, not the daemon's.
        //
        // The connection is stated rather than edited: the mod's end of it is the only
        // half that can be changed while the socket is down, which is exactly when it
        // needs changing, so mod settings owns it and this page points at it. The
        // daemon's own bind is TOML-only - slopd reads it at startup, so a box here that
        // took effect on the next restart would mostly read as a field that did nothing.
        void DoConnectionNote(Listing_Standard l)
        {
            SlopWidgets.SectionHeading(l, "Connection");
            Note(l, $"This game dials {SlopClient.BaseUrl} ({SessionHub.Instance.Status}). " +
                    $"The daemon is bound to {_cfg.Bind}.");

            var row = l.GetRect(SlopWidgets.BtnH);
            float w = (row.width - SlopWidgets.GapS) / 2f;
            if (SlopWidgets.Button(new Rect(row.x, row.y, w, row.height), "Connection..."))
                Find.WindowStack.Add(new Dialog_ModSettings(SlopWorldMod.Instance));

            // The terminal's own settings are now a tab of this same dialog, left of
            // Usage: the honest answer to the press is a tab swap rather than a window.
            if (SlopWidgets.Button(new Rect(row.x + w + SlopWidgets.GapS, row.y, w, row.height),
                    "Appearance..."))
                SlopOptions.OpenTerminalTab();
        }

        // A second line about the line above it. Every page here has one of these; this is
        // the only thing that distinguishes it from body text.
        static void Note(Listing_Standard l, string text)
        {
            GUI.color = SlopWidgets.Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);

            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit as TOML", SlopWidgets.Btn.Ghost)) ConfigWindow.Open();
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

            // Between the two ends, which is where the room actually is - the offsets that
            // used to put it there were counted off labels this bar now measures itself.
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

            if (int.TryParse(_pollMs, out int p)) _cfg.PollMs = Mathf.Clamp(p, 20, 5000);
            if (int.TryParse(_history, out int h)) _cfg.HistoryLimit = Mathf.Clamp(h, 0, 100000);

            SlopClient.Put("/api/config/patch", _cfg.ToPatchJson(),
                _ =>
                {
                    _error = null;
                    SlopOptions.Reread();
                    SessionHub.Instance.Refresh();
                    Messages.Message("SlopWorld: config saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }

        // The mod's assembly is read once at startup, so a rebuilt mod only reaches the
        // screen in a fresh process - which, with the colony saved on the way out and
        // resumed on the way back in, costs a loading screen and nothing else. The agents
        // are the daemon's and it is not restarting.
        void ConfirmRestartGame()
        {
            if (string.IsNullOrEmpty((_cfg.GameCmd ?? "").Trim()))
            {
                SlopWidgets.Fail("set a game command above and save first");
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Save the colony and restart RimWorld? The daemon starts it again a few " +
                "seconds later and the agents keep running throughout.",
                // Nothing here saves or quits: the daemon answers this request by telling every
                // client to do exactly that, and doing it twice is two saves and two shutdowns.
                // The error road is still ours, because a refused request sends no such event.
                () => SlopClient.Post("/api/game/restart", "{\"delay_ms\":1000}", _ => { }, SlopWidgets.Fail)));
        }

    }
}
