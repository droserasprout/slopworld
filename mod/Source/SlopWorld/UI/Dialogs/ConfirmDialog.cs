using System;
using Verse;

namespace SlopWorld
{
    public static class ConfirmDialog
    {
        public static Window Create(string message, Action confirmed, bool destructive = false) =>
            AlertDialog.Create("Confirm", message, "Confirm", confirmed, "Cancel",
                primaryKind: destructive ? UiTheme.Btn.Danger : UiTheme.Btn.Primary,
                width: 480f);
    }
}
