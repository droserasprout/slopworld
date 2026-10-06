using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class StorageLoadStateTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("storage publishes rows before sizes and retains them after scan failure", ScanFailure);
            yield return ("storage refresh rejects old inventory and size callbacks", StaleCallbacks);
            yield return ("storage closure invalidates both load stages", Closure);
        }

        static void ScanFailure()
        {
            var state = new StorageLoadState<string>();
            Action<string> inventory = null;
            Action<string> measured = null;
            Action<string> failed = null;
            state.Load((sizes, ok, fail) =>
            {
                if (!sizes) inventory = ok;
                else { measured = ok; failed = fail; }
            });
            AssertEx.Equal(null, measured, "size scan waits for quick inventory");
            inventory("rows");
            AssertEx.Equal("rows", state.Value, "rows render while scan is pending");
            AssertEx.True(state.Loading, "scan remains pending");
            AssertEx.False(state.SizesAvailable, "zero placeholders are not actual sizes");
            failed("scan failed");
            AssertEx.Equal("rows", state.Value, "scan failure retains usable paths");
            AssertEx.Equal("scan failed", state.Error, "failure is visible");
            AssertEx.False(state.Loading, "failure allows retry");
            AssertEx.False(state.SizesAvailable, "failed scan does not imply zero usage");
            state.Load((sizes, ok, fail) => ok(sizes ? "measured" : "new rows"));
            AssertEx.Equal("measured", state.Value, "retry publishes sizes");
            AssertEx.True(state.SizesAvailable, "completed scan supplies actual sizes");
            AssertEx.Equal(null, state.Error, "successful retry clears error");
        }

        static void StaleCallbacks()
        {
            var state = new StorageLoadState<string>();
            Action<string> oldInventory = null;
            Action<string> oldMeasured = null;
            Action<string> oldFailure = null;
            int scans = 0;
            state.Load((sizes, ok, fail) =>
            {
                if (!sizes) oldInventory = ok;
                else { scans++; oldMeasured = ok; oldFailure = fail; }
            });
            oldInventory("old rows");
            state.Load((sizes, ok, fail) => ok(sizes ? "new measured" : "new rows"));
            oldInventory("late inventory");
            oldMeasured("late sizes");
            oldFailure("late failure");
            AssertEx.Equal(1, scans, "stale inventory cannot launch another scan");
            AssertEx.Equal("new measured", state.Value, "new inventory wins");
            AssertEx.True(state.SizesAvailable, "new sizes remain available");
            AssertEx.Equal(null, state.Error, "old failure cannot overwrite success");
        }

        static void Closure()
        {
            foreach (bool closeDuringScan in new[] { false, true })
            {
                var state = new StorageLoadState<string>();
                Action<string> complete = null;
                Action<string> failure = null;
                state.Load((sizes, ok, fail) => { complete = ok; failure = fail; });
                if (closeDuringScan) complete("rows");
                state.Invalidate();
                complete("late");
                failure("late error");
                AssertEx.Equal(closeDuringScan ? "rows" : null, state.Value,
                    "closed page ignores pending callbacks");
                AssertEx.False(state.Loading, "closure stops pending state");
                AssertEx.Equal(null, state.Error, "closure ignores late failures");
            }
        }
    }
}
