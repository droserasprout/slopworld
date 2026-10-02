using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SelectionCommandTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("disabled commands cannot execute", Disabled);
            yield return ("select all keeps caller side effects", SelectAll);
        }

        public static void EachCommandUsesItsOwnAvailabilityAndAction()
        {
            var commands = new[] { SelectionCommand.Copy, SelectionCommand.Paste, SelectionCommand.SelectAll, SelectionCommand.Cut };
            for (int enabled = 0; enabled < commands.Length; enabled++)
            {
                var available = new SelectionCommandAvailability(canCopy: enabled == 0, canPaste: enabled == 1, canSelectAll: enabled == 2, canCut: enabled == 3);
                int called = 0;
                foreach (var command in commands)
                {
                    int before = called;
                    bool expected = command == commands[enabled];
                    AssertEx.Equal(expected, SelectionCommandPolicy.TryExecute(command, available, () => called++), "availability for " + command);
                    AssertEx.Equal(before + (expected ? 1 : 0), called, "action count after " + command);
                }
            }
        }

        static void Disabled()
        {
            var unavailable = new SelectionCommandAvailability(canCopy: false, canPaste: false, canSelectAll: false, canCut: false);
            int called = 0;
            foreach (SelectionCommand command in Enum.GetValues(typeof(SelectionCommand)))
            {
                AssertEx.False(SelectionCommandPolicy.TryExecute(command, unavailable, () => called++), "disabled " + command);
                AssertEx.Equal(0, called, "rejected " + command + " never calls the action");
            }
        }

        static void SelectAll()
        {
            var available = new SelectionCommandAvailability(canCopy: false, canPaste: false, canSelectAll: true, canCut: false);
            int first = 0;
            int second = 0;
            AssertEx.True(SelectionCommandPolicy.TryExecute(
                SelectionCommand.SelectAll, available, () => first++),
                "first caller can select all");
            AssertEx.True(SelectionCommandPolicy.TryExecute(
                SelectionCommand.SelectAll, available, () => second += 2),
                "second caller can select all");
            AssertEx.Equal(1, first, "first select-all side effect remains local");
            AssertEx.Equal(2, second, "second select-all side effect remains local");
        }
    }
}
