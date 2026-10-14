using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class PresetHierarchyTests
    {
        static PresetInfo Preset(string name, params string[] requires) =>
            new PresetInfo { Name = name, Requires = requires.ToList() };

        [Test]
        public void DependenciesStayTogetherAtTheirActualDepth()
        {
            var rust = Preset("rust");
            var cache = Preset("rust-cache", "rust");
            var sccache = Preset("rust-sccache", "rust-cache");
            var tree = new PresetHierarchy(new[] { sccache, Preset("rust-other"), cache, rust });
            Assert.That(
                tree.Items.Select(p => p.Name),
                Is.EqualTo(new[] { "rust", "rust-cache", "rust-sccache", "rust-other" })
            );
            Assert.That(tree.Depth(rust), Is.Zero);
            Assert.That(tree.Depth(cache), Is.EqualTo(1));
            Assert.That(tree.Depth(sccache), Is.EqualTo(2));
        }

        [Test]
        public void BundlesAndDependenciesOutsideTheGroupRemainRoots()
        {
            var bundle = Preset("bundle", "rust", "python");
            var missing = Preset("missing", "outside");
            var tree = new PresetHierarchy(
                new[] { bundle, missing, Preset("global"), Preset("rust") }
            );
            Assert.That(tree.Items[0].Name, Is.EqualTo("global"));
            Assert.That(tree.Depth(bundle), Is.Zero);
            Assert.That(tree.Depth(missing), Is.Zero);
        }

        [Test]
        public void OnlySlopworldDebugBundleNestsUnderItsDevelopmentDependency()
        {
            var dev = Preset("slopworld-dev", "rust-sccache", "nuget-cache");
            var debug = Preset("slopworld-debug", "docker", "slopworld-dev", "x11");
            var unrelated = Preset("other-debug", "other-dev", "docker", "x11");
            var tree = new PresetHierarchy(
                new[] { debug, unrelated, dev, Preset("other-dev"), Preset("docker") }
            );
            Assert.That(tree.Depth(dev), Is.Zero);
            Assert.That(tree.Depth(debug), Is.EqualTo(1));
            Assert.That(tree.Items.IndexOf(debug), Is.EqualTo(tree.Items.IndexOf(dev) + 1));
            Assert.That(tree.Depth(unrelated), Is.Zero);
            Assert.That(new PresetHierarchy(new[] { debug }).Depth(debug), Is.Zero);

            dev.Requires.Clear();
            dev.Requires.Add(debug.Name);
            tree = new PresetHierarchy(new[] { debug, dev });
            Assert.That(tree.Items.Count, Is.EqualTo(2));
            Assert.That(tree.Items.All(p => tree.Depth(p) == 0), Is.True);
        }

        [Test]
        public void CyclicOrSelfDependenciesRemainVisibleWithoutRecursion()
        {
            var tree = new PresetHierarchy(
                new[] { Preset("a", "b"), Preset("b", "a"), Preset("self", "self") }
            );
            Assert.That(tree.Items.Select(p => p.Name), Is.EqualTo(new[] { "a", "b", "self" }));
            Assert.That(tree.Items.All(p => tree.Depth(p) == 0), Is.True);
        }
    }
}
