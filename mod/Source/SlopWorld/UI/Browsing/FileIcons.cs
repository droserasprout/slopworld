using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // File-view icons are baked from the Material Icon Theme manifest by tools/assets/fileicons.py
    // into loose textures. Keep the lookup table in code (the mod has no TOML parser), and
    // cache hits and misses because ContentFinder would otherwise scan every tree row.
    public static class FileIcons
    {
        const string Dir = "SlopWorld/FileIcons/";

        // The two names the lookup falls back to rather than looks up.
        const string Folder = "folder-base";
        const string Plain = "document";

        static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>();

        // Whole filenames, tried before anything is read as an extension - a lockfile is a
        // lockfile whatever it is called after the dot.
        static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "go.mod", "go" }, { "go.sum", "go" },
                { "Gemfile", "ruby" }, { "Rakefile", "ruby" },
                { ".bashrc", "console" }, { ".zshrc", "console" },
                { ".profile", "console" }, { ".bash_profile", "console" },
                { "Cargo.lock", "lock" }, { "package-lock.json", "lock" },
                { "yarn.lock", "lock" }, { "pnpm-lock.yaml", "lock" },
                { "poetry.lock", "lock" }, { "uv.lock", "lock" },
                { "flake.lock", "lock" }, { "bun.lockb", "lock" },
                { "composer.lock", "lock" },
                { ".gitignore", "git" }, { ".gitattributes", "git" },
                { ".gitmodules", "git" }, { ".gitconfig", "git" },
                { ".gitkeep", "git" }, { ".mailmap", "git" },
                { "Dockerfile", "docker" }, { ".dockerignore", "docker" },
                { "docker-compose.yml", "docker" }, { "docker-compose.yaml", "docker" },
                { "compose.yml", "docker" }, { "compose.yaml", "docker" },
                { "Makefile", "makefile" }, { "GNUmakefile", "makefile" },
                { "justfile", "makefile" },
                { "CMakeLists.txt", "cmake" },
                { "gradlew", "gradle" }, { "gradle.properties", "gradle" },
                { "package.json", "nodejs" }, { ".nvmrc", "nodejs" },
                { ".node-version", "nodejs" },
                { ".npmrc", "npm" }, { ".npmignore", "npm" }, { ".yarnrc", "npm" },
                { "tsconfig.json", "tsconfig" }, { "jsconfig.json", "tsconfig" },
                { "LICENSE", "license" }, { "LICENCE", "license" },
                { "LICENSE.md", "license" }, { "LICENSE.txt", "license" },
                { "COPYING", "license" }, { "COPYING.LESSER", "license" },
                { "NOTICE", "license" }, { "UNLICENSE", "license" },
                { "README", "readme" }, { "README.md", "readme" },
                { "README.txt", "readme" }, { "README.rst", "readme" },
                { "AGENTS.md", "readme" }, { "CLAUDE.md", "readme" },
                { "CHANGELOG", "changelog" }, { "CHANGELOG.md", "changelog" },
                { "CHANGES.md", "changelog" }, { "HISTORY.md", "changelog" },
                { "NEWS.md", "changelog" },
                { "CONTRIBUTING.md", "contributing" },
                { "CODE_OF_CONDUCT.md", "contributing" }, { "SECURITY.md", "contributing" },
                { "TODO", "todo" }, { "TODO.md", "todo" }, { "TODOS.md", "todo" },
                { ".editorconfig", "settings" }, { ".env", "settings" },
                { ".env.local", "settings" }, { ".env.example", "settings" },
                { ".envrc", "settings" }, { ".clang-format", "settings" },
                { ".vimrc", "vim" }, { ".nvimrc", "vim" },
            };

        static readonly Dictionary<string, string> Exts =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "rs", "rust" },
                { "py", "python" }, { "pyi", "python" }, { "pyw", "python" },
                { "ts", "typescript" }, { "mts", "typescript" }, { "cts", "typescript" },
                { "tsx", "react" }, { "jsx", "react" },
                { "js", "javascript" }, { "mjs", "javascript" }, { "cjs", "javascript" },
                { "go", "go" },
                { "cs", "csharp" }, { "csx", "csharp" },
                { "c", "c" },
                { "h", "h" }, { "hh", "h" }, { "hpp", "h" }, { "hxx", "h" },
                { "cpp", "cpp" }, { "cc", "cpp" }, { "cxx", "cpp" }, { "c++", "cpp" },
                { "java", "java" }, { "class", "java" }, { "jar", "java" },
                { "kt", "kotlin" }, { "kts", "kotlin" },
                { "swift", "swift" },
                { "rb", "ruby" }, { "gemspec", "ruby" },
                { "php", "php" },
                { "lua", "lua" },
                { "zig", "zig" },
                { "ex", "elixir" }, { "exs", "elixir" },
                { "hs", "haskell" }, { "lhs", "haskell" },
                { "scala", "scala" }, { "sc", "scala" },
                { "dart", "dart" },
                { "pl", "perl" }, { "pm", "perl" }, { "t", "perl" },
                { "jl", "julia" },
                { "ml", "ocaml" }, { "mli", "ocaml" },
                { "clj", "clojure" }, { "cljs", "clojure" },
                { "cljc", "clojure" }, { "edn", "clojure" },
                { "nim", "nim" }, { "nims", "nim" },
                { "cr", "crystal" },
                { "sol", "solidity" },
                { "r", "r" }, { "rmd", "r" },
                { "asm", "assembly" }, { "s", "assembly" },
                { "vue", "vue" },
                { "svelte", "svelte" },
                { "astro", "astro" },
                { "sh", "console" }, { "bash", "console" }, { "zsh", "console" },
                { "fish", "console" }, { "ksh", "console" },
                { "ps1", "powershell" }, { "psm1", "powershell" }, { "psd1", "powershell" },
                { "tex", "tex" }, { "bib", "tex" }, { "cls", "tex" }, { "sty", "tex" },
                { "md", "markdown" }, { "mdx", "markdown" }, { "markdown", "markdown" },
                { "html", "html" }, { "htm", "html" }, { "xhtml", "html" },
                { "css", "css" },
                { "scss", "sass" }, { "sass", "sass" }, { "less", "sass" },
                { "styl", "sass" },
                { "json", "json" }, { "jsonc", "json" }, { "json5", "json" },
                { "jsonl", "json" }, { "ndjson", "json" },
                { "yaml", "yaml" }, { "yml", "yaml" },
                { "toml", "toml" },
                { "xml", "xml" }, { "xsd", "xml" }, { "xsl", "xml" },
                { "xslt", "xml" }, { "plist", "xml" },
                { "graphql", "graphql" }, { "gql", "graphql" },
                { "proto", "proto" },
                { "prisma", "prisma" },
                { "csv", "table" }, { "tsv", "table" }, { "xls", "table" },
                { "xlsx", "table" }, { "ods", "table" },
                { "sql", "database" }, { "db", "database" }, { "sqlite", "database" },
                { "sqlite3", "database" }, { "duckdb", "database" },
                { "log", "log" },
                { "diff", "diff" }, { "patch", "diff" },
                { "url", "url" }, { "webloc", "url" }, { "desktop", "url" },
                { "eml", "email" }, { "mbox", "email" },
                { "png", "image" }, { "jpg", "image" }, { "jpeg", "image" },
                { "gif", "image" }, { "bmp", "image" }, { "webp", "image" },
                { "svg", "image" }, { "ico", "image" }, { "tga", "image" },
                { "avif", "image" }, { "tiff", "image" },
                { "mp4", "video" }, { "mkv", "video" }, { "webm", "video" },
                { "mov", "video" }, { "avi", "video" }, { "m4v", "video" },
                { "mp3", "audio" }, { "ogg", "audio" }, { "wav", "audio" },
                { "flac", "audio" }, { "opus", "audio" }, { "m4a", "audio" },
                { "aac", "audio" },
                { "pdf", "pdf" },
                { "zip", "zip" }, { "tar", "zip" }, { "tgz", "zip" }, { "gz", "zip" },
                { "xz", "zip" }, { "zst", "zip" }, { "bz2", "zip" }, { "7z", "zip" },
                { "rar", "zip" }, { "tar.gz", "zip" }, { "tar.xz", "zip" },
                { "tar.zst", "zip" },
                { "exe", "exe" }, { "dll", "exe" }, { "so", "exe" }, { "dylib", "exe" },
                { "bin", "exe" }, { "o", "exe" }, { "a", "exe" }, { "wasm", "exe" },
                { "pyc", "exe" }, { "app", "exe" },
                { "ttf", "font" }, { "otf", "font" }, { "woff", "font" },
                { "woff2", "font" }, { "eot", "font" },
                { "lock", "lock" },
                { "dockerfile", "docker" },
                { "mk", "makefile" }, { "mak", "makefile" },
                { "cmake", "cmake" },
                { "gradle", "gradle" },
                { "nix", "nix" },
                { "tf", "terraform" }, { "tfvars", "terraform" },
                { "tfstate", "terraform" }, { "hcl", "terraform" },
                { "ini", "settings" }, { "cfg", "settings" }, { "conf", "settings" },
                { "config", "settings" }, { "properties", "settings" },
                { "editorconfig", "settings" }, { "env", "settings" },
                { "pem", "certificate" }, { "crt", "certificate" },
                { "cer", "certificate" }, { "der", "certificate" },
                { "p12", "certificate" }, { "pfx", "certificate" },
                { "key", "key" }, { "pub", "key" }, { "gpg", "key" },
                { "asc", "key" }, { "kdbx", "key" },
                { "vim", "vim" },
                { "txt", "document" }, { "text", "document" }, { "rtf", "document" },
                { "doc", "document" }, { "docx", "document" }, { "odt", "document" },
            };

        public static Texture2D Of(string name, bool isDir) =>
            Tex(isDir ? Folder : Pick(name));

        // Prefer exact names, then the longest matching extension. Dotfiles use the name table.
        static string Pick(string name)
        {
            if (string.IsNullOrEmpty(name)) return Plain;

            string icon;
            if (Names.TryGetValue(name, out icon)) return icon;

            // From the first dot after the first character: a leading dot is what makes a
            // file hidden, not what starts its extension.
            for (int i = name.IndexOf('.', 1); i > 0; i = name.IndexOf('.', i + 1))
                if (Exts.TryGetValue(name.Substring(i + 1), out icon))
                    return icon;

            return Plain;
        }

        static Texture2D Tex(string icon)
        {
            Texture2D tex;
            if (Cache.TryGetValue(icon, out tex)) return tex;

            // Quietly: a name this table has that the bake does not is a generic page, not a
            // red error every frame it is on screen.
            tex = ContentFinder<Texture2D>.Get(Dir + icon, false);
            if ((tex == null || tex == BaseContent.BadTex) && icon != Plain)
                tex = ContentFinder<Texture2D>.Get(Dir + Plain, false);

            Cache[icon] = tex;
            return tex;
        }
    }
}
