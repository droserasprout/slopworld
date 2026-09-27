using System.Linq;
using Verse;

namespace SlopWorld
{
    static class ReaderAppearance
    {
        public static void OfferRestart()
        {
            FileReaders.Restore();
            var readers = new[] { SearchView.ActivePager }.Concat(FileReaders.Tabs.All)
                .Where(p => p.CanRestartForAppearance)
                .GroupBy(p => p.Session).Select(group => group.First())
                .Select(p => new { Pager = p, Session = p.Session, Operation = p.Operation }).ToArray();
            if (readers.Length == 0) return;
            Find.WindowStack.Add(AlertDialog.Create("Apply reader settings",
                "Restart active pager and diff tabs to apply the new settings? Reading positions may reset.",
                "Yes", () =>
                {
                    foreach (var reader in readers)
                        reader.Pager.RestartForAppearance(reader.Session, reader.Operation);
                }, "No"));
        }
    }
}
