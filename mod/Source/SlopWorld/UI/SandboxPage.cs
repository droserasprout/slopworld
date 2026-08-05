using System.Collections.Generic;
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

        Vector2 _presetScroll, _previewScroll;
        // The preset whose preview sits beside the list. Kept by reference and dropped when
        // a reload no longer offers it, so a stale name is never previewed.
        PresetInfo _selected;

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
                // Global is a short row of three boxes on top; the presets take the rest of
                // the page and get the scroll, so both halves are read top to bottom.
                DoGlobal(new Rect(inner.x, inner.y, inner.width, 200f));
                float presetsY = inner.y + 200f + 12f;
                DoPresets(new Rect(inner.x, presetsY, inner.width, inner.yMax - presetsY));
            }

            DoFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 32f));
        }

        // The "Global" section: `[sandbox]`, the base every sandbox is built on. Three
        // boxes in one row, so the three groups read side by side. Whether an agent is
        // sandboxed at all is its project's answer, never this page's.
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

            float gap = 8f;
            float boxW = (r.width - gap * 2f) / 3f;
            float boxH = r.yMax - top;
            _roPaths = SlopWidgets.PathList(new Rect(r.x, top, boxW, boxH),
                "Read-only binds", _roPaths);
            _rwPaths = SlopWidgets.PathList(new Rect(r.x + boxW + gap, top, boxW, boxH),
                "Read-write binds", _rwPaths);
            _passEnv = SlopWidgets.PathList(new Rect(r.x + (boxW + gap) * 2f, top, boxW, boxH),
                "Passed env vars", _passEnv);
        }

        // The "Presets" section: the daemon's preset directory on the left, and beside it a
        // preview of whichever preset is selected. The preview is read in the same three
        // groups the Global half edits, so the base can be held against what adds to it.
        void DoPresets(Rect r)
        {
            Heading(r, "Presets");
            float top = r.y + 30f;
            float boxH = r.yMax - top;

            var presets = SessionHub.Instance.Presets;
            if (_selected != null && !presets.Contains(_selected))
                _selected = null;

            // Two columns: the list, and the selected preset's preview. The preview wants the
            // read, so it gets the wider half.
            float previewW = Mathf.Min(380f, r.width * 0.45f);
            float listW = r.width - previewW - 12f;
            DoPresetList(new Rect(r.x, top, listW, boxH));
            DoPreview(new Rect(r.x + listW + 12f, top, previewW, boxH));
        }

        // The list, grouped by category the way the project dialog groups its checkboxes.
        // A click picks the row for the preview; it is not a checkbox here - a project
        // ticks presets, this page only shows what they are.
        void DoPresetList(Rect r)
        {
            var presets = SessionHub.Instance.Presets;
            var groups = presets
                .OrderBy(p => Category(p), System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .GroupBy(Category)
                .ToList();

            float h = (presets.Count + groups.Count) * 24f + 8f;
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(h, r.height));
            Widgets.BeginScrollView(r, ref _presetScroll, view);

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
                    if (p == _selected)
                        Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.16f));
                    Widgets.Label(cell, p.Name);
                    if (Widgets.ButtonInvisible(cell))
                        _selected = p;
                    TooltipHandler.TipRegion(cell,
                        $"{p.Description}\n\n{string.Join("\n", p.Gives.ToArray())}");
                    y += 24f;
                }
            }
            Widgets.EndScrollView();
        }

        // The selected preset, in the same three groups the Global half edits, each row
        // parted by a rule so "read-only", "read-write" and "env" read as the three things
        // a preset can add rather than as one list.
        void DoPreview(Rect r)
        {
            if (_selected == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.x, r.y, r.width, 24f),
                    "Select a preset to see what it adds.");
                GUI.color = Color.white;
                return;
            }

            var p = _selected;
            var view = new Rect(0f, 0f, r.width - 18f,
                Mathf.Max(Measure(p, r.width - 18f), r.height));
            Widgets.BeginScrollView(r, ref _previewScroll, view);

            float y = 0f;
            y = Row(view, y, "Read-only binds", p.Ro);
            y = Rule(view.width, y);
            y = Row(view, y, "Read-write binds", p.Rw);
            y = Rule(view.width, y);
            Row(view, y, "Passed env vars", p.Env);

            Widgets.EndScrollView();
        }

        static float Row(Rect view, float y, string label, List<string> items)
        {
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(0f, y, view.width, 20f), label);
            GUI.color = Color.white;
            y += 22f;

            string text = TextOf(items);
            float h = Text.CalcHeight(text, view.width);
            Widgets.Label(new Rect(0f, y, view.width, h), text);
            return y + h + 4f;
        }

        static float Rule(float width, float y)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawBoxSolid(new Rect(0f, y, width, 1f), GUI.color);
            GUI.color = Color.white;
            return y + 12f;
        }

        static float Measure(PresetInfo p, float width)
        {
            float y = 0f;
            y += 22f + Text.CalcHeight(TextOf(p.Ro), width) + 4f;
            y += 12f;
            y += 22f + Text.CalcHeight(TextOf(p.Rw), width) + 4f;
            y += 12f;
            y += 22f + Text.CalcHeight(TextOf(p.Env), width);
            return y;
        }

        static string TextOf(List<string> items) =>
            items.Count > 0 ? string.Join("\n", items.ToArray()) : "(nothing)";

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