using System;
using Verse;

namespace SlopWorld
{
    public static class ConfirmDialog
    {
        public static Window Create(string message, Action confirmed, bool destructive = false) =>
            AlertDialog.Create("Confirm", message, "Confirm", confirmed, "Cancel",
                primaryKind: destructive ? UiWidgets.Btn.Danger : UiWidgets.Btn.Primary,
                width: 480f);
    }
}
