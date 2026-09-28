using System;

namespace SlopWorld
{
    // Scan on Ctrl+MouseDown, not repaint or pointer motion.
    // Recognize relative and absolute paths. FilesView validates project reachability.
    public static class PathScan
    {
        public static string At(string text, int column) => At(text, column, out _);

        // Returns the source line when the path carries a `:line` or `:line:column`
        // suffix. A zero line means that no usable source line was present.
        public static string At(string text, int column, out int line)
        {
            line = 0;
            if (string.IsNullOrEmpty(text) || column < 0 || column >= text.Length)
                return null;

            int start = column, end = column + 1;
            while (start > 0 && !Boundary(text[start - 1])) start--;
            while (end < text.Length && !Boundary(text[end])) end++;
            while (start < end && IsWrapper(text[start])) start++;
            while (end > start && IsTail(text[end - 1])) end--;

            // Compiler locations commonly append :line or :line:column.
            int location = end;
            while (location > start)
            {
                int colon = text.LastIndexOf(':', location - 1, location - start);
                if (colon < start || !Digits(text, colon + 1, location)) break;
                line = Number(text, colon + 1, location);
                location = colon;
            }
            if (location < end) end = location;

            if (end <= start) return null;
            string path = text.Substring(start, end - start);
            // A scheme's `//` is not a directory separator. Leave URLs to UrlScan.
            if (path.IndexOf("://", StringComparison.Ordinal) >= 0)
                return null;
            if (path[0] == '/')
            {
                // A bare "/" is the root, not a file the tree needs to scroll to.
                if (path.Length < 2) return null;
                return path;
            }
            if (!path.StartsWith("./", StringComparison.Ordinal) &&
                !path.StartsWith("../", StringComparison.Ordinal) &&
                path.IndexOf('/') < 0 && !LooksLikeRootFile(path))
                return null;
            return path;
        }

        // Resolve a diagnostic against the terminal cwd, then require the result to remain
        // below the configured project root. The viewer runs from the project root, so it
        // needs this absolute form even when the terminal printed a relative path.
        public static string ResolveProjectPath(string root, string cwd, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;
            try
            {
                char separator = System.IO.Path.DirectorySeparatorChar;
                string rootPath = System.IO.Path.GetFullPath(NormalizeSeparators(root))
                    .TrimEnd(separator);
                if (rootPath.Length == 0) rootPath = separator.ToString();
                string basePath = string.IsNullOrEmpty(cwd) ? rootPath :
                    System.IO.Path.GetFullPath(NormalizeSeparators(cwd));
                string absolute = System.IO.Path.IsPathRooted(path)
                    ? System.IO.Path.GetFullPath(NormalizeSeparators(path))
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath,
                        NormalizeSeparators(path)));
                string prefix = rootPath == separator.ToString()
                    ? rootPath : rootPath + separator;
                StringComparison comparison = separator == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (absolute == rootPath || !absolute.StartsWith(prefix, comparison)) return null;
                return absolute.Replace(separator, '/');
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string NormalizeSeparators(string path) =>
            (path ?? "").Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar);

        static bool Boundary(char c) => char.IsWhiteSpace(c) || c == '"' || c == '`' ||
            c == '<' || c == '>' || c == '|' || c == '=';

        static bool IsWrapper(char c) => c == '(' || c == '[' || c == '{' || c == '\'';

        static bool IsTail(char c) => c == ')' || c == ']' || c == '}' || c == '\'' ||
            c == ',' || c == ';' || c == ':' || c == '!' || c == '?' || c == '.';

        static bool LooksLikeRootFile(string path)
        {
            int dot = path.LastIndexOf('.');
            return dot >= 0 && dot + 1 < path.Length;
        }

        // A dash immediately after an extension may introduce prose ("file.rs—the next").
        // The caller must first check the complete name, then confirm this candidate exists.
        internal static string BeforeProseDash(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] != '—' && name[i] != '–') continue;
                int dot = name.LastIndexOf('.', i);
                if (dot <= 0 || dot == i - 1) continue;
                bool extension = true;
                for (int j = dot + 1; j < i; j++)
                    if (!IsExtensionCharacter(name[j])) { extension = false; break; }
                if (extension) return name.Substring(0, i);
            }
            return null;
        }

        static bool IsExtensionCharacter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9');

        static int Number(string text, int start, int end)
        {
            int value = 0;
            for (int i = start; i < end; i++)
            {
                int digit = text[i] - '0';
                if (value > (int.MaxValue - digit) / 10) return 0;
                value = value * 10 + digit;
            }
            return value;
        }

        static bool Digits(string text, int start, int end)
        {
            if (start >= end) return false;
            for (int i = start; i < end; i++)
                if (text[i] < '0' || text[i] > '9') return false;
            return true;
        }
    }
}
