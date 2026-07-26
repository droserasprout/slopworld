using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Settings GUI over the daemon's config.toml. Three tabs: the daemon itself,
    /// the sandbox every agent runs in, and the sessions. State rules keep to the
    /// raw editor behind "Edit as TOML" - they are regexes, and a text box is the
    /// honest widget for a regex.
    /// </summary>
    public class ConfigMenuWindow : Window
    {
        enum Tab { Daemon, Sandbox, Sessions }

        Tab _tab = Tab.Daemon;
        SlopConfig _cfg;
        string _path = "";
        string _error;
        bool _loaded;

        // Free-text mirrors of the typed fields, so a half-typed number is not
        // clamped out from under the player mid-keystroke.
        string _pollMs, _history, _usagePoll;
        string _roPaths, _rwPaths, _passEnv;

        Vector2 _scroll;

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<ConfigMenuWindow>();
            if (open != null) { open.Close(); return; }

            var w = new ConfigMenuWindow();
            w.Load();
            Find.WindowStack.Add(w);
        }

        public ConfigMenuWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    _path = j["path"].AsString();
                    _pollMs = _cfg.PollMs.ToString();
                    _history = _cfg.HistoryLimit.ToString();
                    _usagePoll = _cfg.UsagePollSecs.ToString();
                    _roPaths = SlopConfig.Lines(_cfg.RoPaths);
                    _rwPaths = SlopConfig.Lines(_cfg.RwPaths);
                    _passEnv = SlopConfig.Lines(_cfg.PassEnv);
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });

            SessionHub.Instance.Refresh();
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 34f), "Configuration");
            Text.Font = GameFont.Small;

            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(rect.x + 160f, rect.y + 8f, rect.width - 160f, 24f),
                _loaded ? _path : "loading...");
            GUI.color = Color.white;

            var body = new Rect(rect.x, rect.y + 68f, rect.width, rect.height - 68f - 40f);
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, new List<TabRecord>
            {
                new TabRecord("Daemon", () => _tab = Tab.Daemon, _tab == Tab.Daemon),
                new TabRecord("Sandbox", () => _tab = Tab.Sandbox, _tab == Tab.Sandbox),
                new TabRecord("Sessions", () => _tab = Tab.Sessions, _tab == Tab.Sessions),
            });

            var inner = body.ContractedBy(12f);
            if (!_loaded)
            {
                DrawNotice(inner, _error ?? "Waiting for the daemon...");
            }
            else
            {
                switch (_tab)
                {
                    case Tab.Daemon: DoDaemon(inner); break;
                    case Tab.Sandbox: DoSandbox(inner); break;
                    default: DoSessions(inner); break;
                }
            }

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));
        }

        void DrawNotice(Rect r, string msg)
        {
            GUI.color = _error != null ? new Color(0.95f, 0.45f, 0.45f) : Color.gray;
            Widgets.Label(r, msg);
            GUI.color = Color.white;
        }

        // ----------------------------------------------------------- daemon tab

        void DoDaemon(Rect r)
        {
            var l = new Listing_Standard();
            l.Begin(r);

            l.Label("Bind address");
            _cfg.Bind = l.TextEntry(_cfg.Bind);

            l.Gap(4f);
            l.Label("Token (blank = no auth, fine on a loopback bind)");
            _cfg.Token = l.TextEntry(_cfg.Token);

            l.Gap(4f);
            l.Label("tmux socket");
            _cfg.TmuxSocket = l.TextEntry(_cfg.TmuxSocket);

            l.Gap(4f);
            l.Label("State tick, ms (how often a quiet session decays to idle)");
            _pollMs = l.TextEntry(_pollMs);

            l.Gap(4f);
            l.Label("Scrollback lines kept per session");
            _history = l.TextEntry(_history);

            l.Gap(4f);
            l.Label("Game command (blank disables restarting the game from here)");
            _cfg.GameCmd = l.TextEntry(_cfg.GameCmd);

            l.Gap(6f);
            l.CheckboxLabeled("Poll Anthropic for what is left of the subscription",
                ref _cfg.Usage,
                "Feeds the readout in the top-left corner. The daemon reads the OAuth " +
                "token Claude Code keeps on this machine and asks Anthropic; off means " +
                "it never touches that file.");

            if (_cfg.Usage)
            {
                l.Label("Seconds between usage polls");
                _usagePoll = l.TextEntry(_usagePoll);

                l.Label("Claude credentials file");
                _cfg.ClaudeCredentials = l.TextEntry(_cfg.ClaudeCredentials);
            }

            l.Gap(6f);
            if (l.ButtonText("Save the colony and restart the game"))
                ConfirmRestartGame();

            l.Gap(10f);
            GUI.color = new Color(0.85f, 0.75f, 0.45f);
            l.Label("Bind address, socket and tick are read once at startup: they are " +
                    "saved now and take hold when slopd restarts.");
            GUI.color = Color.white;

            l.Gap(6f);
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            l.Label($"The mod talks to {SlopClient.BaseUrl} ({SessionHub.Instance.Status}). " +
                    "Change that end in Options > Mod settings.");
            GUI.color = Color.white;

            l.End();
        }

        // ---------------------------------------------------------- sandbox tab

        void DoSandbox(Rect r)
        {
            var l = new Listing_Standard();
            l.Begin(r);

            l.CheckboxLabeled("Sandbox agents with bubblewrap", ref _cfg.SandboxEnabled,
                "Off means every agent runs with your full user account. A session can " +
                "still opt out on its own.");
            l.Gap(6f);
            l.End();

            // Three path lists side by side: they are read together, and stacking
            // them would push the last one off the tab.
            float colW = (r.width - 16f) / 3f;
            float top = r.y + 64f;
            float h = r.height - 64f;

            _roPaths = PathList(new Rect(r.x, top, colW, h),
                "Read-only binds", _roPaths);
            _rwPaths = PathList(new Rect(r.x + colW + 8f, top, colW, h),
                "Read-write binds", _rwPaths);
            _passEnv = PathList(new Rect(r.x + (colW + 8f) * 2f, top, colW, h),
                "Passed env vars", _passEnv);
        }

        /// <summary>One entry per line; blank lines are dropped on save.</summary>
        static string PathList(Rect r, string label, string text)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), label);
            var box = new Rect(r.x, r.y + 24f, r.width, r.height - 24f);
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.25f));
            return Widgets.TextArea(box.ContractedBy(4f), text);
        }

        // --------------------------------------------------------- sessions tab

        void DoSessions(Rect r)
        {
            var l = new Listing_Standard();
            l.Begin(new Rect(r.x, r.y, r.width, 92f));

            l.Label("Command for sessions that do not set their own");
            l.Gap(2f);

            var row = l.GetRect(28f);
            Widgets.Label(new Rect(row.x, row.y + 3f, 60f, 24f), "Agent");
            _cfg.Agent = Widgets.TextField(new Rect(row.x + 60f, row.y, 220f, 24f), _cfg.Agent);

            l.End();

            var list = new Rect(r.x, r.y + 96f, r.width, r.height - 96f - 36f);
            var sessions = SessionHub.Instance.Sessions;
            var view = new Rect(0f, 0f, list.width - 18f, Mathf.Max(sessions.Count * 34f, list.height));

            Widgets.BeginScrollView(list, ref _scroll, view);
            float y = 0f;
            foreach (var s in sessions.ToList())
            {
                DrawSessionRow(new Rect(0f, y, view.width, 30f), s);
                y += 34f;
            }
            if (sessions.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(4f, 4f, view.width - 8f, 24f),
                    "No sessions in config.toml yet.");
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(r.x, list.yMax + 4f, 140f, 28f), "Add session"))
                Find.WindowStack.Add(new EditSessionDialog(null));
        }

        void DrawSessionRow(Rect r, SessionInfo s)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawHighlightIfMouseover(r);

            Widgets.Label(new Rect(r.x + 6f, r.y + 4f, 150f, 22f), s.Name);

            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            string flags = (s.Sandbox ? "bwrap" : "unsandboxed") +
                           (s.Net ? "" : ", no net") +
                           (s.Autostart ? ", autostart" : "");
            Widgets.Label(new Rect(r.x + 160f, r.y + 4f, r.width - 290f, 22f),
                $"{s.Dir}  ({flags})");
            GUI.color = Color.white;

            float x = r.xMax - 6f;

            x -= 58f;
            if (Widgets.ButtonText(new Rect(x, r.y + 3f, 54f, 24f), "Edit"))
                Find.WindowStack.Add(new EditSessionDialog(s));

            x -= 58f;
            if (Widgets.ButtonText(new Rect(x, r.y + 3f, 54f, 24f), "Del"))
            {
                var name = s.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove session '{name}'? This kills the tmux session and drops it from config.toml.",
                    () => SessionHub.Instance.Remove(name, Fail),
                    destructive: true));
            }
        }

        // -------------------------------------------------------------- footer

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
            // Floored at the same 10s the poller enforces, so what the GUI shows
            // after a save is what the daemon is actually doing.
            if (int.TryParse(_usagePoll, out int u)) _cfg.UsagePollSecs = Mathf.Clamp(u, 10, 3600);
            _cfg.RoPaths = SlopConfig.Split(_roPaths);
            _cfg.RwPaths = SlopConfig.Split(_rwPaths);
            _cfg.PassEnv = SlopConfig.Split(_passEnv);

            SlopClient.Put("/api/config/values", _cfg.ToJson(),
                _ =>
                {
                    _error = null;
                    SessionHub.Instance.Refresh();
                    Messages.Message("SlopWorld: config saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }

        /// <summary>
        /// The other half of a redeploy. The mod's assembly is read once at
        /// startup, so a rebuilt mod only reaches the screen in a fresh process -
        /// and with the colony saved on the way out and resumed on the way back
        /// in, that costs a loading screen and nothing else. The agents never
        /// notice: they are the daemon's, and the daemon is not restarting.
        ///
        /// slopd does the relaunch because nothing inside the game outlives its
        /// own shutdown, and the daemon already does.
        /// </summary>
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
                // Nothing here saves or quits: the daemon answers this request by
                // telling every client to do exactly that, and doing it twice -
                // once from the reply and once from the event - is two saves and
                // two shutdowns. The error road is still ours, because a request
                // that was refused sends no such event and the person is owed a
                // reason.
                () => SlopClient.Post("/api/game/restart", "{\"delay_ms\":1000}", _ => { }, Fail)));
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }
}
