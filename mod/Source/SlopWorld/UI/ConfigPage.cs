using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What this machine does, drawn as the first page of the options menu rather than as a
    // window of its own. There was only ever one subject here: the daemon, and the base
    // every sandbox is built on. The rest went because it was answering somebody else's
    // question - the agent list is the `agents` window, and the sandbox a project runs in
    // is that project's, and what the daemon is allowed to ask Anthropic or OpenRouter for
    // is the Usage page, there being two sellers to keep apart and a palette to draw.
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
        string _roPaths, _rwPaths, _passEnv;

        Vector2 _scroll;
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
                    _roPaths = SlopConfig.Lines(_cfg.RoPaths);
                    _rwPaths = SlopConfig.Lines(_cfg.RwPaths);
                    _passEnv = SlopConfig.Lines(_cfg.PassEnv);
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        // The category row is the title, so all this needs of the top of its rect is to
        // say which file is being edited.
        public void Draw(Rect rect)
        {
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                _loaded ? _path : "loading...");
            GUI.color = Color.white;

            var body = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f - 40f);
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(12f);

            if (!_loaded)
            {
                GUI.color = _error != null ? new Color(0.95f, 0.45f, 0.45f) : Color.gray;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                // The fields want reading top to bottom and the bind lists want width,
                // so they get a column each rather than the lists being pushed under a
                // form that scrolls.
                float rightW = Mathf.Min(300f, inner.width * 0.42f);
                float leftW = inner.width - rightW - 12f;
                DoFields(new Rect(inner.x, inner.y, leftW, inner.height));
                DoBaseBinds(new Rect(inner.xMax - rightW, inner.y, rightW, inner.height));
            }

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));
        }


        void DoFields(Rect r)
        {
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(_fieldsH, r.height));
            Widgets.BeginScrollView(r, ref _scroll, view);

            // Begun far taller than it is, so a control that would cross the bottom does
            // not start a second column and drop the rest of the form on top of itself.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            DoConnectionNote(l);

            l.Gap(8f);
            l.Label("Command presets for agents and errands that name none of their own");
            l.Gap(2f);
            l.Label("Agent");
            _cfg.Agent = l.TextEntry(_cfg.Agent);
            l.Gap(2f);
            // What a shell errand runs. tmux hands it a pty, so it is interactive without
            // being told to be.
            l.Label("Shell");
            _cfg.Shell = l.TextEntry(_cfg.Shell);
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label("Both name a command preset. What one runs is a TOML file beside this " +
                    "one; the agent dialog lists them.");
            GUI.color = Color.white;

            l.Gap(10f);
            l.Label("Game command (blank disables restarting the game from here)");
            _cfg.GameCmd = l.TextEntry(_cfg.GameCmd);
            l.Gap(4f);
            if (l.ButtonText("Save the colony and restart the game"))
                ConfirmRestartGame();

            l.Gap(10f);
            l.Label("tmux socket");
            _cfg.TmuxSocket = l.TextEntry(_cfg.TmuxSocket);
            l.Gap(2f);
            l.Label("State tick, ms (how often a quiet session decays to idle)");
            _pollMs = l.TextEntry(_pollMs);
            l.Gap(2f);
            l.Label("Scrollback lines kept per session");
            _history = l.TextEntry(_history);

            l.Gap(6f);
            GUI.color = new Color(0.85f, 0.75f, 0.45f);
            l.Label("Socket, tick and scrollback are read once at startup: they are " +
                    "saved now and take hold when slopd restarts.");
            GUI.color = Color.white;

            _fieldsH = l.CurHeight + 8f;
            l.End();

            Widgets.EndScrollView();
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
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label($"This game dials {SlopClient.BaseUrl} ({SessionHub.Instance.Status}). " +
                    $"The daemon is bound to {_cfg.Bind}.");
            GUI.color = Color.white;

            var row = l.GetRect(30f);
            float w = (row.width - 8f) / 2f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, w, 30f), "Connection..."))
                Find.WindowStack.Add(new Dialog_ModSettings(SlopWorldMod.Instance));

            // Judged against a running pane, which is why it is also on the terminal's own
            // gear. Reachable from both, because a setting you can find in one place only,
            // and that place a running agent, reads as a setting that does not exist.
            if (Widgets.ButtonText(new Rect(row.x + w + 8f, row.y, w, 30f), "Appearance..."))
                TerminalSettingsWindow.Open();
        }

        // This page's own window, which sits in the room the column and the line
        // leave and has just been told there is more of it or less.
        void DoBaseBinds(Rect r)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), "Bound into every sandbox");

            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            var note = new Rect(r.x, r.y + 22f, r.width, 52f);
            Widgets.Label(note,
                "The base every project builds on. A project's own presets and binds are " +
                "added to these; whether an agent is sandboxed at all is its project's " +
                "answer.");
            GUI.color = Color.white;

            float top = note.yMax + 6f;
            float h = (r.yMax - top - 16f) / 3f;

            _roPaths = PathList(new Rect(r.x, top, r.width, h),
                "Read-only binds", _roPaths);
            _rwPaths = PathList(new Rect(r.x, top + h + 8f, r.width, h),
                "Read-write binds", _rwPaths);
            _passEnv = PathList(new Rect(r.x, top + (h + 8f) * 2f, r.width, h),
                "Passed env vars", _passEnv);
        }

        // One entry per line; blank lines are dropped on save.
        static string PathList(Rect r, string label, string text)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), label);
            var box = new Rect(r.x, r.y + 22f, r.width, Mathf.Max(r.height - 22f, 40f));
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.25f));
            return Widgets.TextArea(box.ContractedBy(4f), text);
        }


        void DoFooter(Rect bar)
        {
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 110f, 30f), "Reload"))
                Load();

            if (Widgets.ButtonText(new Rect(bar.x + 118f, bar.y, 140f, 30f), "Edit as TOML"))
                ConfigWindow.Open();

            if (_error != null && _loaded)
            {
                GUI.color = new Color(0.95f, 0.45f, 0.45f);
                Widgets.Label(new Rect(bar.x + 268f, bar.y + 4f, bar.width - 400f, 24f), _error);
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 30f), "Save"))
                Save();
        }

        void Save()
        {
            if (!_loaded) return;

            if (int.TryParse(_pollMs, out int p)) _cfg.PollMs = Mathf.Clamp(p, 20, 5000);
            if (int.TryParse(_history, out int h)) _cfg.HistoryLimit = Mathf.Clamp(h, 0, 100000);
            _cfg.RoPaths = SlopConfig.Split(_roPaths);
            _cfg.RwPaths = SlopConfig.Split(_rwPaths);
            _cfg.PassEnv = SlopConfig.Split(_passEnv);

            SlopClient.Put("/api/config/values", _cfg.ToJson(),
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
                Fail("set a game command above and save first");
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Save the colony and restart RimWorld? The daemon starts it again a few " +
                "seconds later and the agents keep running throughout.",
                // Nothing here saves or quits: the daemon answers this request by telling every
                // client to do exactly that, and doing it twice is two saves and two shutdowns.
                // The error road is still ours, because a refused request sends no such event.
                () => SlopClient.Post("/api/game/restart", "{\"delay_ms\":1000}", _ => { }, Fail)));
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }
}
