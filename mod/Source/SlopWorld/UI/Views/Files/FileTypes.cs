using System.Collections.Generic;

namespace SlopWorld
{
    // Shared file classification; the daemon still owns all file access.
    static class FileTypes
    {
        // Unknown extensions are offered to the pager; known binary formats are excluded.
        static readonly HashSet<string> BinaryExt = new HashSet<string>
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".tga",
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".opus",
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv",
            ".pdf", ".ps", ".zip", ".gz", ".tar", ".7z", ".rar", ".xz", ".bz2",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".db", ".sqlite", ".sqlite3", ".dll", ".so", ".dylib", ".exe",
            ".bin", ".o", ".a", ".class", ".pyc", ".pyo", ".jar", ".iso", ".img",
        };

        public static bool IsText(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot < 0) return true;               // no extension: read it as text
            return !BinaryExt.Contains(name.Substring(dot).ToLowerInvariant());
        }

        public static bool IsMarkdown(string name)
        {
            int dot = (name ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot).ToLowerInvariant();
            return ext == ".md" || ext == ".markdown" || ext == ".mdx";
        }

        public static bool IsImage(string name)
        {
            int dot = (name ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

    }
}
