using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Ordinary item creation is shared by the sidebar and command palette. The editor
    // factory owns kind-specific forms; each menu owns its surrounding entries.
    internal static class LibraryItemCreation
    {
        internal static readonly IReadOnlyList<Option> Options = Array.AsReadOnly(
            new[]
            {
                new Option(LibraryItemKind.Prompt, "Prompt", "Prompt"),
                new Option(LibraryItemKind.Breadcrumb, "Breadcrumb", "Breadcrumb"),
                new Option(LibraryItemKind.Shell, "Shell script", "Shell"),
                new Option(LibraryItemKind.FileAction, "File action", "File Action"),
            }
        );

        internal sealed class Option
        {
            readonly LibraryItemKind _kind;
            internal string SidebarLabel { get; }
            internal string PaletteLabel { get; }

            internal Option(LibraryItemKind kind, string sidebarLabel, string paletteLabel)
            {
                _kind = kind;
                SidebarLabel = sidebarLabel;
                PaletteLabel = paletteLabel;
            }

            internal void Open() => WorkspaceWindow.OpenOverPane(EditLibraryItemDialog.New(_kind));
        }
    }
}
