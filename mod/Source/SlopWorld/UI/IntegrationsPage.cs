using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public class IntegrationsPage
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
                    SessionHub.Instance.Config = _cfg;
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect, "Credentials used by the daemon.");

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

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            SlopWidgets.SectionHeading(l, "Anthropic");
            l.Gap(SlopWidgets.GapS);
            l.Label("Credentials file");
            _cfg.ClaudeCredentials = SlopWidgets.Field(l, "integrations.anthropic.credentials",
                _cfg.ClaudeCredentials);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenRouter");
            l.Gap(SlopWidgets.GapS);
            l.Label("Key file");
            _cfg.OpenrouterKeyFile = SlopWidgets.Field(l, "integrations.openrouter.key",
                _cfg.OpenrouterKeyFile);
            SlopWidgets.Note(l, "Blank uses $OPENROUTER_API_KEY. The key stays on the host.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenAI / Codex");
            l.Gap(SlopWidgets.GapS);
            l.Label("Credentials file");
            _cfg.OpenaiCredentials = SlopWidgets.Field(l, "integrations.openai.credentials",
                _cfg.OpenaiCredentials);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.Note(l, "Credential files stay on the host.");

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();
            _scroll.End();
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit as TOML", SlopWidgets.Btn.Ghost)) ConfigWindow.Open();
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
                    SessionHub.Instance.Config = _cfg;
                    SlopOptions.Reread();
                    Messages.Message("SlopWorld: integration settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }
    }
}
