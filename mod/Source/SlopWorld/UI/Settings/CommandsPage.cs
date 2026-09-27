using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Agent and shell defaults come from the daemon catalog. File editing uses the chosen editor.
    public class CommandsPage : DaemonConfigPage
    {
        bool _agentCustom, _agentShellCustom, _shellCustom, _editorCustom;

        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "command settings saved.";

        protected override void AfterLoad()
        {
            SessionHub.Instance.Catalog.LoadPresets();
            _agentCustom = _agentShellCustom = _shellCustom = _editorCustom = false;
        }

        protected override void DrawFields(Listing_Standard l)
        {
            CommandPicker.Draw(l, "Agent", "commands.agent",
                _cfg.Agent, Commands(CommandInfo.AgentKind, _cfg.Agent),
                _agentCustom, value => _cfg.Agent = value, value => _agentCustom = value,
                defaultValue: _cfg.FactoryDefaults?.Agent);
            CommandPicker.Draw(l, "Agent shell", "commands.agent-shell", _cfg.AgentShell,
                Commands(CommandInfo.ShellKind, _cfg.AgentShell),
                _agentShellCustom, value => _cfg.AgentShell = value,
                value => _agentShellCustom = value, "Custom executable",
                _cfg.FactoryDefaults?.AgentShell);
            CommandPicker.Draw(l, "Shell", "commands.shell", _cfg.Shell,
                Commands(CommandInfo.ShellKind, _cfg.Shell),
                _shellCustom, value => _cfg.Shell = value, value => _shellCustom = value,
                defaultValue: _cfg.FactoryDefaults?.Shell);

            CommandPicker.Draw(l, "Editor", "commands.editor", _cfg.Editor, EditorChoices(),
                _editorCustom, value => _cfg.Editor = value, value => _editorCustom = value,
                defaultValue: _cfg.FactoryDefaults?.Editor);

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Template rules");
            UiLayout.Note(l, "Use {file} to insert a quoted path. Use {line} to insert the line from a " +
                "search result. If a template has no {file}, SlopWorld adds -- before the path.");
            UiLayout.Note(l, "SlopWorld splits each template into arguments. It does not use a shell.");
            l.Gap(UiTheme.GapM);
            if (UiLayout.Button(l, "Code"))
                ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.AppearanceCode));
        }

        static List<CommandChoice> Commands(string kind, string current)
        {
            var choices = SessionHub.Instance.Commands
                .Where(c => c.Kind == kind)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CommandChoice(c.Name, c.Name))
                .ToList();
            if (!string.IsNullOrEmpty(current) &&
                !choices.Any(c => c.Value == current))
                choices.Insert(0, new CommandChoice(current, current));
            return choices;
        }

        static List<CommandChoice> EditorChoices() => new List<CommandChoice>
        {
            new CommandChoice("micro", "micro"),
            new CommandChoice("vim", "vim"),
            new CommandChoice("nano", "nano"),
            new CommandChoice("Neovim", "nvim"),
            new CommandChoice("Emacs client", "emacsclient -c"),
        };

    }
}
