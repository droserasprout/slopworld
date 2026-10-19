using UnityEngine;

namespace SlopWorld
{
    // Shared reader marks keep workspace chrome consistent with the Open Files pane.
    internal static class ReaderRowChrome
    {
        internal const float IconW = RowActions.IconW;

        internal static Color Tint(RowAct action) =>
            action == RowAct.Edit ? UiTheme.Warn
            : action == RowAct.Diff ? UiTheme.Info
            : UiTheme.Dim;

        internal static Texture2D Icon(ReaderRow reader) =>
            reader.Presentation == ReaderPresentation.Markdown ? Icons.MarkdownPreview
            : reader.Presentation == ReaderPresentation.Image ? Icons.ImagePreview
            : RowActions.Tex(reader.Action);
    }
}
