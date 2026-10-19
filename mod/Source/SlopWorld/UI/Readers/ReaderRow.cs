namespace SlopWorld
{
    internal enum ReaderPresentation
    {
        Source,
        Markdown,
        Image,
    }

    // One presentation snapshot for routed reader drawing and menus. Native adapters
    // supply their actual rendering kind; daemon metadata supplies source identity only.
    internal sealed class ReaderRow
    {
        internal readonly string Identity;
        internal readonly ReaderFileTarget Target;
        internal readonly ReaderPresentation Presentation;
        internal readonly RowAct Action;
        internal readonly bool IsPreview,
            Pinned,
            CanClose;
        internal readonly string PreviewRoot;
        internal bool CanPin => IsPreview && !Pinned;

        internal ReaderRow(SessionInfo info, IPreviewTab owner, ProjectInfo project)
        {
            Identity = info.Name;
            Target = ReaderFileTarget.From(info, project);
            Action = SessionRowAction.Of(info);
            IsPreview = owner?.Alive == true;
            Pinned = IsPreview && owner.Locked;
            CanClose = owner != null || ReaderDismissal.CanClose(info);
            var native = owner as NativeFileTab;
            Presentation = native?.Presentation ?? ReaderPresentation.Source;
            PreviewRoot = native?.PreviewRoot;
        }

        // The same choice creates native content and describes it in the sidebar.
        internal static ReaderPresentation NativeKind(string name, bool plainText) =>
            FileTypes.IsImage(name) ? ReaderPresentation.Image
            : !plainText && FileTypes.IsMarkdown(name) ? ReaderPresentation.Markdown
            : ReaderPresentation.Source;
    }
}
