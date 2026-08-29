using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class MountEntryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("parses and labels mount modes", ParsesAndLabelsModes);
            yield return ("round trips mount lists", RoundTripsMountLists);
        }

        static void ParsesAndLabelsModes()
        {
            AssertEx.Equal(MountMode.Ro, MountEntry.ParseMode(" RO "), "read-only mode");
            AssertEx.Equal(MountMode.Rw, MountEntry.ParseMode("Rw"), "read-write mode");
            AssertEx.Equal(MountMode.Rw, MountEntry.ParseMode("future"), "unknown mode fallback");
            AssertEx.Equal(MountMode.Rw, MountEntry.ParseMode(null), "missing mode fallback");

            AssertEx.Equal("ro", MountEntry.ModeName(MountMode.Ro), "read-only wire name");
            AssertEx.Equal("rw", MountEntry.ModeName(MountMode.Rw), "read-write wire name");
            AssertEx.Equal("rw", MountEntry.ModeName(MountMode.None), "none wire fallback");
            AssertEx.Equal("None", MountEntry.ModeLabel(MountMode.None), "none label");
            AssertEx.Equal("Read-only", MountEntry.ModeLabel(MountMode.Ro), "read-only label");
            AssertEx.Equal("Read-write", MountEntry.ModeLabel(MountMode.Rw), "read-write label");
        }

        static void RoundTripsMountLists()
        {
            var mounts = MountEntry.ListFromJson(JVal.Parse(
                "[{\"project\":\"docs\\\"and\\\"tests\",\"mode\":\"ro\"}," +
                "{\"project\":\"scratch\"}]"));

            AssertEx.Equal(2, mounts.Count, "mount count");
            AssertEx.Equal("docs\"and\"tests", mounts[0].Project, "quoted project name");
            AssertEx.Equal(MountMode.Ro, mounts[0].Mode, "parsed read-only mount");
            AssertEx.Equal(MountMode.Rw, mounts[1].Mode, "missing mode defaults writable");

            string wire = MountEntry.ListToJson(mounts);
            var roundTrip = MountEntry.ListFromJson(JVal.Parse(wire));
            AssertEx.Equal(wire, MountEntry.ListToJson(roundTrip), "mount list JSON round trip");
            AssertEx.Equal("[]", MountEntry.ListToJson(new List<MountEntry>()),
                           "empty mount list JSON");
        }
    }
}
