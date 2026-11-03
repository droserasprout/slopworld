using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class FileNodeTests
    {
        public static void RelativePathsRespectRootAndDirectoryBoundaries()
        {
            foreach (string root in new[] { "/project", "/project/" })
            {
                var node = new FileNode { Root = root };
                foreach (string path in new[] { "/project", "/project/", "/projects/a", "/else/a" })
                {
                    node.Path = path;
                    Assert.That(node.Relative, Is.Null);
                }
                node.Path = "/project/a";
                Assert.That(node.Relative, Is.EqualTo("a"));
                node.Path = "/project/src/file.cs";
                Assert.That(node.Relative, Is.EqualTo("src/file.cs"));
            }
            var absolute = new FileNode { Root = "/", Path = "/a" };
            Assert.That(absolute.Relative, Is.EqualTo("a"));
            absolute.Path = "/";
            Assert.That(absolute.Relative, Is.Null);
            absolute.Path = "relative";
            Assert.That(absolute.Relative, Is.Null);
            absolute.Root = null;
            Assert.That(absolute.Relative, Is.Null);
        }

        public static void TreeProjectionRetainsListingStateAndExpansionEligibility()
        {
            var node = new FileNode
            {
                Root = "/project",
                Path = "/project/src",
                Name = "src",
                Project = "checkout",
                IsDir = true,
                HasChildren = true,
                Loading = true,
                Error = "failed",
                More = true,
                Depth = 2,
                Children = new List<FileNode>(),
            };
            IContentTreeNode tree = node;
            Assert.That(tree.Name, Is.EqualTo("src"));
            Assert.That(tree.Key, Is.EqualTo("src"));
            Assert.That(tree.ScopeKey, Is.EqualTo("checkout"));
            Assert.That(tree.IsDirectory, Is.True);
            Assert.That(tree.Depth, Is.EqualTo(2));
            Assert.That(tree.CanExpand, Is.True);
            Assert.That(tree.Loading, Is.True);
            Assert.That(tree.Error, Is.EqualTo("failed"));
            Assert.That(tree.More, Is.True);
            Assert.That(tree.Children, Is.SameAs(node.Children));
            node.HasChildren = false;
            Assert.That(tree.CanExpand, Is.False);
            node.HasChildren = true;
            node.IsDir = false;
            Assert.That(tree.CanExpand, Is.False);
            node.Path = node.Root;
            Assert.That(tree.Key, Is.Empty);
        }
    }
}
