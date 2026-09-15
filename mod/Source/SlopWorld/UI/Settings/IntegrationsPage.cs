using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public class IntegrationsPage : DaemonConfigPage
    {
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "integration settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Anthropic");
            l.Label("Credentials file");
            _cfg.ClaudeCredentials = UiControls.Field(l, "integrations.anthropic.credentials",
                _cfg.ClaudeCredentials, defaultValue: SharedDefaults.DefaultClaudeCredentials);

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "OpenRouter");
            l.Label("Key file");
            _cfg.OpenrouterKeyFile = UiControls.Field(l, "integrations.openrouter.key",
                _cfg.OpenrouterKeyFile, defaultValue: "");
            UiLayout.Note(l, "Blank uses $OPENROUTER_API_KEY. The key stays on the host.");

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "OpenAI / Codex");
            l.Label("Credentials file");
            _cfg.OpenaiCredentials = UiControls.Field(l, "integrations.openai.credentials",
                _cfg.OpenaiCredentials, defaultValue: SharedDefaults.DefaultOpenaiCredentials);

            l.Gap(UiTheme.GapL);
            UiLayout.Note(l, "Credential files stay on the host.");
        }
    }
}
