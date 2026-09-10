using System;

namespace SlopWorld
{
    public enum SelectionCommand
    {
        Copy,
        Paste,
        SelectAll,
        Cut,
    }

    public readonly struct SelectionCommandAvailability
    {
        public readonly bool CanCopy;
        public readonly bool CanPaste;
        public readonly bool CanSelectAll;
        public readonly bool CanCut;

        public SelectionCommandAvailability(bool canCopy, bool canPaste,
                                             bool canSelectAll, bool canCut)
        {
            CanCopy = canCopy;
            CanPaste = canPaste;
            CanSelectAll = canSelectAll;
            CanCut = canCut;
        }
    }

    public static class SelectionCommandPolicy
    {
        public static bool IsEnabled(SelectionCommand command,
                                     SelectionCommandAvailability availability)
        {
            switch (command)
            {
                case SelectionCommand.Copy: return availability.CanCopy;
                case SelectionCommand.Paste: return availability.CanPaste;
                case SelectionCommand.SelectAll: return availability.CanSelectAll;
                case SelectionCommand.Cut: return availability.CanCut;
                default: return false;
            }
        }

        public static bool TryExecute(SelectionCommand command,
                                      SelectionCommandAvailability availability,
                                      Action action)
        {
            if (!IsEnabled(command, availability) || action == null) return false;
            action();
            return true;
        }
    }
}
