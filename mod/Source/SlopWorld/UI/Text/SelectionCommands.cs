using System;
using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Shared presentation for text-selection menus. Callers choose which commands exist and
    // keep their own clipboard, range and SelectAll side effects.
    public static class SelectionCommands
    {
        public static void Add(
            List<FloatMenuOption> options, SelectionCommandAvailability availability,
            Action copy, Action paste, Action selectAll, Action cut = null)
        {
            if (cut != null) AddCut(options, availability, cut);
            if (copy != null) AddCopy(options, availability, copy);
            if (paste != null) AddPaste(options, availability, paste);
            if (selectAll != null) AddSelectAll(options, availability, selectAll);
        }

        public static void AddCopy(List<FloatMenuOption> options,
                                   SelectionCommandAvailability availability, Action copy)
        {
            Add(options, "Copy", SelectionCommand.Copy, availability, copy);
        }

        public static void AddPaste(List<FloatMenuOption> options,
                                    SelectionCommandAvailability availability, Action paste)
        {
            Add(options, "Paste", SelectionCommand.Paste, availability, paste);
        }

        public static void AddSelectAll(List<FloatMenuOption> options,
                                        SelectionCommandAvailability availability,
                                        Action selectAll)
        {
            Add(options, "Select all", SelectionCommand.SelectAll, availability, selectAll);
        }

        static void AddCut(List<FloatMenuOption> options,
                           SelectionCommandAvailability availability, Action cut)
        {
            Add(options, "Cut", SelectionCommand.Cut, availability, cut);
        }

        static void Add(List<FloatMenuOption> options, string label, SelectionCommand command,
                        SelectionCommandAvailability availability, Action action)
        {
            if (action == null) return;
            var option = new FloatMenuOption(label, () =>
                SelectionCommandPolicy.TryExecute(command, availability, action));
            option.Disabled = !SelectionCommandPolicy.IsEnabled(command, availability);
            options.Add(option);
        }
    }
}
