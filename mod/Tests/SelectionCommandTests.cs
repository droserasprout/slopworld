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

        static void Disabled()
        {
            var unavailable = new SelectionCommandAvailability(false, false, false, false);
            bool called = false;
            AssertEx.False(SelectionCommandPolicy.TryExecute(
                SelectionCommand.Copy, unavailable, () => called = true),
                "disabled copy is rejected");
            AssertEx.False(called, "disabled copy does not invoke its action");
            AssertEx.False(SelectionCommandPolicy.TryExecute(
                SelectionCommand.Paste, unavailable, () => called = true),
                "disabled paste is rejected");
            AssertEx.False(SelectionCommandPolicy.TryExecute(
                SelectionCommand.Cut, unavailable, () => called = true),
                "disabled cut is rejected");
        }

        static void SelectAll()
        {
            var available = new SelectionCommandAvailability(false, false, true, false);
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
