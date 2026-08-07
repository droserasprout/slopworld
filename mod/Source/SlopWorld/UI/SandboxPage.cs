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

        readonly SmoothScroll _presetScroll = new SmoothScroll();
        readonly SmoothScroll _previewScroll = new SmoothScroll();
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
            SlopWidgets.PageCaption(rect,
                "The base every sandbox is built on, and the presets that add to it.");

            var body = SlopWidgets.PageBody(rect);
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                // Global is a short row of three boxes on top; the presets take the rest of
                // the page and get the scroll, so both halves are read top to bottom.
                DoGlobal(new Rect(inner.x, inner.y, inner.width, 200f));
                float presetsY = inner.y + 200f + SlopWidgets.GapL;
                DoPresets(new Rect(inner.x, presetsY, inner.width, inner.yMax - presetsY));
            }

            DoFooter(SlopWidgets.FooterBar(rect));
        }

        // The "Global" section: `[sandbox]`, the base every sandbox is built on. Three
        // boxes in one row, so the three groups read side by side. Whether an agent is
        // sandboxed at all is its project's answer, never this page's.
        void DoGlobal(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Global");
            float top = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;

            // Measured rather than given a height: forty pixels was two lines of the font it
            // was written against and one and a half of a larger one.
            const string what =
                "The base every project builds on. A project's own presets and binds are " +
                "added to these; whether an agent is sandboxed at all is its project's " +
                "answer.";
            GUI.color = SlopWidgets.Dim;
            var note = new Rect(r.x, top, r.width, Text.CalcHeight(what, r.width));
            Widgets.Label(note, what);
            GUI.color = Color.white;
            top = note.yMax + SlopWidgets.GapM;

            float gap = SlopWidgets.GapS;
            float boxW = (r.width - gap * 2f) / 3f;
            float boxH = r.yMax - top;
            _roPaths = SlopWidgets.PathList(new Rect(r.x, top, boxW, boxH),
                "sandbox.ro", "Read-only binds", _roPaths);
            _rwPaths = SlopWidgets.PathList(new Rect(r.x + boxW + gap, top, boxW, boxH),
                "sandbox.rw", "Read-write binds", _rwPaths);
            _passEnv = SlopWidgets.PathList(new Rect(r.x + (boxW + gap) * 2f, top, boxW, boxH),
                "sandbox.env", "Passed env vars", _passEnv);
        }

        // The "Presets" section: the daemon's preset directory on the left, and beside it a
        // preview of whichever preset is selected. The preview is read in the same three
        // groups the Global half edits, so the base can be held against what adds to it.
        void DoPresets(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Presets");
            float top = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            float boxH = r.yMax - top;

            var presets = SessionHub.Instance.Presets;
            if (_selected != null && !presets.Contains(_selected))
                _selected = null;

            // Two columns: the list, and the selected preset's preview. The preview wants the
            // read, so it gets the wider half.
            float previewW = Mathf.Min(380f, r.width * 0.45f);
            float listW = r.width - previewW - SlopWidgets.GapM;
            DoPresetList(new Rect(r.x, top, listW, boxH));
            DoPreview(new Rect(r.x + listW + SlopWidgets.GapM, top, previewW, boxH));
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

            float pitch = SlopWidgets.RowH;
            float h = (presets.Count + groups.Count) * pitch + SlopWidgets.GapS;
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(h, r.height));
            _presetScroll.Begin(r, view);

            if (presets.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(0f, 0f, view.width, pitch),
                    "The daemon has not sent its preset list yet.");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var g in groups)
            {
                SlopWidgets.SectionHeading(new Rect(0f, y, view.width, pitch), g.Key);
                y += pitch;

                foreach (var p in g)
                {
                    var cell = new Rect(SlopWidgets.GapS, y,
                        view.width - SlopWidgets.GapS, pitch);
                    if (p == _selected)
                        Widgets.DrawBoxSolid(cell, SlopWidgets.RowOn);
                    var wasAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    // A way out keeps its colour even when selected: this page is where the
                    // presets are read against each other, and that is the difference worth
                    // seeing without clicking each one.
                    GUI.color = p.IsEscape ? SlopWidgets.Warn
                        : p == _selected ? SlopWidgets.Lead : SlopWidgets.Name;
                    Widgets.Label(cell, p.Name);
                    GUI.color = Color.white;
                    Text.Anchor = wasAnchor;
                    if (Widgets.ButtonInvisible(cell))
                        _selected = p;
                    string cost = p.IsEscape
                        ? $"Way out of the sandbox: {p.Escapes}.\n\n" : "";
                    TooltipHandler.TipRegion(cell,
                        $"{cost}{p.Description}\n\n{string.Join("\n", p.Gives.ToArray())}");
                    y += pitch;
                }
            }
            _presetScroll.End();
        }

        // The selected preset, in the same three groups the Global half edits, each row
        // parted by a rule so "read-only", "read-write" and "env" read as the three things
        // a preset can add rather than as one list.
        void DoPreview(Rect r)
        {
            if (_selected == null)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(r.x, r.y, r.width, SlopWidgets.RowH),
                    "Select a preset to see what it adds.");
                GUI.color = Color.white;
                return;
            }

            var p = _selected;
            var view = new Rect(0f, 0f, r.width - 18f,
                Mathf.Max(Measure(p, r.width - 18f), r.height));
            _previewScroll.Begin(r, view);

            float y = 0f;
            // Above the groups, in the colour the list drew it: what a preset costs is not a
            // fourth kind of bind, it is the sentence to read before any of them.
            if (p.IsEscape)
            {
                GUI.color = SlopWidgets.Warn;
                string cost = $"Way out of the sandbox: {p.Escapes}.";
                float h = Text.CalcHeight(cost, view.width);
                Widgets.Label(new Rect(0f, y, view.width, h), cost);
                GUI.color = Color.white;
                y = Rule(view.width, y + h + SlopWidgets.GapXS);
            }

            y = Row(view, y, "Read-only binds", p.Ro);
            y = Rule(view.width, y);
            y = Row(view, y, "Read-write binds", p.Rw);
            y = Rule(view.width, y);
            y = Row(view, y, "Private, one copy per session", p.Private);
            y = Rule(view.width, y);
            Row(view, y, "Passed env vars", p.Env);

            _previewScroll.End();
        }

        // What one group costs, heading and all. Row and Measure walked the same layout with
        // the same four figures written out twice, which is a pair to get out of step the
        // first time either is touched.
        static float GroupH(List<string> items, float width) =>
            SlopWidgets.RowH + SlopWidgets.GapXS
            + Text.CalcHeight(TextOf(items), width) + SlopWidgets.GapXS;

        static float Row(Rect view, float y, string label, List<string> items)
        {
            SlopWidgets.SectionHeading(new Rect(0f, y, view.width, SlopWidgets.RowH), label);
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            string text = TextOf(items);
            float h = Text.CalcHeight(text, view.width);
            Widgets.Label(new Rect(0f, y, view.width, h), text);
            return y + h + SlopWidgets.GapXS;
        }

        static float Rule(float width, float y)
        {
            Slab.Hairline(new Rect(0f, y, width, 1f), SlopWidgets.Edge);
            return y + SlopWidgets.GapM;
        }

        static float Measure(PresetInfo p, float width) =>
            CostH(p, width)
            + GroupH(p.Ro, width) + SlopWidgets.GapM
            + GroupH(p.Rw, width) + SlopWidgets.GapM
            + GroupH(p.Private, width) + SlopWidgets.GapM
            + GroupH(p.Env, width);

        // The line above the groups, and the rule under it, for a preset that has one.
        static float CostH(PresetInfo p, float width) =>
            p.IsEscape
                ? Text.CalcHeight($"Way out of the sandbox: {p.Escapes}.", width)
                  + SlopWidgets.GapXS + SlopWidgets.GapM
                : 0f;

        static string TextOf(List<string> items) =>
            items.Count > 0 ? string.Join("\n", items.ToArray()) : "(nothing)";

        static string Category(PresetInfo p) =>
            string.IsNullOrEmpty(p.Category) ? "other" : p.Category;

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