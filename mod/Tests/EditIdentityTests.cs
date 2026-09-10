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
            yield return ("tabbed form geometry stays bounded", Geometry);
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

        static void Geometry()
        {
            foreach (float width in new[] { 0f, 3f, 132f, 700f })
                foreach (float height in new[] { 0f, 2f, 40f, 700f })
                {
                    var form = new UiLayoutRect(17f, 31f, width, height);
                    var layout = TabbedFormLayout.Arrange(form, 132f, 32f, 30f, 8f, 16f);
                    Contained(form, layout.Rail);
                    Contained(form, layout.Body);
                    Contained(form, layout.Footer);
                    AssertEx.True(layout.Rail.YMax <= layout.Footer.Y || layout.Rail.Height == 0f,
                        "rail does not overlap footer");
                    AssertEx.True(layout.Body.YMax <= layout.Footer.Y || layout.Body.Height == 0f,
                        "body does not overlap footer");
                }
        }

        static void Contained(UiLayoutRect parent, UiLayoutRect child)
        {
            AssertEx.True(child.Width >= 0f && child.Height >= 0f &&
                child.X >= parent.X && child.Y >= parent.Y &&
                child.XMax <= parent.XMax && child.YMax <= parent.YMax,
                "tabbed geometry stays bounded");
        }
    }
}
