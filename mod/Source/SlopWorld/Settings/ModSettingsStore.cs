using System;
using System.IO;
using System.Text;

namespace SlopWorld
{
    // Owns TOML file I/O and replacement. The host supplies profile paths and error reporting.
    static class ModSettingsStore
    {
        public static ModSettings Load(string path, Action<Exception> reportError)
        {
            var settings = new ModSettings();
            try
            {
                if (File.Exists(path))
                {
                    settings = ModSettings.FromToml(File.ReadAllText(path));
                    return settings;
                }
            }
            catch (Exception e)
            {
                reportError?.Invoke(e);
            }
            return settings;
        }

        public static void Write(string path, ModSettings settings)
        {
            string text = settings.ToToml();
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) directory = ".";
            Directory.CreateDirectory(directory);

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
        }

    }
}
