using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityStubs.Gui;
using Verse;

namespace SlopWorld
{
    // Owns installed font discovery and registration with the game's GUI backend.
    // Font indices belong to the backend for its lifetime; Font wrappers may be destroyed.
    static class NativeFonts
    {
        static Dictionary<string, string> _paths;
        static readonly HashSet<string> Mono = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        public static string[] Names(bool monospaced = false)
        {
            EnsureCatalog();
            var names = monospaced ? new List<string>(Mono) : new List<string>(_paths.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public static void Rescan() => _paths = null;

        public static Font Create(string name, int size) => Create(new[] { name }, size);

        public static Font Create(string[] candidates, int size)
        {
            EnsureCatalog();
            var backend = GuiBackend.Current;
            if (backend == null)
                return null;
            foreach (string name in candidates)
            {
                if (!_paths.TryGetValue(name, out string path))
                    continue;
                try
                {
                    // Include size in the registration key: the backend deduplicates by name
                    // and byte length, while fontSize=0 uses the registered import size.
                    int index = backend.RegisterFont(
                        name + "@" + size,
                        File.ReadAllBytes(path),
                        size,
                        0
                    );
                    if (index >= 0)
                        return new Font(name) { GameFontIndex = index };
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Log.Warning($"[SlopWorld] font {name}: {e.Message}");
                }
            }
            return null;
        }

        static void EnsureCatalog()
        {
            if (_paths != null)
                return;
            _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Mono.Clear();
            var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var paths = Font.GetPathsToOSFonts();
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string family = InstalledFontNames.Read(bytes);
                    if (family == null)
                        continue;
                    // Prefer a regular face over styled files of the same family.
                    string stem = Path.GetFileNameWithoutExtension(path);
                    bool regular =
                        !stem.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                        && !stem.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                        && !stem.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
                    int score = regular ? 1 : 0;
                    if (regular && stem.Contains("Regular", StringComparison.OrdinalIgnoreCase))
                        score++;
                    if (!scores.TryGetValue(family, out int previousScore) || score > previousScore)
                    {
                        _paths[family] = path;
                        scores[family] = score;
                        if (InstalledFontNames.Monospaced(bytes))
                            Mono.Add(family);
                        else
                            Mono.Remove(family);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Log.Warning($"[SlopWorld] font catalog {path}: {e.Message}");
                }
            }
        }
    }
}
