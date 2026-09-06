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
            UiWidgets.SectionHeading(l, "Anthropic");
            l.Label("Credentials file");
            _cfg.ClaudeCredentials = UiWidgets.Field(l, "integrations.anthropic.credentials",
                _cfg.ClaudeCredentials);

            l.Gap(UiWidgets.GapL);
            UiWidgets.SectionHeading(l, "OpenRouter");
            l.Label("Key file");
            _cfg.OpenrouterKeyFile = UiWidgets.Field(l, "integrations.openrouter.key",
                _cfg.OpenrouterKeyFile);
            UiWidgets.Note(l, "Blank uses $OPENROUTER_API_KEY. The key stays on the host.");

            l.Gap(UiWidgets.GapL);
            UiWidgets.SectionHeading(l, "OpenAI / Codex");
            l.Label("Credentials file");
            _cfg.OpenaiCredentials = UiWidgets.Field(l, "integrations.openai.credentials",
                _cfg.OpenaiCredentials);

            l.Gap(UiWidgets.GapL);
            UiWidgets.Note(l, "Credential files stay on the host.");
        }
    }
}
