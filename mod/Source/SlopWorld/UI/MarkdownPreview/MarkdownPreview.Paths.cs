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
        readonly Func<string> _projectRoot;

        public MarkdownPathResolver(string project, string path)
            : this(project, path, null)
        {
        }

        internal MarkdownPathResolver(string project, string path, Func<string> projectRoot)
        {
            _project = project ?? "";
            _path = path ?? "";
            _projectRoot = projectRoot;
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

            if (source.StartsWith("//", StringComparison.Ordinal)) return false;

            // Relative links are useful in project README files. Keep them inside the owning
            // project so a document cannot turn a Ctrl+click into an arbitrary root-file read.
            if (string.IsNullOrEmpty(_project)) return false;
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root)) return false;

            source = UriPath(source);
            if (source == null) return false;
            if (source.Length == 0) return false;

            try
            {
                string relative = Uri.UnescapeDataString(source)
                    .Replace('/', Path.DirectorySeparatorChar);
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
            catch (UriFormatException)
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

            source = UriPath(source);
            if (source == null || source.Length == 0) return null;

            string baseDir = Path.GetDirectoryName(_path) ?? ".";
            try
            {
                string local = Uri.UnescapeDataString(source)
                    .Replace('/', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.IsPathRooted(local)
                    ? local : Path.Combine(baseDir, local));
                return IsInsideProject(candidate) ? candidate : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        // Strip URI suffixes before decoding. Encoded delimiters belong to the filename.
        static string UriPath(string source)
        {
            int query = source.IndexOf('?');
            int fragment = source.IndexOf('#');
            int end = source.Length;
            if (query >= 0) end = Math.Min(end, query);
            if (fragment >= 0) end = Math.Min(end, fragment);
            return source.Substring(0, end);
        }

        string ProjectRoot => _projectRoot != null
            ? _projectRoot()
            : SessionHub.Instance.Project(_project)?.ExpandedDir;

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
