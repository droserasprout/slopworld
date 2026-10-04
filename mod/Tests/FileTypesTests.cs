namespace SlopWorld.Tests
{
    public static class FileTypesTests
    {
        public static void ImageLocationsAlwaysUseNativePreview()
        {
            foreach (string name in new[] { "figure.png", "figure.PNG", "photo.jpg", "photo.JPEG" })
                foreach (int line in new[] { 0, 1, 42 })
                    AssertEx.True(FileTypes.UseNativePreview(name, line, null),
                        name + " uses the image viewer at line " + line);
        }

        public static void TextLocationsAndScopedLinksKeepTheirReaderPolicy()
        {
            AssertEx.True(FileTypes.UseNativePreview("README.md", 0, null), "Markdown defaults to native preview");
            AssertEx.True(!FileTypes.UseNativePreview("README.md", 12, null), "Markdown source locations use pager");
            AssertEx.True(!FileTypes.UseNativePreview("code.cs", 0, null), "ordinary text uses pager");
            AssertEx.True(!FileTypes.UseNativePreview("code.cs", 12, null), "text source locations use pager");
            AssertEx.True(FileTypes.UseNativePreview("code.cs", 12, ""), "scoped links retain native preview");
        }
    }
}
