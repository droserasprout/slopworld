namespace SlopWorld
{
    // Track the unfiltered reader lifetime independently of the pane's manual visibility.
    // Pending opens count as occupancy so cancellation/failure can close the empty pane too.
    internal sealed class OpenFilesVisibility
    {
        bool _occupied;

        // Explicit dismissal follows the rows the user can see. Hidden readers retain
        // their lifetime, but must not leave an empty pane behind after its last row closes.
        public bool ReaderDismissed(int visibleReaders, bool opening) =>
            visibleReaders == 0 && !opening;

        public bool ShouldHide(bool hasReaders, bool opening)
        {
            bool occupied = hasReaders || opening;
            bool hide = _occupied && !occupied;
            _occupied = occupied;
            return hide;
        }
    }
}
