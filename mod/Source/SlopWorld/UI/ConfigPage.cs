using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // First options page for daemon/config values and game controls. Connection values remain
    // editable in mod settings when the socket is down; regex rules stay in raw TOML.
    public class ConfigPage : IOptionPage
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
                    SessionHub.Instance.Config = _cfg;
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
            GUI.color = SlopWidgets.Warn;
            l.Label("Socket, tick and scrollback are read once at startup: they are " +
                    "saved now and take hold when slopd restarts.");
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "This install");
            var s = SlopWorldMod.Instance.settings;
            bool gm = SlopWidgets.Checkbox(l, "Grandma's visiting", s.grandmaMode);
            SlopWidgets.Note(l, "No fun allowed! Disable gore, vomit, and offensive/harmful tips. " +
                    "Put it back when she leaves.");

            l.Gap(SlopWidgets.GapS);
            bool eco = SlopWidgets.Checkbox(l, "Eco mode", s.ecoMode);
            SlopWidgets.Note(l, "80% less CPU. 0.1% less guilt. You're welcome, Earth.");

            if (gm != s.grandmaMode || eco != s.ecoMode)
            {
                s.grandmaMode = gm;
                s.ecoMode = eco;
                s.MarkDirty();
            }

            // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
            // does nothing, and the note above is what says so. Stepped to twentieths because
            // the value keys a material - see Eco.Shade.
            if (s.ecoMode)
            {
                l.Gap(SlopWidgets.GapS);
                float dim = Mathf.Round(SlopWidgets.Slider(l, "Backdrop dimming", s.ecoDim,
                    0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                SlopWidgets.Note(l, "How far the picture behind the agents is taken down. At zero it is " +
                        "the menu's own background at full strength.");
                if (dim != s.ecoDim) { s.ecoDim = dim; s.MarkDirty(); }
            }

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();
        }

        // These buttons edit mod settings, not config.toml. The mod endpoint remains useful
        // while the socket is down; slopd's bind is startup-only, so it stays a displayed value.
        void DoConnectionNote(Listing_Standard l)
        {
            SlopWidgets.SectionHeading(l, "Connection");
            SlopWidgets.Note(l, $"This game dials {SlopClient.BaseUrl} ({SessionHub.Instance.Status}). " +
                    $"The daemon is bound to {_cfg.Bind}.");

            var row = l.GetRect(SlopWidgets.BtnH);
            float w = (row.width - SlopWidgets.GapS) / 2f;
            if (SlopWidgets.Button(new Rect(row.x, row.y, w, row.height), "Connection..."))
                Find.WindowStack.Add(new Dialog_ModSettings(SlopWorldMod.Instance));

            // The terminal's own settings are a child of Appearance in this dialog. Keep
            // this shortcut direct and name the destination rather than the parent group.
            if (SlopWidgets.Button(new Rect(row.x + w + SlopWidgets.GapS, row.y, w, row.height),
                    "Terminal..."))
                SlopOptions.OpenTerminalTab();
        }


        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);

            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit", SlopWidgets.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

            // Between the two ends, which is where the room actually is - the offsets that
            // used to put it there were counted off labels this bar now measures itself.
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

            if (int.TryParse(_pollMs, out int p)) _cfg.PollMs = Mathf.Clamp(p, 20, 5000);
            if (int.TryParse(_history, out int h)) _cfg.HistoryLimit = Mathf.Clamp(h, 0, 100000);

            SlopClient.Put("/api/config/patch", _cfg.ToPatchJson(),
                _ =>
                {
                    _error = null;
                    SlopOptions.Reread();
                    SessionHub.Instance.Refresh();
                    Messages.Message("SlopWorld: settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }

    }
}
