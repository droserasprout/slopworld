using System.Collections.Generic;

namespace SlopWorld
{
    public enum EditMode
    {
        New,
        Edit,
        Copy,
    }

    // Identity is the address and presentation of an editor. The edited model and its save
    // policy stay with the feature that owns them.
    public readonly struct EditIdentity
    {
        public readonly EditMode Mode;
        public readonly string OriginalName;
        public readonly string CopySource;

        EditIdentity(EditMode mode, string originalName, string copySource)
        {
            Mode = mode;
            OriginalName = originalName ?? "";
            CopySource = copySource;
        }

        public bool IsNew => Mode != EditMode.Edit;

        public static EditIdentity ForNew() => new EditIdentity(EditMode.New, "", null);

        public static EditIdentity ForEdit(string originalName) =>
            new EditIdentity(EditMode.Edit, originalName, null);

        public static EditIdentity ForCopy(string sourceName) =>
            new EditIdentity(EditMode.Copy, "", sourceName);

        public string Title(string noun)
        {
            switch (Mode)
            {
                case EditMode.Copy: return $"Copy of '{CopySource}'";
                case EditMode.Edit: return $"Edit '{OriginalName}'";
                case EditMode.New: return "New " + noun;
                default: throw new System.ArgumentOutOfRangeException(nameof(Mode), Mode, null);
            }
        }

        public string CopyName(IEnumerable<string> taken, string fallback) =>
            NameTools.FreeName(CopySource, taken, fallback);
    }
}
