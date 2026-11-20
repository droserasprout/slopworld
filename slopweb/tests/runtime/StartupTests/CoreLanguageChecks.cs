using System.IO.Compression;

internal static class CoreLanguageChecks
{
    private static byte[] Pack(string name, string text)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
            writer.Write(text);
        return bytes.ToArray();
    }

    internal static void Run()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var packs = new CoreLanguagePacks(temp.FullName);
            Directory.CreateDirectory(Path.Combine(temp.FullName, "English"));
            packs.Register("French (Français)");
            packs.Register("Loose");
            if (
                packs.Choose("../Loose", "English") != ""
                || packs.StartupLanguage("English") != "English"
                || packs.Choose("Removed", "Absent") != ""
                || packs.StartupLanguage("Absent") != "Absent"
                || packs.Choose("English", "Loose") != ""
                || packs.StartupLanguage("Loose") != "English"
            )
                throw new Exception("Core language fallback or built-in selection changed");
            if (packs.Choose("", "Loose") != "Loose" || packs.StartupLanguage("Loose") != "English")
                throw new Exception("Selected an unavailable locale before installation");
            foreach (
                var invalid in new[]
                {
                    "English/keys.xml",
                    "Loose/../English/keys.xml",
                    "Loose/bad.DLL",
                }
            )
            {
                try
                {
                    packs.Install(Pack(invalid, "bad"));
                    throw new Exception("Accepted invalid locale member");
                }
                catch (InvalidDataException) { }
            }
            if (
                packs.StartupLanguage("Loose") != "English"
                || packs.StartupLanguage("English") != "English"
            )
                throw new Exception("Failed pack promoted an unmaterialized catalog entry");
            if (Directory.EnumerateFileSystemEntries(Path.Combine(temp.FullName, "Loose")).Any())
                throw new Exception("A failed pack published partial data");
            packs.Install(Pack("Loose/Keyed/test.xml", "translated"));
            if (
                packs.StartupLanguage("Loose") != "Loose"
                || packs.StartupLanguage("English") != "English"
                || File.ReadAllText(Path.Combine(temp.FullName, "Loose/Keyed/test.xml"))
                    != "translated"
            )
                throw new Exception("Loose language install or retry failed");

            if (packs.Choose("French (Français)", "English") != "French (Français)")
                throw new Exception("Did not request selected tar language");
            packs.Install(Pack("French (Français).tar", "original tar"));
            if (
                packs.StartupLanguage("English") != "French (Français)"
                || File.ReadAllText(Path.Combine(temp.FullName, "French (Français).tar"))
                    != "original tar"
                || Directory.Exists(Path.Combine(temp.FullName, "French (Français)"))
                || packs.Choose("French (Français)", "English") != ""
                || packs.StartupLanguage("English") != "French (Français)"
            )
                throw new Exception("Native tar layout or repeated selection changed");
        }
        finally
        {
            temp.Delete(true);
        }
    }
}
