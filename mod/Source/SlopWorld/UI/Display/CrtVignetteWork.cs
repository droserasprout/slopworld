using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SlopWorld
{
    // Called only by the presentation thread. Workers own arrays, never textures or the
    // MonoBehaviour. A resize coalesces behind the current job; stale pixels cannot publish.
    internal sealed class CrtVignetteWork : IDisposable
    {
        internal readonly record struct Request(
            int Width,
            int Height,
            float Corner,
            float Scanline,
            float Scale
        );

        readonly Func<Request, CancellationToken, Task<Color32[]>> start;
        readonly Action<Exception> report;
        Task<Color32[]> task;
        CancellationTokenSource cancellation;
        Request running;
        Request? failed;

        internal CrtVignetteWork(
            Action<Exception> report,
            Func<Request, CancellationToken, Task<Color32[]>> start = null
        )
        {
            this.report = report;
            this.start =
                start
                ?? (
                    (request, token) =>
                        Task.Run(
                            () =>
                                CrtVignettePixels.Build(
                                    request.Width,
                                    request.Height,
                                    request.Corner,
                                    request.Scanline,
                                    request.Scale,
                                    token
                                ),
                            token
                        )
                );
        }

        internal bool TryGet(Request request, out Color32[] pixels)
        {
            pixels = null;
            if (task != null)
            {
                if (running != request)
                    cancellation.Cancel();
                if (!task.IsCompleted)
                    return false;
                try
                {
                    var result = task.GetAwaiter().GetResult();
                    if (running == request && !cancellation.IsCancellationRequested)
                        pixels = result;
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    if (running == request)
                        failed = request;
                    report(e);
                }
                finally
                {
                    task = null;
                    cancellation.Dispose();
                    cancellation = null;
                }
                if (pixels != null)
                    return true;
            }
            if (failed == request)
                return false;
            running = request;
            cancellation = new CancellationTokenSource();
            try
            {
                task = start(request, cancellation.Token);
            }
            catch
            {
                cancellation.Dispose();
                cancellation = null;
                throw;
            }
            return false;
        }

        internal void Cancel()
        {
            failed = null;
            if (task == null)
                return;
            var retiredCancellation = cancellation;
            retiredCancellation.Cancel();
            _ = task.ContinueWith(
                t =>
                {
                    _ = t.Exception;
                    retiredCancellation.Dispose();
                },
                TaskScheduler.Default
            );
            task = null;
            cancellation = null;
        }

        public void Dispose() => Cancel();
    }
}
