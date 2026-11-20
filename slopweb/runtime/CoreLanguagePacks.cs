// Entry reads language files synchronously. Keep catalog directories visible to
// Verse while a selected translation is downloaded and validated before startup.
using System.IO.Compression;

internal sealed class CoreLanguagePacks(string root)
{
    private readonly HashSet<string> deferred = new(StringComparer.Ordinal);
    private string? pending;
    private string? requested;

    internal void Register(string folder)
    {
        if (!ValidFolder(folder) || !deferred.Add(folder))
            throw new InvalidDataException("Invalid Core language folder");
        Directory.CreateDirectory(Path.Combine(root, folder));
    }

    private static bool ValidFolder(string folder) =>
        folder.Length != 0 && folder != "." && folder != ".." && Path.GetFileName(folder) == folder;

    // Verse matches the complete folder name, including any native-script suffix.
    // Only request an installed pack. Missing system locales keep native fallback.
    private bool Available(string folder) =>
        ValidFolder(folder)
        && (
            Directory.Exists(Path.Combine(root, folder))
            || File.Exists(Path.Combine(root, folder + ".tar"))
        );

    internal string Choose(string requested, string nativeDefault)
    {
        this.requested = Available(requested) ? requested : null;
        string folder = this.requested ?? nativeDefault;
        pending = deferred.Contains(folder) ? folder : null;
        return pending ?? "";
    }

    // A failed fetch (or a preference loaded later by Verse) must never promote
    // an empty catalog directory to the active language. English is in Core.
    internal string StartupLanguage(string nativePreference) =>
        requested != null && !deferred.Contains(requested) ? requested
        : deferred.Contains(nativePreference) ? "English"
        : nativePreference;

    internal void Install(byte[] bytes)
    {
        var folder = pending ?? throw new InvalidOperationException("No selected language pack");
        using var archive = new ZipArchive(new MemoryStream(bytes));
        bool tar = archive.Entries.Count == 1 && archive.Entries[0].FullName == folder + ".tar";
        if (
            !tar
            && (
                archive.Entries.Count == 0
                || archive.Entries.Any(entry =>
                    !entry.FullName.StartsWith(folder + "/", StringComparison.Ordinal)
                    || entry.FullName.Split('/').Any(part => part is "" or "." or "..")
                    || entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                )
            )
        )
            throw new InvalidDataException("Unexpected language pack contents");

        // A failed or truncated pack must leave an enumerable catalog entry and
        // allow native startup to fall back without exposing partial translations.
        var temporary = Directory.CreateDirectory(
            Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"))
        );
        try
        {
            archive.ExtractToDirectory(temporary.FullName);
            var placeholder = Path.Combine(root, folder);
            Directory.Delete(placeholder);
            try
            {
                if (tar)
                    File.Move(
                        Path.Combine(temporary.FullName, folder + ".tar"),
                        placeholder + ".tar"
                    );
                else
                    Directory.Move(Path.Combine(temporary.FullName, folder), placeholder);
            }
            catch
            {
                Directory.CreateDirectory(placeholder);
                throw;
            }
            deferred.Remove(folder);
            pending = null;
        }
        finally
        {
            temporary.Delete(true);
        }
    }
}
