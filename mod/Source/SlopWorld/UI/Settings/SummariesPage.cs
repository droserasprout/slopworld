using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Title policies and models; see notes/agent-titles.md.
    public class SummariesPage : DaemonConfigPage
    {
        enum SummaryTarget
        {
            Codex,
            Pi,
            Tasks,
        }

        static readonly SummaryTarget[] Targets =
        {
            SummaryTarget.Codex,
            SummaryTarget.Pi,
            SummaryTarget.Tasks,
        };

        string _minPromptChars;

        protected override string SavedMessage => "title settings saved.";

        protected override void AfterLoad()
        {
            _minPromptChars = _cfg.TitleMinChars.ToString();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            float rowH = UiWidgets.FieldH + UiWidgets.GapS;
            var table = l.GetRect(rowH * (Targets.Length + 1) + UiWidgets.GapS);
            UiTable.Draw(table, Targets, rowH, PolicyColumns(), DrawPolicyRow);
            l.Gap(UiWidgets.GapM);
            UiWidgets.Note(l, "Choose which submitted prompts or delegated tasks receive an " +
                "OpenRouter summary. Task summaries are generated once per task.");

            l.Gap(UiWidgets.GapL);
            l.Label("Minimum prompt length");
            _minPromptChars = UiWidgets.Field(l, "usage.summary.minimum", _minPromptChars);
            UiWidgets.Note(l, "Prompts shorter than this many characters are not summarized. " +
                "Short prompts do not use up a first-prompt title attempt.");
            l.Gap(UiWidgets.GapM);
            l.Label("Model");
            _cfg.TitleModel = UiWidgets.Field(l, "usage.summary.model", _cfg.TitleModel);
            UiWidgets.Note(l, "Up to 2,000 characters of each prompt go to OpenRouter. " +
                "Summaries do not depend on credit polling.");

        }

        static List<UiTable.Column> PolicyColumns()
        {
            return new List<UiTable.Column>
            {
                new UiTable.Column("Source", 0f, true, TextAnchor.MiddleLeft, UiWidgets.GapS),
                new UiTable.Column("Summarize", 220f, false, TextAnchor.MiddleCenter),
            };
        }

        void DrawPolicyRow(SummaryTarget target, Rect row, Rect[] cells)
        {
            UiWidgets.RowLabel(cells[0], TargetLabel(target));
            var button = cells[1].ContractedBy(UiWidgets.GapXS, UiWidgets.GapXS);
            if (UiWidgets.Button(button, PolicyLabel(Policy(target))))
                OpenPolicyMenu(target);
        }

        static string TargetLabel(SummaryTarget target)
        {
            switch (target)
            {
                case SummaryTarget.Codex: return "Codex sessions";
                case SummaryTarget.Pi: return "Pi sessions";
                default: return "Tasks in sidebar";
            }
        }

        string Policy(SummaryTarget target)
        {
            switch (target)
            {
                case SummaryTarget.Codex: return _cfg.AgentTitles;
                case SummaryTarget.Pi: return _cfg.PiTitles;
                default: return _cfg.TaskSummaries;
            }
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

        void OpenPolicyMenu(SummaryTarget target)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Never", () => SetPolicy(target, "never")),
                new FloatMenuOption("Once", () => SetPolicy(target, "once")),
            };
            if (target != SummaryTarget.Tasks)
                options.Add(new FloatMenuOption("Always", () => SetPolicy(target, "always")));
            Find.WindowStack.Add(new UiMenu(options));
        }

        void SetPolicy(SummaryTarget target, string policy)
        {
            switch (target)
            {
                case SummaryTarget.Codex: _cfg.AgentTitles = policy; break;
                case SummaryTarget.Pi: _cfg.PiTitles = policy; break;
                default: _cfg.TaskSummaries = policy; break;
            }
        }

        protected override void BeforeSave()
        {
            if (int.TryParse(_minPromptChars, out int minimum))
                _cfg.TitleMinChars = Mathf.Clamp(minimum, 0, 2000);
        }
    }
}
