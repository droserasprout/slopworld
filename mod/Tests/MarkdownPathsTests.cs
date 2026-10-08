using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class MarkdownPathsTests
    {
        static MarkdownPathResolver Resolver(string project = "demo", string root = "/work/demo") =>
            new MarkdownPathResolver(project, "/work/demo/docs/readme.md", () => root);

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string source in new[] { null, "", "  ", "#section", "?query", "//example.test/file",
                "file:///etc/passwd", "javascript:alert(1)", "../../outside", "../../demo-other/file", "%00" })
                yield return ("reject unsafe or empty Markdown destination: " + (source ?? "null"), () =>
                {
                    var resolver = Resolver();
                    AssertEx.False(resolver.TryResolveLink(source, out var external, out var local), "link is not actionable");
                    AssertEx.Equal<string>(null, external, "no external destination");
                    AssertEx.Equal<string>(null, local, "no local destination");
                    AssertEx.Equal<string>(null, resolver.ResolveImagePath(source), "image cannot escape project");
                }
                );
            foreach (string source in new[] { "http://example.test", "https://example.test?a=1&b=2", "mailto:reader@example.test" })
                yield return ("external link allowed but not image: " + source, () =>
                {
                    var resolver = Resolver(project: "");
                    AssertEx.True(resolver.TryResolveLink(source, out var external, out var local), "safe external link does not require project");
                    AssertEx.Equal(source, external, "normalized external destination preserved");
                    AssertEx.Equal<string>(null, local, "external link is not a local file");
                    AssertEx.Equal<string>(null, resolver.ResolveImagePath(source), "external image loads remain disabled");
                }
                );
        }

        public static void LocalResourcesDecodeOnceAndStayWithinProject()
        {
            var resolver = Resolver();
            const string source = "../assets/a%23b%3Fc%2520.png?download=1#preview";
            const string expected = "/work/demo/assets/a#b?c%20.png";
            AssertEx.True(resolver.TryResolveLink(source, out var external, out var local), "project-relative resource accepted");
            AssertEx.Equal(expected, local, "encoded delimiters belong to filename and escapes decode once");
            AssertEx.Equal<string>(null, external, "local resource is not an external URL");
            AssertEx.Equal(expected, resolver.ResolveImagePath(source), "image and link use the same decoding rules");
            AssertEx.True(resolver.IsInsideProject("/work/demo/assets/file"), "project descendant accepted");
            AssertEx.False(resolver.IsInsideProject("/work/demo-other/file"), "sibling prefix is not a project descendant");
        }

        public static void MissingOrInvalidProjectRootsRejectLocalResources()
        {
            foreach (var resolver in new[] { Resolver(project: ""), Resolver(root: ""), Resolver(root: null), Resolver(root: "bad\0root") })
            {
                AssertEx.False(resolver.TryResolveLink("next.md", out _, out _), "unresolved project cannot open local links");
                AssertEx.Equal<string>(null, resolver.ResolveImagePath("figure.png"), "unresolved project cannot load local images");
                AssertEx.False(resolver.IsInsideProject("/work/demo/file"), "unresolved project cannot authorize paths");
            }
        }
        public static void RejectsImageSchemes()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal(null, resolver.ResolveImagePath("https://example.test/a.png"),
                "remote images stay disabled");
            AssertEx.Equal(null, resolver.ResolveImagePath("data:image/png;base64,AA=="),
                "data images stay disabled");
            AssertEx.Equal(null, resolver.ResolveImagePath("ftp://example.test/a.png"),
                "unsupported image schemes stay disabled");
        }

        public static void DecodesUriPaths()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal("/work/demo/docs/my file.md",
                resolver.ResolvedDestination("my%20file.md?view=raw#section"),
                "query and fragment are removed before percent decoding");
            AssertEx.Equal("/work/demo/docs/café.md",
                resolver.ResolvedDestination("caf%C3%A9.md"), "Unicode URI paths decode");
            AssertEx.Equal("/work/demo/docs/100%done.md",
                resolver.ResolvedDestination("100%25done.md"), "encoded literal percent decodes once");
        }

        public static void RejectsEncodedTraversal()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal(null, resolver.ResolvedDestination("%2e%2e/%2e%2e/etc/passwd"),
                "encoded traversal is checked after decoding");
        }



    }

    static class MarkdownPathResolverTestExtensions
    {
        public static string ResolvedDestination(this MarkdownPathResolver resolver, string source)
        {
            resolver.TryResolveLink(source, out var external, out var local);
            return external == null ? local : external;
        }
    }
}
