using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class BrowseDialogStateTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("failed browsing preserves navigation and supports recovery", Recovers);
            yield return ("initial browse failure leaves selection unavailable", InitialFailure);
            yield return ("stale browse replies cannot replace newer navigation", StaleReplies);
            yield return ("closing browsing invalidates pending replies", Close);
        }

        static Wire.BrowseResult Listing(string path)
        {
            var result = new Wire.BrowseResult
            {
                Path = path,
                Parent = "/",
                Truncated = true,
            };
            result.Dirs.Add("child");
            return result;
        }

        static void Recovers()
        {
            var state = new BrowseDialogState();
            state.Accept(state.Begin(), Listing("/work"));
            long child = state.Begin();
            AssertEx.True(state.Pending, "navigation blocks selection while pending");
            state.Fail(child, "permission denied");
            AssertEx.False(state.Pending, "failure permits recovery");
            AssertEx.Equal("/work", state.Path, "last successful path survives");
            AssertEx.Equal("/", state.Parent, "parent remains navigable");
            AssertEx.Equal("child", state.Directories[0], "children remain navigable");
            AssertEx.True(state.Truncated, "retained listing retains truncation");
            AssertEx.Equal("permission denied", state.Error, "error remains visible");
            long retry = state.Begin();
            AssertEx.Equal<string>(null, state.Error, "retry clears old error");
            state.Accept(retry, new Wire.BrowseResult { Path = "/" });
            AssertEx.Equal("/", state.Path, "recovery commits new path");
            AssertEx.Equal<string>(null, state.Parent, "root has no parent");
            AssertEx.Equal(0, state.Directories.Length, "new listing replaces old children");
            AssertEx.False(state.Truncated, "new listing replaces truncation");
        }

        static void InitialFailure()
        {
            var state = new BrowseDialogState();
            state.Fail(state.Begin(), "offline");
            AssertEx.Equal<string>(null, state.Path, "cannot select a path before success");
            state.Accept(state.Begin(), null);
            AssertEx.False(state.Pending, "missing response releases request");
            AssertEx.Equal<string>(null, state.Path, "missing response cannot create selection");
            AssertEx.True(state.Error != null, "missing response reports error");
            state.Accept(state.Begin(), Listing("/work"));
            state.Accept(state.Begin(), null);
            AssertEx.Equal("/work", state.Path, "missing response retains committed navigation");
        }

        static void StaleReplies()
        {
            var state = new BrowseDialogState();
            long first = state.Begin();
            long second = state.Begin();
            AssertEx.False(state.Accept(first, Listing("/old")), "stale success rejected");
            AssertEx.False(state.Fail(first, "old failure"), "stale error rejected");
            AssertEx.True(state.Pending, "current request remains pending");
            state.Accept(second, Listing("/new"));
            state.Accept(first, Listing("/old"));
            state.Fail(first, "old failure");
            AssertEx.Equal("/new", state.Path, "late reply cannot overwrite navigation");
            AssertEx.Equal<string>(null, state.Error, "late error cannot overwrite success");
        }

        static void Close()
        {
            var state = new BrowseDialogState();
            long request = state.Begin();
            state.Close();
            state.Close();
            AssertEx.False(state.Accept(request, Listing("/late")), "closed success rejected");
            AssertEx.False(state.Fail(request, "late error"), "closed failure rejected");
            AssertEx.False(state.Pending, "repeated cleanup leaves no pending request");
            AssertEx.Equal<string>(null, state.Path, "closed reply cannot commit navigation");
        }
    }
}
