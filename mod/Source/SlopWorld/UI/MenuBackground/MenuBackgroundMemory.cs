using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using Verse;

namespace SlopWorld
{
    // Once per resident-set replacement, not per frame. Working set includes the whole
    // process and deferred destruction of retired textures. Its delta is not texture cost.
    internal static class MenuBackgroundMemory
    {
        internal static long WorkingSet()
        {
            try
            {
                using (var process = Process.GetCurrentProcess()) return process.WorkingSet64;
            }
            catch { return -1; }
        }

        internal static void Report(Texture2D[] frames, bool baked, long elapsedMs, long before)
        {
            // Diagnostics must never turn a successfully installed background into a failure.
            try
            {
                long pixels = 0, native = 0;
                var formats = new Dictionary<TextureFormat, int>();
                foreach (var tex in frames)
                {
                    formats.TryGetValue(tex.format, out int count);
                    formats[tex.format] = count + 1;
                    pixels += tex.format == TextureFormat.DXT1
                        ? (long)((tex.width + 3) / 4) * ((tex.height + 3) / 4) * 8
                        : (long)tex.width * tex.height * 3;
                    native += Profiler.GetRuntimeMemorySizeLong(tex);
                }
                var labels = new List<string>();
                foreach (var pair in formats) labels.Add($"{pair.Key}:{pair.Value}");
                Log.Message($"[SlopWorld] background memory source={(baked ? "bake" : "cache")} " +
                    $"frames={frames.Length} size={frames[0].width}x{frames[0].height} " +
                    $"formats={string.Join(",", labels)} pixelBytes={pixels} nativeBytes={native} " +
                    $"loadMs={elapsedMs} workingSetBefore={before} workingSetAfter={WorkingSet()} " +
                    $"managedUsed={Profiler.GetMonoUsedSizeLong()} " +
                    $"managedHeap={Profiler.GetMonoHeapSizeLong()}");
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] background memory report unavailable: {e.Message}");
            }
        }
    }
}
