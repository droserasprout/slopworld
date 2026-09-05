using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Title policies and models; see notes/agent-titles.md.
    public class SummariesPage : DaemonConfigPage
    {
        string _minPromptChars;

        protected override string SavedMessage => "title settings saved.";

        protected override void AfterLoad()
        {
            _minPromptChars = _cfg.TitleMinChars.ToString();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            SlopWidgets.SectionHeading(l, "Codex");
            if (SlopWidgets.Button(l,
                    "Name sessions: " + PolicyLabel(_cfg.AgentTitles)))
                OpenPolicyMenu(false);
            SlopWidgets.Note(l, "Names a Codex session from its submitted prompt.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Pi");
            if (SlopWidgets.Button(l,
                    "Name sessions: " + PolicyLabel(_cfg.PiTitles)))
                OpenPolicyMenu(true);
            SlopWidgets.Note(l, "Pi defaults to every prompt. The daemon applies this setting before input " +
                "reaches Pi, so it takes effect in the current session.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Host");
            _cfg.HostTitles = SlopWidgets.Checkbox(l, "Summarize host commands", _cfg.HostTitles,
                "Names commands submitted in host terminals. Off leaves host terminal titles to " +
                "the terminal application.");
            SlopWidgets.Note(l, "Host terminals summarize each submitted command, independently of " +
                "the Codex and Pi policies.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "All summaries");
            l.Label("Minimum prompt length");
            _minPromptChars = SlopWidgets.Field(l, "usage.summary.minimum", _minPromptChars);
            SlopWidgets.Note(l, "Prompts and host commands shorter than this many characters " +
                "are not summarized. Short prompts do not use up a first-prompt title attempt.");
            l.Gap(SlopWidgets.GapM);
            l.Label("Model");
            _cfg.TitleModel = SlopWidgets.Field(l, "usage.summary.model", _cfg.TitleModel);
            SlopWidgets.Note(l, "Up to 2,000 characters of each prompt or command go to OpenRouter. " +
                "Summaries do not depend on credit polling.");

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

        protected override void BeforeSave()
        {
            if (int.TryParse(_minPromptChars, out int minimum))
                _cfg.TitleMinChars = Mathf.Clamp(minimum, 0, 2000);
        }
    }
}
