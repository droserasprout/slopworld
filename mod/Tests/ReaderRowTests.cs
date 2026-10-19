namespace SlopWorld.Tests
{
    static class ReaderRowTests
    {
        public static void NativeMarkdownSourceUsesSourcePresentationAndCapturedBoundary()
        {
            foreach (bool plainText in new[] { false, true })
            {
                var tab = new NativeFileTab
                {
                    Header = "native",
                    Path = "/checkout/readme.md",
                    PreviewRoot = "/checkout",
                    Presentation = ReaderRow.NativeKind("readme.md", plainText),
                    View = new MarkdownPreview("# Document", "readme.md"),
                };
                var info = new SessionInfo
                {
                    Name = tab.Header,
                    Intent = "view",
                    ReaderPath = tab.Path,
                    ReaderScope = "captured-checkout",
                };
                var row = new ReaderRow(info, tab, null);
                AssertEx.Equal(
                    plainText ? ReaderPresentation.Source : ReaderPresentation.Markdown,
                    row.Presentation,
                    "rendering mode overrides the Markdown extension"
                );
                AssertEx.Equal("captured-checkout", row.Target.Scope, "menu retains checkout");
                AssertEx.Equal(tab.Path, row.Target.Path, "menu retains source path");
                AssertEx.Equal("/checkout", row.PreviewRoot, "source switch retains read boundary");
                AssertEx.True(row.CanClose && row.CanPin, "native preview exposes reader actions");
                tab.Lock();
                row = new ReaderRow(info, tab, null);
                AssertEx.True(row.Pinned, "new snapshot follows pin");
                AssertEx.False(row.CanPin, "pinned native does not offer redundant pin");
            }
            AssertEx.Equal(
                ReaderPresentation.Image,
                ReaderRow.NativeKind("image.png", true),
                "images retain native image presentation"
            );
        }

        public static void SessionReadersDoNotInferRenderingFromTheirFilename()
        {
            var source = new SessionInfo
            {
                Name = "pager",
                Intent = "view",
                ReaderPath = "/readme.md",
                Ephemeral = true,
            };
            var row = new ReaderRow(source, null, null);
            AssertEx.Equal(
                ReaderPresentation.Source,
                row.Presentation,
                "pager is source even for Markdown"
            );
            AssertEx.True(row.CanClose, "adopted pager uses fallback dismissal");
            AssertEx.False(row.CanPin, "unowned reader cannot be pinned");
            source.Intent = "edit";
            row = new ReaderRow(source, null, null);
            AssertEx.Equal(RowAct.Edit, row.Action, "editor has process actions");
            AssertEx.False(row.CanClose, "editor requires confirmed Stop");
        }
    }
}
