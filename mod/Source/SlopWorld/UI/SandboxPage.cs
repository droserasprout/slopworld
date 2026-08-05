using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The system-wide sandbox, as its own tab of the options menu: the base every sandbox
    // is built on under a "Global" heading, and the presets that can add to it under
    // "Presets". Split out of ConfigPage because that page answers "what does this machine
    // do" - the daemon, the defaults, the game - and the base binds were the paragraph
    // about the ground every agent runs on, which is a different question and one this tab
    // can answer with both halves at once.
    //
    // The Global half is `[sandbox]` in config.toml, saved over HTTP like the rest of the
    // machine's config. The Presets half is a directory of the daemon's preset files, drawn
    // read-only: which presets a *project* uses is that project's answer, ticked on its own
    // dialog, and this page is where the base is read against what adds to it.
    public class SandboxPage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        string _roPaths, _rwPaths, _passEnv;

        Vector2 _presetScroll;

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    _roPaths = SlopConfig.Lines(_cfg.RoPaths);
                    _rwPaths = SlopConfig.Lines(_cfg.RwPaths);
                    _passEnv = SlopConfig.Lines(_cfg.PassEnv);
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });

            // The presets are the daemon's directory, fetched on the page like the project
            // dialog fetches them, so a file added while the game is up is a name here too.
            SessionHub.Instance.LoadPresets();
        }

        public void Draw(Rect rect)
        {
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                "The base every sandbox is built on, and the presets that add to it.");
            GUI.color = Color.white;

            var body = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f - 40f);
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(12f);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : Color.gray;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                // Two halves on one tab so the base is read against the presets that add to
                // it. The binds want width and the presets want a scroll of their own, so
                // they get a column each, the way ConfigPage's fields and binds did.
                float presetsW = Mathf.Min(330f, inner.width * 0.42f);
                DoGlobal(new Rect(inner.x, inner.y, inner.width - presetsW - 12f, inner.height));
                DoPresets(new Rect(inner.xMax - presetsW, inner.y, presetsW, inner.height));
            }

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));
        }

        // The "Global" section: `[sandbox]`, the base every sandbox is built on. Whether an
        // agent is sandboxed at all is its project's answer, never this page's.
        void DoGlobal(Rect r)
        {
            Heading(r, "Global");
            float top = r.y + 30f;

            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(r.x, top, r.width, 40f),
                "The base every project builds on. A project's own presets and binds are " +
                "added to these; whether an agent is sandboxed at all is its project's " +
                "answer.");
            GUI.color = Color.white;
            top += 46f;

            float h = (r.yMax - top - 8f) / 3f;
            _roPaths = SlopWidgets.PathList(new Rect(r.x, top, r.width, h),
                "Read-only binds", _roPaths);
            _rwPaths = SlopWidgets.PathList(new Rect(r.x, top + h + 8f, r.width, h),
                "Read-write binds", _rwPaths);
            _passEnv = SlopWidgets.PathList(new Rect(r.x, top + (h + 8f) * 2f, r.width, h),
                "Passed env vars", _passEnv);
        }

        // The "Presets" section: the daemon's preset directory, grouped by category the way
        // the project dialog groups its checkboxes. Read-only here - ticking which presets a
        // project uses is that project's dialog's job - and a preset's row shows what it
        // binds in the tooltip, so the base can be read against what adds to it.
        void DoPresets(Rect r)
        {
            Heading(r, "Presets");
            float top = r.y + 30f;

            var presets = SessionHub.Instance.Presets;
            var groups = presets
                .OrderBy(p => Category(p), System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .GroupBy(Category)
                .ToList();

            float h = (presets.Count + groups.Count) * 24f + 8f;
            float boxH = r.yMax - top - 8f;
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(h, boxH));
            Widgets.BeginScrollView(new Rect(r.x, top, r.width, boxH),
                ref _presetScroll, view);

            if (presets.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, 0f, view.width, 22f),
                    "The daemon has not sent its preset list yet.");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var g in groups)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(0f, y, view.width, 22f), g.Key);
                GUI.color = Color.white;
                y += 24f;

                foreach (var p in g)
                {
                    var cell = new Rect(8f, y, view.width - 8f, 22f);
                    Widgets.Label(cell, p.Name);
                    TooltipHandler.TipRegion(cell,
                        $"{p.Description}\n\n{string.Join("\n", p.Gives.ToArray())}");
                    y += 24f;
                }
            }
            Widgets.EndScrollView();
        }

        static string Category(PresetInfo p) =>
            string.IsNullOrEmpty(p.Category) ? "other" : p.Category;

        static void Heading(Rect r, string text)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), text);
            Text.Font = GameFont.Small;
        }

        void DoFooter(Rect bar)
        {
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 110f, 30f), "Reload"))
                Load();

            if (_error != null && _loaded)
            {
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(new Rect(bar.x + 118f, bar.y + 4f, bar.width - 260f, 24f), _error);
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 30f), "Save",
                    true, false, _loaded))
                Save();
        }

        void Save()
        {
            if (!_loaded) return;

            _cfg.RoPaths = SlopConfig.Split(_roPaths);
            _cfg.RwPaths = SlopConfig.Split(_rwPaths);
            _cfg.PassEnv = SlopConfig.Split(_passEnv);

            SlopClient.Put("/api/config/values", _cfg.ToJson(),
                _ =>
                {
                    _error = null;
                    SlopOptions.Reread();
                    SessionHub.Instance.Refresh();
                    Messages.Message("SlopWorld: sandbox settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }
    }
}