using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityStubs.Gui;
using Verse;

namespace SlopWorld
{
    // Owns installed font discovery and registration with the game's GUI backend.
    // Font indices belong to the backend for its lifetime; Font wrappers may be destroyed.
    static class NativeFonts
    {
        sealed class Catalog
        {
            internal readonly Dictionary<string, (string Path, int Face)> Paths = new(
                StringComparer.OrdinalIgnoreCase
            );
            internal readonly HashSet<string> Mono = new(StringComparer.OrdinalIgnoreCase);
            internal readonly List<string> Warnings = new();
        }

        static readonly object Gate = new object();
        static Task<Catalog> pending;
        static Catalog catalog;

        // The game's OS font discovery is file/fontconfig enumeration, with no graphics
        // access. Workers build a private snapshot; only consumers publish it.
        internal static void Prepare()
        {
            lock (Gate)
                if (catalog == null)
                    pending ??= Task.Run(Scan);
        }

        public static string[] Names(bool monospaced = false)
        {
            var current = EnsureCatalog();
            var names = monospaced
                ? new List<string>(current.Mono)
                : new List<string>(current.Paths.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public static void Rescan()
        {
            lock (Gate)
            {
                catalog = null;
                // Old scans contain managed snapshots only. Observe failures without ever
                // publishing their results after the catalog generation has changed.
                if (pending != null)
                    _ = pending.ContinueWith(
                        t =>
                        {
                            _ = t.Exception;
                        },
                        TaskScheduler.Default
                    );
                pending = null;
            }
        }

        public static Font Create(string name, int size) => Create(new[] { name }, size);

        public static Font Create(string[] candidates, int size)
        {
            var current = EnsureCatalog();
            var backend = GuiBackend.Current;
            if (backend == null)
                return null;
            foreach (string name in candidates)
            {
                if (!current.Paths.TryGetValue(name, out var source))
                    continue;
                try
                {
                    // Include size in the registration key: the backend deduplicates by name
                    // and byte length, while fontSize=0 uses the registered import size.
                    int index = backend.RegisterFont(
                        name + "@" + size,
                        OpenTypeCollection.Extract(File.ReadAllBytes(source.Path), source.Face),
                        size,
                        0
                    );
                    if (index >= 0)
                        return new Font(name) { GameFontIndex = index };
                }
                catch (Exception e)
                    when (e is IOException
                        || e is UnauthorizedAccessException
                        || e is ArgumentException
                        || e is OverflowException
                    )
                {
                    Log.Warning($"[SlopWorld] font {name}: {e.Message}");
                }
            }
            return null;
        }

        static Catalog EnsureCatalog()
        {
            while (true)
            {
                Task<Catalog> scan;
                lock (Gate)
                {
                    if (catalog != null)
                        return catalog;
                    pending ??= Task.Run(Scan);
                    scan = pending;
                }
                Catalog result;
                try
                {
                    result = scan.GetAwaiter().GetResult();
                }
                catch
                {
                    lock (Gate)
                        if (ReferenceEquals(scan, pending))
                            pending = null;
                    throw;
                }
                lock (Gate)
                {
                    if (!ReferenceEquals(scan, pending))
                        continue;
                    if (catalog == null)
                    {
                        catalog = result;
                        foreach (string warning in result.Warnings)
                            Log.Warning(warning);
                    }
                    return catalog;
                }
            }
        }

        static Catalog Scan()
        {
            var result = new Catalog();
            var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var paths = (string[])Font.GetPathsToOSFonts().Clone();
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    for (int face = 0; face < InstalledFontNames.Count(bytes); face++)
                    {
                        string family = InstalledFontNames.Read(bytes, face);
                        if (family == null)
                            continue;
                        int score = InstalledFontNames.StyleDistance(bytes, face);
                        if (
                            !scores.TryGetValue(family, out int previousScore)
                            || score < previousScore
                        )
                        {
                            result.Paths[family] = (path, face);
                            scores[family] = score;
                            if (InstalledFontNames.Monospaced(bytes, face))
                                result.Mono.Add(family);
                            else
                                result.Mono.Remove(family);
                        }
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    result.Warnings.Add($"[SlopWorld] font catalog {path}: {e.Message}");
                }
            }
            return result;
        }
    }
}
