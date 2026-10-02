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

        protected override string SavedMessage => "title settings saved.";

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
                "OpenRouter summary. The daemon creates each task summary once.");

            l.Gap(UiTheme.GapL);
            l.Label("Minimum prompt length");
            string minPromptChars = _configState.DraftText("summaries.minimum",
                "daemon.title_min_chars", _cfg.TitleMinChars.ToString(CultureInfo.InvariantCulture));
            minPromptChars = UiControls.Field(l, "usage.summary.minimum", minPromptChars,
                defaultValue: _cfg.FactoryDefaults?.TitleMinChars?.ToString(CultureInfo.InvariantCulture));
            _configState.SetDraftText("summaries.minimum", "daemon.title_min_chars",
                minPromptChars);
            UiLayout.Validation(l, MinimumError(minPromptChars));
            UiLayout.Note(l, "The daemon skips prompts shorter than this many characters. " +
                "A short prompt does not count as the first title attempt.");
            l.Gap(UiTheme.GapM);
            l.Label("Model");
            _cfg.TitleModel = UiControls.Field(l, "usage.summary.model", _cfg.TitleModel,
                defaultValue: _cfg.FactoryDefaults?.TitleModel);
            UiLayout.Note(l, "The daemon sends up to 2,000 characters of each prompt to OpenRouter. " +
                "Summary requests do not depend on usage polling.");

            l.Gap(UiTheme.GapM);
            l.Label("Summarizer prompt");
            _cfg.SummaryPrompt = UiControls.Area(l, 150f, "usage.summary.prompt",
                _cfg.SummaryPrompt, defaultValue: _cfg.FactoryDefaults?.SummaryPrompt);
            UiLayout.Note(l, "When this field has text, the daemon sends it before the submitted prompt " +
                "for session titles and task summaries. The daemon adds the submitted prompt automatically.");

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

        protected override string ValidationError => !_loaded || _cfg == null ? null :
            MinimumError(_configState.DraftText("summaries.minimum",
                "daemon.title_min_chars", _cfg.TitleMinChars.ToString(CultureInfo.InvariantCulture)));

        protected override bool PrepareSave(out string error)
        {
            _configState.ClearQueuedNormalizations();
            string minPromptChars = _configState.DraftText("summaries.minimum",
                "daemon.title_min_chars", _cfg.TitleMinChars.ToString(CultureInfo.InvariantCulture));
            if (!DaemonConfigValidation.TitleMinimum(minPromptChars, out int minimum,
                                                      out error)) return false;
            _cfg.TitleMinChars = minimum;
            _configState.SetDraftText("summaries.minimum", "daemon.title_min_chars",
                minPromptChars);
            _configState.QueueDraftTextNormalization("summaries.minimum",
                "daemon.title_min_chars", minimum.ToString(CultureInfo.InvariantCulture));
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
