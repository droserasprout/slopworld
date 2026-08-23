using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Machine-wide command defaults. Preset names stay separate from executable templates:
    // Agent and Shell select daemon command files, while the other fields are host apps the
    // mod starts for file actions.
    public class CommandsPage : ListEditorPage
    {
        bool _agentCustom, _shellCustom, _pagerCustom, _editorCustom;
        bool _highlighterCustom;

        protected override string SavedMessage => "command settings saved.";

        class Choice
        {
            public readonly string Label;
            public readonly string Value;

            public Choice(string label, string value)
            {
                Label = label;
                Value = value;
            }
        }

        protected override void AfterLoad()
        {
            SessionHub.Instance.LoadPresets();
            _agentCustom = _shellCustom = _pagerCustom = _editorCustom = false;
            _highlighterCustom = false;
        }

        protected override void DrawFields(Listing_Standard l)
        {
            SlopWidgets.SectionHeading(l, "Session defaults");
            ChoiceRow(l, "Agent", "commands.agent", _cfg.Agent, Presets(_cfg.Agent),
                _agentCustom, value => _cfg.Agent = value, value => _agentCustom = value);
            ChoiceRow(l, "Shell", "commands.shell", _cfg.Shell, Presets(_cfg.Shell),
                _shellCustom, value => _cfg.Shell = value, value => _shellCustom = value);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Default apps");
            ChoiceRow(l, "Pager", "commands.pager", _cfg.Pager, PagerChoices(),
                _pagerCustom, value => _cfg.Pager = value, value => _pagerCustom = value);
            ChoiceRow(l, "Editor", "commands.editor", _cfg.Editor, EditorChoices(),
                _editorCustom, value => _cfg.Editor = value, value => _editorCustom = value);
            ChoiceRow(l, "Syntax highlighter", "commands.highlighter", _cfg.Highlighter,
                HighlighterChoices(), _highlighterCustom, value => _cfg.Highlighter = value,
                value => _highlighterCustom = value);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Template legend");
            SlopWidgets.Note(l, "{file} is replaced with a quoted file path; {line} with a search result " +
                "line. Without {file}, file commands receive -- and the path.");
            SlopWidgets.Note(l, "%s is less's filename placeholder for the syntax highlighter. A blank " +
                "highlighter disables it.");
            SlopWidgets.Note(l, "Templates are split into arguments without a shell.");
        }

        static List<Choice> Presets(string current)
        {
            var choices = SessionHub.Instance.Commands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new Choice(c.Name, c.Name))
                .ToList();
            if (!string.IsNullOrEmpty(current) &&
                !choices.Any(c => c.Value == current))
                choices.Insert(0, new Choice(current, current));
            return choices;
        }

        static List<Choice> PagerChoices() => new List<Choice>
        {
            new Choice("less", "less"),
            new Choice("more", "more"),
            new Choice("bat (always page)", "bat --paging=always"),
        };

        static List<Choice> EditorChoices() => new List<Choice>
        {
            new Choice("micro", "micro"),
            new Choice("vim", "vim"),
            new Choice("nano", "nano"),
            new Choice("Neovim", "nvim"),
            new Choice("Emacs client", "emacsclient -c"),
        };

        static List<Choice> HighlighterChoices() => new List<Choice>
        {
            new Choice("highlight (256 colors)", "highlight --out-format=xterm256"),
            new Choice("bat (always color)", "bat --color=always --paging=never"),
            new Choice("Off", ""),
        };

        void ChoiceRow(Listing_Standard l, string label, string fieldName, string value,
                       List<Choice> choices, bool custom, Action<string> set,
                       Action<bool> setCustom)
        {
            Rect row = l.GetRect(SlopWidgets.RowH);
            float leftW = Mathf.Min(220f, row.width * .42f);
            float rightX = row.x + leftW + SlopWidgets.GapM;
            float rightW = row.width - leftW - SlopWidgets.GapM;
            bool isCustom = custom || !choices.Any(c => c.Value == value);

            GUI.color = SlopWidgets.Name;
            SlopWidgets.RowLabel(new Rect(row.x, row.y, leftW, row.height), label);
            GUI.color = Color.white;

            string shown = isCustom
                ? "Custom"
                : choices.First(c => c.Value == value).Label;
            if (SlopWidgets.Button(new Rect(rightX, row.y, rightW, row.height), shown,
                    SlopWidgets.Btn.Default))
            {
                var options = choices.Select(c => new FloatMenuOption(c.Label, () =>
                {
                    setCustom(false);
                    set(c.Value);
                })).ToList();
                options.Add(new FloatMenuOption("Custom", () => setCustom(true)));
                Find.WindowStack.Add(new SlopMenu(options));
            }

            if (isCustom)
            {
                Rect customRow = l.GetRect(SlopWidgets.FieldH);
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(customRow.x, customRow.y, leftW,
                    customRow.height), "Custom template");
                GUI.color = Color.white;
                set(SlopWidgets.Field(new Rect(rightX, customRow.y, rightW, customRow.height),
                    fieldName + ".custom", value));
            }
        }
    }
}
