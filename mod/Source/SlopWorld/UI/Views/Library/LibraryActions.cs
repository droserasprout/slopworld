using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Shared library execution and management. Surfaces retain navigation history and
    // visibility; project selection and temporary-project routing have one owner.
    static class LibraryActions
    {
        public static void Run(LibraryItemInfo item, string project = null, bool temp = false)
        {
            if (item.Kind != LibraryItemKind.Prompt && item.Kind != LibraryItemKind.Shell) return;
            if (item.Link == LibraryItemLink.Ask && project == null && !temp)
            {
                TerminalWindow.OpenOverPane(new UiMenu(WhereOptions(item)));
                return;
            }
            bool scratch = temp || item.Link == LibraryItemLink.Temp;
            SessionHub.Instance.SessionStore.RunLibraryItem(item.Name,
                session => TerminalWindow.Open(session), UiLayout.Fail,
                project ?? (scratch ? null : item.Project), scratch,
                Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        public static List<FloatMenuOption> WhereOptions(LibraryItemInfo item)
        {
            var options = SessionHub.Instance.Projects.Select(p =>
                new FloatMenuOption($"{p.Name}  -  {p.Dir}", () => Run(item, p.Name))).ToList();
            options.Add(new FloatMenuOption($"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(item, null, true)));
            return options;
        }

        public static void Edit(LibraryItemInfo item) =>
            TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(item));
        public static void Duplicate(LibraryItemInfo item) =>
            TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(item));
        public static void Remove(LibraryItemInfo item)
        {
            string name = item.Name;
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                $"Remove library entry '{name}'? Anything it already started keeps running.",
                () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiLayout.Fail), destructive: true));
        }
    }
}
