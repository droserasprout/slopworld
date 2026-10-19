using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Title policies and models. See notes/agent-titles.md.
    public class SummariesPage : DaemonConfigPage
    {
        readonly UiAreaResize _promptResize = new UiAreaResize(150f);

        sealed class PolicyTarget
        {
            public string Label;
            public Func<DaemonConfig, string> GetPolicy;
            public Action<DaemonConfig, string> SetPolicy;
            public bool AllowsAlways;
        }

        static readonly PolicyTarget[] Targets =
        {
            new PolicyTarget
            {
                Label = "Codex sessions",
                GetPolicy = config => config.AgentTitles,
                SetPolicy = (config, policy) => config.AgentTitles = policy,
                AllowsAlways = true,
            },
            new PolicyTarget
            {
                Label = "Pi sessions",
                GetPolicy = config => config.PiTitles,
                SetPolicy = (config, policy) => config.PiTitles = policy,
                AllowsAlways = true,
            },
            new PolicyTarget
            {
                Label = "Tasks in sidebar",
                GetPolicy = config => config.TaskSummaries,
                SetPolicy = (config, policy) => config.TaskSummaries = policy,
            },
        };

        static readonly string[] AgentPolicies = { "Never", "Once", "Always" };
        static readonly string[] TaskPolicies = { "Never", "Once" };

        protected override string SavedMessage => "title settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Policies");
            float rowH = UiTheme.FieldH + UiTheme.GapS;
            if (l.ColumnWidth < 430f)
            {
                foreach (var target in Targets)
                {
                    UiSelector.Draw(
                        l,
                        target.Label,
                        PolicyLabel(target.GetPolicy(_cfg)),
                        target.AllowsAlways ? AgentPolicies : TaskPolicies,
                        () => PolicyOptions(target),
                        out _
                    );
                }
            }
            else
            {
                var table = l.GetRect(rowH * (Targets.Length + 1) + UiTheme.GapS);
                UiTable.Draw(table, Targets, rowH, PolicyColumns(), DrawPolicyRow);
            }
            l.Gap(UiTheme.GapM);
            UiLayout.Note(
                l,
                "Choose which submitted prompts or delegated tasks receive an "
                    + "OpenRouter summary. The daemon creates each task summary once."
            );

            l.Gap(UiTheme.GapL);
            l.Label("Minimum prompt length");
            string minPromptChars = MinimumText;
            minPromptChars = UiControls.Field(
                l,
                "usage.summary.minimum",
                minPromptChars,
                defaultValue: _cfg.FactoryDefaults?.TitleMinChars?.ToString(
                    CultureInfo.InvariantCulture
                )
            );
            _configState.SetDraftText(
                "summaries.minimum",
                "daemon.title_min_chars",
                minPromptChars
            );
            UiLayout.Validation(l, MinimumError(minPromptChars));
            UiLayout.Note(
                l,
                "The daemon skips prompts shorter than this many characters. "
                    + "A short prompt does not count as the first title attempt."
            );
            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Summarizer");
            l.Label("Model");
            _cfg.TitleModel = UiControls.Field(
                l,
                "usage.summary.model",
                _cfg.TitleModel,
                defaultValue: _cfg.FactoryDefaults?.TitleModel
            );
            UiLayout.Note(
                l,
                "The daemon sends up to 2,000 characters of each prompt to OpenRouter. "
                    + "Summary requests do not depend on usage polling."
            );

            l.Gap(UiTheme.GapM);
            l.Label("Summarizer prompt");
            _cfg.SummaryPrompt = UiControls.Area(
                l,
                150f,
                "usage.summary.prompt",
                _cfg.SummaryPrompt,
                defaultValue: _cfg.FactoryDefaults?.SummaryPrompt,
                resize: _promptResize
            );
            UiLayout.Note(
                l,
                "When this field has text, the daemon sends it before the submitted prompt "
                    + "for session titles and task summaries. The daemon adds the submitted prompt automatically."
            );
        }

        static List<UiTable.Column> PolicyColumns()
        {
            return new List<UiTable.Column>
            {
                new UiTable.Column("Source", 0f, true, TextAnchor.MiddleLeft, UiTheme.GapS),
                new UiTable.Column("Summarize", 220f, false, TextAnchor.MiddleCenter),
            };
        }

        void DrawPolicyRow(PolicyTarget target, Rect row, Rect[] cells)
        {
            UiText.RowLabel(cells[0], target.Label);
            var button = cells[1].ContractedBy(UiTheme.GapXS, UiTheme.GapXS);
            UiSelector.DrawInline(
                button,
                PolicyLabel(target.GetPolicy(_cfg)),
                () => PolicyOptions(target)
            );
        }

        public static string PolicyLabel(string policy)
        {
            switch (policy)
            {
                case "once":
                    return "Once";
                case "always":
                    return "Always";
                default:
                    return "Never";
            }
        }

        List<FloatMenuOption> PolicyOptions(PolicyTarget target)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Never", () => target.SetPolicy(_cfg, "never")),
                new FloatMenuOption("Once", () => target.SetPolicy(_cfg, "once")),
            };
            if (target.AllowsAlways)
                options.Add(new FloatMenuOption("Always", () => target.SetPolicy(_cfg, "always")));
            return options;
        }

        string MinimumText =>
            _configState.DraftText(
                "summaries.minimum",
                "daemon.title_min_chars",
                _cfg.TitleMinChars.ToString(CultureInfo.InvariantCulture)
            );

        protected override string ValidationError =>
            !_loaded || _cfg == null ? null : MinimumError(MinimumText);

        protected override bool PrepareSave(out string error)
        {
            _configState.ClearQueuedNormalizations();
            string minPromptChars = MinimumText;
            if (!DaemonConfigValidation.TitleMinimum(minPromptChars, out int minimum, out error))
                return false;
            _cfg.TitleMinChars = minimum;
            _configState.SetDraftText(
                "summaries.minimum",
                "daemon.title_min_chars",
                minPromptChars
            );
            _configState.QueueDraftTextNormalization(
                "summaries.minimum",
                "daemon.title_min_chars",
                minimum.ToString(CultureInfo.InvariantCulture)
            );
            error = null;
            return true;
        }

        static string MinimumError(string text)
        {
            DaemonConfigValidation.TitleMinimum(text, out _, out string error);
            return error;
        }
    }
}
