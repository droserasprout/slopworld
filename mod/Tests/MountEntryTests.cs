using System;
using System.Linq;
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
            AssertEx.Equal("None", MountPresentation.ModeLabel(MountMode.None), "none label");
            AssertEx.Equal("Read-only", MountPresentation.ModeLabel(MountMode.Ro), "read-only label");
            AssertEx.Equal("Read-write", MountPresentation.ModeLabel(MountMode.Rw), "read-write label");
        }

        static void RoundTripsMountLists()
        {
            var mounts = MountEntry.ListFromWire(new[] {
                new Wire.Mount { From = "docs\"and\"tests", To = "/mnt/docs", Mode = "ro" },
                new Wire.Mount { From = "scratch" },
            });

            AssertEx.Equal(2, mounts.Count, "mount count");
            AssertEx.Equal("docs\"and\"tests", mounts[0].From, "quoted project name");
            AssertEx.Equal("/mnt/docs", mounts[0].To, "destination path");
            AssertEx.Equal(MountMode.Ro, mounts[0].Mode, "parsed read-only mount");
            AssertEx.Equal(MountMode.Rw, mounts[1].Mode, "missing mode defaults writable");

            var wire = mounts.Select(m => m.ToWire()).ToList();
            var roundTrip = MountEntry.ListFromWire(wire);
            AssertEx.Equal(wire[0], roundTrip[0].ToWire(), "mount binary model round trip");
            AssertEx.Equal(0, MountEntry.ListFromWire(new Wire.Mount[0]).Count, "empty mount list");
        }
    }
}
