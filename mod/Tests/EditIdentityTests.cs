using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class EditIdentityTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("new/edit/copy identity titles", Titles);
            yield return ("copy identity uses NameTools collisions", CopyName);
        }

        static void Titles()
        {
            var created = EditIdentity.ForNew();
            AssertEx.True(created.IsNew, "new identity is new");
            AssertEx.Equal("New agent", created.Title("agent"), "new title");

            var edited = EditIdentity.ForEdit("old-name");
            AssertEx.False(edited.IsNew, "edit identity is not new");
            AssertEx.Equal("old-name", edited.OriginalName, "edit address");
            AssertEx.Equal("Edit 'old-name'", edited.Title("agent"), "edit title");

            var copied = EditIdentity.ForCopy("old-name");
            AssertEx.True(copied.IsNew, "copy saves as new");
            AssertEx.Equal("old-name", copied.CopySource, "copy source");
            AssertEx.Equal("Copy of 'old-name'", copied.Title("agent"), "copy title");
        }

        static void CopyName()
        {
            var identity = EditIdentity.ForCopy("agent-2");
            AssertEx.Equal("agent-4", identity.CopyName(
                new[] { "agent-2", "agent-3" }, "agent"),
                "copy name skips occupied suffixes");
        }

    }
}
