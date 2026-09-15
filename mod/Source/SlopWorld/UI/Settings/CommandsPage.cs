using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Machine-wide command defaults. Agent and shell choices come from the daemon's live command
    // catalog; the remaining fields are host apps for file actions.
    public class CommandsPage : DaemonConfigPage
    {
        bool _agentCustom, _agentShellCustom, _shellCustom, _pagerCustom, _editorCustom;
        bool _highlighterCustom;

        protected override bool ShowEditButton => true;
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
            SessionHub.Instance.Catalog.LoadPresets();
            _agentCustom = _agentShellCustom = _shellCustom = _pagerCustom = _editorCustom = false;
            _highlighterCustom = false;
        }

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Session defaults");
            ChoiceRow(l, "Agent", "commands.agent",
                _cfg.Agent, Commands(CommandInfo.AgentKind, _cfg.Agent),
                _agentCustom, value => _cfg.Agent = value, value => _agentCustom = value,
                defaultValue: _cfg.FactoryDefaults.Agent);
            ChoiceRow(l, "Agent shell", "commands.agent-shell", _cfg.AgentShell,
                Commands(CommandInfo.ShellKind, _cfg.AgentShell),
                _agentShellCustom, value => _cfg.AgentShell = value,
                value => _agentShellCustom = value, "Custom executable",
                _cfg.FactoryDefaults.AgentShell);
            ChoiceRow(l, "Shell", "commands.shell", _cfg.Shell,
                Commands(CommandInfo.ShellKind, _cfg.Shell),
                _shellCustom, value => _cfg.Shell = value, value => _shellCustom = value,
                defaultValue: _cfg.FactoryDefaults.Shell);

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Default apps");
            ChoiceRow(l, "Pager", "commands.pager", _cfg.Pager, PagerChoices(),
                _pagerCustom, value => _cfg.Pager = value, value => _pagerCustom = value,
                defaultValue: _cfg.FactoryDefaults.Pager);
            ChoiceRow(l, "Editor", "commands.editor", _cfg.Editor, EditorChoices(),
                _editorCustom, value => _cfg.Editor = value, value => _editorCustom = value,
                defaultValue: _cfg.FactoryDefaults.Editor);
            ChoiceRow(l, "Syntax highlighter", "commands.highlighter", _cfg.Highlighter,
                HighlighterChoices(), _highlighterCustom, value => _cfg.Highlighter = value,
                value => _highlighterCustom = value, defaultValue: _cfg.FactoryDefaults.Highlighter);

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Template legend");
            UiLayout.Note(l, "{file} is replaced with a quoted file path; {line} with a search result " +
                "line. Without {file}, file commands receive -- and the path.");
            UiLayout.Note(l, "%s is less's filename placeholder for the syntax highlighter. A blank " +
                "highlighter disables it.");
            UiLayout.Note(l, "Templates are split into arguments without a shell.");
        }

        static List<Choice> Commands(string kind, string current)
        {
            var choices = SessionHub.Instance.Commands
                .Where(c => c.Kind == kind)
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
                       Action<bool> setCustom, string customLabel = "Custom template",
                       string defaultValue = null)
        {
            bool isCustom = custom || !choices.Any(c => c.Value == value);
            string shown = isCustom
                ? "Custom"
                : choices.First(c => c.Value == value).Label;
            var options = choices.Select(c => new SelectorOption(c.Label, () =>
                {
                    setCustom(false);
                    set(c.Value);
                })).ToList();
            options.Add(new SelectorOption("Custom", () => setCustom(true)));
            UiControls.Select(l, label, shown, options, out _);

            if (isCustom)
            {
                bool stacked = l.ColumnWidth < 430f;
                if (stacked) UiLayout.Note(l, customLabel);
                Rect customRow = l.GetRect(UiTheme.FieldH);
                float leftW = stacked ? 0f : Mathf.Min(220f, customRow.width * .42f);
                float gap = stacked ? 0f : UiTheme.GapM;
                float rightX = customRow.x + leftW + gap;
                float rightW = Mathf.Max(0f, customRow.width - leftW - gap);
                GUI.color = UiTheme.Dim;
                if (!stacked) UiText.RowLabel(new Rect(customRow.x, customRow.y, leftW,
                    customRow.height), customLabel);
                GUI.color = Color.white;
                set(UiText.Field(new Rect(rightX, customRow.y, rightW, customRow.height),
                    fieldName + ".custom", value, defaultValue: defaultValue));
            }
        }
    }
}
