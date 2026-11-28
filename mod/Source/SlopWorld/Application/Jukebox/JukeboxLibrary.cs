using System;
using System.IO;
using System.Threading.Tasks;

namespace SlopWorld
{
    // Like, History and Edit share one TOML document. The browser supplies origin storage
    // during platform installation. Native clients use the existing game-local file.
    internal static class JukeboxLibrary
    {
        internal static Func<string> BrowserRead;

        // A null original appends one record. A replacement compares the editor's original.
        internal static Func<string, string, Task> BrowserWrite;
        internal static event Action Changed;

        internal static string Location =>
            BrowserRead == null ? ApplicationPaths.LikesPath : "This browser";

        internal static string Read()
        {
            if (BrowserRead != null)
                return BrowserRead();
            string path = ApplicationPaths.LikesPath;
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        internal static Task Append(JukeboxLikeRecord record)
        {
            if (BrowserWrite != null)
                return WriteBrowser(null, JukeboxLikeWriter.Format(record));
            return JukeboxLikeWriter.TryAppend(ApplicationPaths.LikesPath, record, out var error)
                ? Task.CompletedTask
                : Task.FromException(error);
        }

        internal static async Task WriteBrowser(string original, string text)
        {
            await BrowserWrite(original, text);
            NotifyChanged();
        }

        internal static void NotifyChanged() => Changed?.Invoke();
    }
}
