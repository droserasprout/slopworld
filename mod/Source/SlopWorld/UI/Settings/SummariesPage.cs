using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Title policies and models; see notes/agent-titles.md.
    public class SummariesPage : DaemonConfigPage
    {
        static readonly (string Label, Func<DaemonConfig, string> GetPolicy,
            Action<DaemonConfig, string> SetPolicy, bool AllowsAlways)[] Targets =
        {
            ("Codex sessions", config => config.AgentTitles,
                (config, policy) => config.AgentTitles = policy, true),
            ("Pi sessions", config => config.PiTitles,
                (config, policy) => config.PiTitles = policy, true),
            ("Tasks in sidebar", config => config.TaskSummaries,
                (config, policy) => config.TaskSummaries = policy, false),
        };

        string _minPromptChars;

        protected override string SavedMessage => "title settings saved.";

        protected override void AfterLoad()
        {
            _minPromptChars = _cfg.TitleMinChars.ToString();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            float rowH = UiTheme.FieldH + UiTheme.GapS;
            if (l.ColumnWidth < 430f)
            {
                foreach (var target in Targets)
                {
                    UiLayout.Note(l, target.Label);
                    if (UiLayout.Button(l, "Summarize: " + PolicyLabel(target.GetPolicy(_cfg))))
                        OpenPolicyMenu(target);
                }
            }
            else
            {
                var table = l.GetRect(rowH * (Targets.Length + 1) + UiTheme.GapS);
                UiTable.Draw(table, Targets, rowH, PolicyColumns(), DrawPolicyRow);
            }
            l.Gap(UiTheme.GapM);
            UiLayout.Note(l, "Choose which submitted prompts or delegated tasks receive an " +
                "OpenRouter summary. Task summaries are generated once per task.");

            l.Gap(UiTheme.GapL);
            l.Label("Minimum prompt length");
            _minPromptChars = UiControls.Field(l, "usage.summary.minimum", _minPromptChars,
                defaultValue: WireContract.DefaultTitleMinChars.ToString());
            UiLayout.Note(l, "Prompts shorter than this many characters are not summarized. " +
                "Short prompts do not use up a first-prompt title attempt.");
            l.Gap(UiTheme.GapM);
            l.Label("Model");
            _cfg.TitleModel = UiControls.Field(l, "usage.summary.model", _cfg.TitleModel,
                defaultValue: WireContract.DefaultTitleModel);
            UiLayout.Note(l, "Up to 2,000 characters of each prompt go to OpenRouter. " +
                "Summaries do not depend on credit polling.");

            l.Gap(UiTheme.GapM);
            l.Label("Summarizer prompt");
            _cfg.SummaryPrompt = UiControls.Area(l, 150f, "usage.summary.prompt",
                _cfg.SummaryPrompt, defaultValue: DaemonConfig.DefaultSummaryPrompt);
            UiLayout.Note(l, "This instruction is sent before the submitted prompt for both " +
                "session titles and task summaries. The submitted prompt is appended automatically.");

        }

        static List<UiTable.Column> PolicyColumns()
        {
            return new List<UiTable.Column>
            {
                new UiTable.Column("Source", 0f, true, TextAnchor.MiddleLeft, UiTheme.GapS),
                new UiTable.Column("Summarize", 220f, false, TextAnchor.MiddleCenter),
            };
        }

        void DrawPolicyRow((string Label, Func<DaemonConfig, string> GetPolicy,
            Action<DaemonConfig, string> SetPolicy, bool AllowsAlways) target,
            Rect row, Rect[] cells)
        {
            UiText.RowLabel(cells[0], target.Label);
            var button = cells[1].ContractedBy(UiTheme.GapXS, UiTheme.GapXS);
            if (UiButtons.Button(button, PolicyLabel(target.GetPolicy(_cfg))))
                OpenPolicyMenu(target);
        }

        public static string PolicyLabel(string policy)
        {
            switch (policy)
            {
                case "once": return "Once";
                case "always": return "Always";
                default: return "Never";
            }
        }

        void OpenPolicyMenu((string Label, Func<DaemonConfig, string> GetPolicy,
            Action<DaemonConfig, string> SetPolicy, bool AllowsAlways) target)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Never", () => SetPolicy(target, "never")),
                new FloatMenuOption("Once", () => SetPolicy(target, "once")),
            };
            if (target.AllowsAlways)
                options.Add(new FloatMenuOption("Always", () => SetPolicy(target, "always")));
            Find.WindowStack.Add(new UiMenu(options));
        }

        void SetPolicy((string Label, Func<DaemonConfig, string> GetPolicy,
            Action<DaemonConfig, string> SetPolicy, bool AllowsAlways) target, string policy)
        {
            target.SetPolicy(_cfg, policy);
        }

        protected override void BeforeSave()
        {
            if (int.TryParse(_minPromptChars, out int minimum))
                _cfg.TitleMinChars = Mathf.Clamp(minimum, 0, 2000);
        }
    }
}
