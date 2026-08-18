using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public class IntegrationsPage : ListEditorPage
    {
        protected override string SavedMessage => "integration settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
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
        }
    }
}
