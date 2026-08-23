using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class RelativePathScanTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("finds project-relative paths", FindsPaths);
            yield return ("trims source locations and wrappers", TrimsLocations);
            yield return ("rejects ambiguous and absolute text", RejectsNonPaths);
        }

        static void FindsPaths()
        {
            AssertEx.Equal("docslop/index.md", RelativePathScan.At("see docslop/index.md now", 8),
                "a path with a directory is found");
            AssertEx.Equal("../src/main.rs", RelativePathScan.At("../src/main.rs", 5),
                "an explicit parent-relative path is found");
        }

        static void TrimsLocations()
        {
            AssertEx.Equal("mod/Foo.cs", RelativePathScan.At("(mod/Foo.cs:12:3)", 6),
                "compiler line and column suffixes are removed");
            AssertEx.Equal("./README", RelativePathScan.At("`./README`,", 4),
                "quotes and punctuation are removed");
        }

        static void RejectsNonPaths()
        {
            AssertEx.True(RelativePathScan.At("README", 2) == null, "a bare word is ambiguous");
            AssertEx.True(RelativePathScan.At("/etc/passwd", 5) == null, "absolute paths are rejected");
            AssertEx.True(RelativePathScan.At("https://example.test/a", 10) == null, "URLs stay URLs");
        }
    }
}
