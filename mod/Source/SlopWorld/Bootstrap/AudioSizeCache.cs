using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SlopWorld
{
    // Disposable performance data: corrupt/missing/unwritable caches never prevent native
    // probing. Callers supply a versioned source identity and only record successful probes.
    internal sealed class AudioSizeCache
    {
        const int MaxEntries = 32768;
        readonly object gate = new object();
        readonly object writer = new object();
        readonly string path;
        readonly Action<Exception> report;
        Dictionary<string, long> sizes = new(StringComparer.Ordinal);
        long revision;
        long savedRevision;

        internal AudioSizeCache(string path, Action<Exception> report)
        {
            this.path = path;
            this.report = report;
            try
            {
                if (!File.Exists(path))
                    return;
                if (new FileInfo(path).Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("Audio size cache is too large.");
                var loaded = JsonSerializer.Deserialize<Dictionary<string, long>>(
                    File.ReadAllText(path)
                );
                if (loaded == null || loaded.Count > MaxEntries)
                    throw new InvalidDataException("Invalid audio size cache.");
                foreach (var pair in loaded)
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value > 0)
                        sizes[pair.Key] = pair.Value;
            }
            catch (Exception e)
                when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                report(e);
            }
        }

        internal bool TryGet(string key, out long size)
        {
            size = 0;
            if (key == null)
                return false;
            lock (gate)
                return sizes.TryGetValue(key, out size);
        }

        internal void Record(string key, long size)
        {
            if (key == null || size <= 0)
                return;
            lock (gate)
            {
                if (sizes.TryGetValue(key, out long previous) && previous == size)
                    return;
                if (sizes.Count >= MaxEntries)
                    sizes.Clear();
                sizes[key] = size;
                revision++;
            }
        }

        internal void Flush()
        {
            // Serialize writers, but never hold the lookup lock across disk I/O. Unique
            // temporary files also make simultaneous diagnostic/game processes safe.
            lock (writer)
            {
                Dictionary<string, long> snapshot;
                long version;
                lock (gate)
                {
                    if (revision == savedRevision)
                        return;
                    snapshot = new Dictionary<string, long>(sizes, StringComparer.Ordinal);
                    version = revision;
                }
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                    File.Move(temporary, path, true);
                    lock (gate)
                        savedRevision = version;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    report(e);
                }
                finally
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
