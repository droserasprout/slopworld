using System;
using System.IO;

namespace SlopWorld
{
    // Resolves document-relative resources once for both the parser and resource loader.
    // Keeping the project boundary here makes every caller use the same filesystem guard.
    sealed class MarkdownPathResolver
    {
        readonly string _project;
        readonly string _path;

        public MarkdownPathResolver(string project, string path)
        {
            _project = project ?? "";
            _path = path ?? "";
        }

        public bool TryResolveLink(string source, out string external, out string local)
        {
            external = null;
            local = null;
            source = MarkdownMarkup.Decode(source).Trim();
            if (source.Length == 0 || source.StartsWith("#", StringComparison.Ordinal)) return false;

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                !string.IsNullOrEmpty(uri.Scheme))
            {
                if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, "mailto", StringComparison.OrdinalIgnoreCase))
                {
                    external = source;
                    return true;
                }
                return false;
            }

            // Relative links are useful in project README files. Keep them inside the owning
            // project so a document cannot turn a Ctrl+click into an arbitrary root-file read.
            if (string.IsNullOrEmpty(_project)) return false;
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root)) return false;

            int fragment = source.IndexOf('#');
            if (fragment >= 0) source = source.Substring(0, fragment);
            int query = source.IndexOf('?');
            if (query >= 0) source = source.Substring(0, query);
            if (source.Length == 0) return false;

            try
            {
                string relative = source.Replace('/', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.IsPathRooted(relative)
                    ? relative : Path.Combine(Path.GetDirectoryName(_path) ?? root, relative));
                if (!IsInsideProject(candidate)) return false;

                local = candidate;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public string ResolveImagePath(string source)
        {
            source = MarkdownMarkup.Decode(source).Trim();
            if (source.Length == 0 || source.StartsWith("//", StringComparison.Ordinal) ||
                source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                !string.IsNullOrEmpty(uri.Scheme)) return null;

            string baseDir = Path.GetDirectoryName(_path) ?? ".";
            string local = source.Replace('/', Path.DirectorySeparatorChar);
            try
            {
                string candidate = Path.GetFullPath(Path.IsPathRooted(local)
                    ? local : Path.Combine(baseDir, local));
                return IsInsideProject(candidate) ? candidate : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        string ProjectRoot => SessionHub.Instance.Project(_project)?.Dir;

        public bool IsInsideProject(string candidate)
        {
            if (string.IsNullOrEmpty(_project)) return false;
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root)) return false;
            try
            {
                string normalizedRoot = Path.GetFullPath(root).TrimEnd(
                    Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
