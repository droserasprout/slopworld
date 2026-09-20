using System;
using System.Diagnostics;
using System.Text;
using UnityEngine.Profiling;

namespace SlopWorld
{
    // All calls are on the Unity main thread. No forced collection or terminal text is logged.
    internal static class MemoryTrace
    {
        static float _next;

        internal static void Append(StringBuilder text, float now)
        {
            if (now < _next) return;
            _next = now + 5f;
            try
            {
                long rss = -1;
                try
                {
                    using (var process = Process.GetCurrentProcess()) rss = process.WorkingSet64;
                }
                catch { }
                text.Append(" memory seconds=").Append((long)now)
                    .Append(" rssBytes=").Append(rss)
                    .Append(" managedUsed=").Append(Profiler.GetMonoUsedSizeLong())
                    .Append(" managedHeap=").Append(Profiler.GetMonoHeapSizeLong())
                    .Append(" unityAllocated=").Append(Profiler.GetTotalAllocatedMemoryLong())
                    .Append(" unityReserved=").Append(Profiler.GetTotalReservedMemoryLong())
                    .Append(" unityUnusedReserved=").Append(Profiler.GetTotalUnusedReservedMemoryLong())
                    .Append(" gc0Total=").Append(GC.CollectionCount(0))
                    .Append(" gc1Total=").Append(GC.CollectionCount(1))
                    .Append(" gc2Total=").Append(GC.CollectionCount(2)).Append(';');
            }
            catch
            {
                // Unsupported counters must not interrupt the session update loop.
                text.Append(" memory-error count=1;");
            }
        }
    }
}
