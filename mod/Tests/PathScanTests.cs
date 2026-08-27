using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class PathScanTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("finds project-relative paths", FindsPaths);
            yield return ("finds absolute paths", FindsAbsolute);
            yield return ("trims source locations and wrappers", TrimsLocations);
            yield return ("finds root files in prose", FindsRootFiles);
            yield return ("resolves paths from the terminal cwd", ResolvesPaths);
            yield return ("rejects ambiguous text and URLs", RejectsNonPaths);
        }

        static void FindsPaths()
        {
            AssertEx.Equal("notes/index.md", PathScan.At("see notes/index.md now", 8),
                "a path with a directory is found");
            AssertEx.Equal("../src/main.rs", PathScan.At("../src/main.rs", 5),
                "an explicit parent-relative path is found");

            int line;
            AssertEx.Equal("src/main.rs", PathScan.At("src/main.rs:12:3", 5, out line),
                "a relative source path is found with its location");
            AssertEx.Equal(12, line, "the line is kept while the column is discarded");
        }

        static void FindsAbsolute()
        {
            AssertEx.Equal("/etc/passwd", PathScan.At("/etc/passwd", 5),
                "an absolute path is found");
            AssertEx.Equal("/home/user/main.rs", PathScan.At("(/home/user/main.rs:12:3)", 6),
                "an absolute path keeps its wrappers and location trimmed");
            AssertEx.True(PathScan.At("a / b", 2) == null, "a bare slash is not a path");
        }

        static void TrimsLocations()
        {
            AssertEx.Equal("mod/Foo.cs", PathScan.At("(mod/Foo.cs:12:3)", 6),
                "compiler line and column suffixes are removed");
            AssertEx.Equal("./README", PathScan.At("`./README`,", 4),
                "quotes and punctuation are removed");
        }

        static void FindsRootFiles()
        {
            int line;
            AssertEx.Equal("AGENTS.md", PathScan.At("at AGENTS.md:507. It needs either", 8,
                out line),
                "a root file with a line number survives sentence punctuation");
            AssertEx.Equal(507, line, "a root file keeps its exact source line");
            AssertEx.Equal(".env", PathScan.At("(.env:3)", 2),
                "a hidden root file is a path");
        }

        static void ResolvesPaths()
        {
            AssertEx.Equal("/work/slopworld/slopd/src/main.rs",
                PathScan.ResolveProjectPath("/work/slopworld", "/work/slopworld/slopd",
                    "src/main.rs"),
                "a relative path uses the terminal cwd");
            AssertEx.Equal("/work/slopworld/src/main.rs",
                PathScan.ResolveProjectPath("/work/slopworld", "/work/slopworld/slopd",
                    "../src/main.rs"),
                "parent components are resolved inside the project");
            AssertEx.True(PathScan.ResolveProjectPath("/work/slopworld", "/work/slopworld/slopd",
                "../../etc/passwd") == null,
                "a path outside the project is rejected");
        }

        static void RejectsNonPaths()
        {
            AssertEx.True(PathScan.At("README", 2) == null, "a bare word is ambiguous");
            AssertEx.True(PathScan.At("https://example.test/a", 10) == null, "URLs stay URLs");
        }
    }
}
