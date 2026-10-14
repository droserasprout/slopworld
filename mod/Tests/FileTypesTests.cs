using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class FileTypesTests
    {
        [TestCase("photo.PNG", false)]
        [TestCase("song.flac", false)]
        [TestCase("archive.tar.gz", false)]
        [TestCase("font.woff2", false)]
        [TestCase("store.sqlite", false)]
        [TestCase("app.DLL", false)]
        [TestCase("disk.iso", false)]
        [TestCase("README", true)]
        [TestCase("main.cs", true)]
        [TestCase("data.unknown", true)]
        [TestCase(".gitignore", true)]
        [TestCase("trailing.", true)]
        [TestCase("", true)]
        public void PagerAcceptsTextAndUnknownSuffixes(string name, bool expected) =>
            Assert.That(FileTypes.IsText(name), Is.EqualTo(expected));

        [TestCase("readme.MD", true)]
        [TestCase("guide.markdown", true)]
        [TestCase("page.mdx", true)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("README", false)]
        [TestCase("readme.md.bak", false)]
        [TestCase("photo.png", false)]
        public void MarkdownRequiresSupportedSuffix(string name, bool expected) =>
            Assert.That(FileTypes.IsMarkdown(name), Is.EqualTo(expected));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("photo")]
        [TestCase("photo.png.bak")]
        [TestCase("animation.gif")]
        [TestCase("image.webp")]
        public void NativeImagesRejectUnsupportedSuffixes(string name) =>
            Assert.That(FileTypes.IsImage(name), Is.False);

        [TestCase("figure.png")]
        [TestCase("figure.PNG")]
        [TestCase("photo.jpg")]
        [TestCase("photo.JPEG")]
        public void ImageLocationsAlwaysUseNativePreview(string name)
        {
            foreach (int line in new[] { 0, 1, 42 })
                Assert.That(FileTypes.UseNativePreview(name, line, null), Is.True, "line " + line);
        }

        [TestCase("README.md", 0, null, true)]
        [TestCase("README.md", 12, null, false)]
        [TestCase("code.cs", 0, null, false)]
        [TestCase("code.cs", 12, null, false)]
        [TestCase("code.cs", 12, "", true)]
        public void TextLocationsAndScopedLinksKeepTheirReaderPolicy(
            string name,
            int line,
            string scope,
            bool expected
        ) => Assert.That(FileTypes.UseNativePreview(name, line, scope), Is.EqualTo(expected));
    }
}
