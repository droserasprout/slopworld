using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The Integrations tab's own page: what this install is set to talk to, and what the
    // daemon is reporting from it now. Text only - every switch behind these lines is on
    // the Usage and Summaries tabs under this row.
    public class IntegrationsPage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _linesH;

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
            SlopWidgets.PageCaption(rect, "Who this install talks to, and what it says.");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
            DoLines(body.ContractedBy(SlopWidgets.GapM));
        }

        void DoLines(Rect r)
        {
            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(r, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
                return;
            }

            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_linesH, r.height));
            _scroll.Begin(r, view);

            // Begun far taller than it is, for the reason every form on these pages is:
            // a line that would cross the bottom starts a second column otherwise.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            SlopWidgets.SectionHeading(l, "Anthropic");
            if (_cfg.Usage)
            {
                l.Label($"Subscription windows, polled every {_cfg.UsagePollSecs}s.");
                Note(l, $"Read from Claude Code's OAuth token at {_cfg.ClaudeCredentials}.");
            }
            else
            {
                Off(l, "Off. The daemon never opens Claude Code's token file.");
            }

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenRouter");
            if (_cfg.Openrouter) l.Label("Credit balance, bought less spent.");
            else Off(l, "Credit polling is off.");
            Note(l, string.IsNullOrEmpty((_cfg.OpenrouterKeyFile ?? "").Trim())
                ? "The key comes from the daemon's own $OPENROUTER_API_KEY."
                : $"The key comes from {_cfg.OpenrouterKeyFile}.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "OpenAI / Codex");
            if (_cfg.Openai)
            {
                l.Label("Primary and secondary usage windows.");
                Note(l, $"Read from Codex's ChatGPT login at {_cfg.OpenaiCredentials}.");
            }
            else
            {
                Off(l, "Off. The daemon never opens Codex's login file.");
            }

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Prompt summaries");
            l.Label($"Codex: {SummariesPage.PolicyLabel(_cfg.AgentTitles)}.");
            l.Label($"Pi: {SummariesPage.PolicyLabel(_cfg.PiTitles)}.");
            Note(l, "A summary names the session it came from. The request goes to " +
                    "OpenRouter whether or not the credit balance is polled.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Right now");
            DoLive(l);

            l.Gap(SlopWidgets.GapL);
            Note(l, "Usage and Summaries, under this row, are where all of it is set.");

            _linesH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();
        }

        // The hub's last snapshot rather than the config: what a seller is set to answer and
        // what it last answered are the two different things this page is for.
        void DoLive(Listing_Standard l)
        {
            var usage = SessionHub.Instance.Usage;

            if (!usage.Any)
            {
                Off(l, "No quota reported yet.");
            }
            else
            {
                l.Label(usage.Windows.Count == 1
                    ? "One quota row on the top bar."
                    : $"{usage.Windows.Count} quota rows on the top bar.");
                if (!string.IsNullOrEmpty(usage.Plan))
                    Note(l, $"Anthropic calls this plan {usage.Plan}.");
            }

            if (!usage.Ok && !string.IsNullOrEmpty(usage.Error))
            {
                GUI.color = SlopWidgets.Warn;
                l.Label($"The last poll failed: {usage.Error}");
                GUI.color = Color.white;
            }
        }

        // A second line about the line above it.
        static void Note(Listing_Standard l, string text)
        {
            GUI.color = SlopWidgets.Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        // A whole integration that is switched off, which is a state and not a footnote.
        static void Off(Listing_Standard l, string text)
        {
            GUI.color = SlopWidgets.Off;
            l.Label(text);
            GUI.color = Color.white;
        }
    }
}
