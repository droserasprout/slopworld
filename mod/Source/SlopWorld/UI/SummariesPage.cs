using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Title policies and models; see docslop/agent-titles.md.
    public class SummariesPage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _fieldsH;

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect,
                "What each agent's sessions are named after, and who names them.");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
            DoFields(body.ContractedBy(SlopWidgets.GapM));

            DoFooter(SlopWidgets.FooterBar(rect));
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

            SlopWidgets.SectionHeading(l, "Codex");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    "Name sessions: " + PolicyLabel(_cfg.AgentTitles)))
                OpenPolicyMenu(false);
            Note(l, "Names a Codex session from its submitted prompt.");

            if (_cfg.AgentTitles != "never")
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Title model");
                _cfg.TitleModel = SlopWidgets.Field(l, "usage.title.model", _cfg.TitleModel);
            }

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Pi");
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    "Name sessions: " + PolicyLabel(_cfg.PiTitles)))
                OpenPolicyMenu(true);
            Note(l, "Pi defaults to every prompt. The daemon applies this setting before input " +
                    "reaches Pi, so it takes effect in the current session.");

            if (_cfg.PiTitles != "never")
            {
                l.Gap(SlopWidgets.GapS);
                l.Label("Pi title model");
                _cfg.PiTitleModel = SlopWidgets.Field(l, "usage.pi.title.model",
                    _cfg.PiTitleModel);
            }

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Both");
            Note(l, "Up to 2,000 characters of each prompt go to OpenRouter. Summaries do not " +
                    "depend on credit polling.");

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

        public static string PolicyLabel(string policy)
        {
            switch (policy)
            {
                case "once": return "First prompt in each conversation";
                case "always": return "Every prompt";
                default: return "Off";
            }
        }

        void OpenPolicyMenu(bool pi)
        {
            Find.WindowStack.Add(new SlopMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Off", () => SetPolicy(pi, "never")),
                new FloatMenuOption("First prompt in each conversation", () =>
                    SetPolicy(pi, "once")),
                new FloatMenuOption("Every prompt", () => SetPolicy(pi, "always")),
            }));
        }

        void SetPolicy(bool pi, string policy)
        {
            if (pi) _cfg.PiTitles = policy;
            else _cfg.AgentTitles = policy;
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);

            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

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

            SlopClient.Put("/api/config/patch", _cfg.ToPatchJson(),
                _ =>
                {
                    _error = null;
                    SlopOptions.Reread();
                    Messages.Message("SlopWorld: title settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }
    }
}
