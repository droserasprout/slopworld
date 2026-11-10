using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    internal static partial class MenuBackgroundBake
    {
        // Owns an unpublished frame set. Step/Take/Dispose are game-thread operations;
        // workers receive only paths, byte/pixel arrays and immutable numeric parameters.
        internal sealed class PreparedFrames : IDisposable
        {
            readonly CancellationTokenSource cancellation = new();
            readonly IEnumerator<bool> steps;
            readonly Stopwatch timer = Stopwatch.StartNew();
            Task worker;
            Texture2D[] result;
            bool disposed;
            internal bool Baked { get; private set; }
            internal long ElapsedMs => timer.ElapsedMilliseconds;
            internal long WorkingSetBefore { get; } = MenuBackgroundMemory.WorkingSet();

            internal PreparedFrames(Texture2D source, string key, MenuBackgroundPreset preset)
            {
                steps = Prepare(source, key, preset).GetEnumerator();
            }

            // At most one decode/upload per step, and the caller permits one step per frame.
            internal bool Step() => !steps.MoveNext();

            internal Texture2D[] Take()
            {
                var frames =
                    result
                    ?? throw new InvalidOperationException("Background frames are not ready.");
                result = null;
                return frames;
            }

            IEnumerable<bool> Prepare(Texture2D source, string key, MenuBackgroundPreset preset)
            {
                string directory = Dir(key);
                int total = preset.Total;
                var token = cancellation.Token;
                var frames = new Texture2D[total];
                try
                {
                    var read = Task.Run(() => ReadCache(directory, total, token), token);
                    worker = read;
                    while (!read.IsCompleted)
                        yield return true;
                    var cached = read.GetAwaiter().GetResult();
                    if (cached != null)
                    {
                        for (int i = 0; i < total; i++)
                        {
                            frames[i] = DecodeFrame(cached[i]);
                            if (frames[i] == null)
                            {
                                // A corrupt/partial cache never publishes a half-decoded set.
                                DestroyFrames(frames);
                                Array.Clear(frames);
                                cached = null;
                                break;
                            }
                            yield return true;
                        }
                    }
                    if (cached == null)
                    {
                        Baked = true;
                        int width = source.width,
                            height = source.height;
                        float scale = Mathf.Min(1f, (float)MaxSide / Mathf.Max(width, height));
                        int bw = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                        int bh = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                        // Native readback is required only for a genuine cache miss. Keep it
                        // on the game thread; downsampling, fuel and each stage are pure math.
                        var original = TextureReadback.ReadBack(source);
                        var prepare = Task.Run(
                            () =>
                            {
                                token.ThrowIfCancellationRequested();
                                var clean = Downsample(original, width, height, bw, bh);
                                var fuel = Fuel(clean, bw, bh);
                                token.ThrowIfCancellationRequested();
                                return (Clean: clean, Fuel: fuel);
                            },
                            token
                        );
                        worker = prepare;
                        while (!prepare.IsCompleted)
                            yield return true;
                        var data = prepare.GetAwaiter().GetResult();
                        for (int i = 0; i < total; i++)
                        {
                            int stage = i;
                            var compute = Task.Run(
                                () =>
                                {
                                    token.ThrowIfCancellationRequested();
                                    var pixels = new Color[bw * bh];
                                    Stage(preset, data.Clean, pixels, data.Fuel, bw, bh, stage);
                                    token.ThrowIfCancellationRequested();
                                    return pixels;
                                },
                                token
                            );
                            worker = compute;
                            while (!compute.IsCompleted)
                                yield return true;
                            var pixels = compute.GetAwaiter().GetResult();
                            var encoded = EncodeFrame(pixels, bw, bh);
                            frames[i] = encoded.Texture;
                            byte[] bytes = encoded.Bytes;
                            var write = Task.Run(
                                () => WriteFrame(directory, stage, bytes, token),
                                token
                            );
                            worker = write;
                            // Always yield after the native work, even if the write is fast.
                            do
                            {
                                yield return true;
                            } while (!write.IsCompleted);
                            write.GetAwaiter().GetResult();
                        }
                    }
                    result = frames;
                    frames = null;
                    timer.Stop();
                    worker = Task.Run(() =>
                    {
                        try
                        {
                            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        Sweep(key);
                    });
                }
                finally
                {
                    DestroyFrames(frames);
                }
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                cancellation.Cancel();
                steps.Dispose();
                DestroyFrames(result);
                result = null;
                var retiredCancellation = cancellation;
                if (worker == null)
                    retiredCancellation.Dispose();
                else
                    _ = worker.ContinueWith(
                        t =>
                        {
                            _ = t.Exception;
                            retiredCancellation.Dispose();
                        },
                        TaskScheduler.Default
                    );
            }
        }

        static byte[][] ReadCache(string directory, int total, CancellationToken token)
        {
            try
            {
                var bytes = new byte[total][];
                for (int i = 0; i < total; i++)
                {
                    token.ThrowIfCancellationRequested();
                    bytes[i] = File.ReadAllBytes(Path.Combine(directory, $"{i:D2}.jpg"));
                }
                return bytes;
            }
            catch (IOException)
            {
                return null;
            }
        }

        static void WriteFrame(string directory, int index, byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{index:D2}.jpg");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        static Texture2D DecodeFrame(byte[] bytes)
        {
#pragma warning disable CA2000 // The returned texture transfers to the unpublished set; failure destroys it here.
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
#pragma warning restore CA2000
            try
            {
                if (!LoadFrame(texture, bytes))
                    return null;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                Keep(texture);
                var ready = texture;
                texture = null;
                return ready;
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }
        }

        static (Texture2D Texture, byte[] Bytes) EncodeFrame(Color[] pixels, int width, int height)
        {
#pragma warning disable CA2000 // Success transfers ownership to the set; failure destroys it here.
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
#pragma warning restore CA2000
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                byte[] bytes = texture.EncodeToJPG(JpegQuality);
                if (!LoadFrame(texture, bytes))
                    throw new InvalidDataException("Could not reload a baked background JPEG.");
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                Keep(texture);
                var ready = texture;
                texture = null;
                return (ready, bytes);
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
