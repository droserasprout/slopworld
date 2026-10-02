using System;
using System.Globalization;
using System.IO;

namespace SlopWorld
{
    // Capture one native music track.
    // Keep its fields together so confirmation text and the saved record identify the same track.
    public sealed class NativeTrackSnapshot
    {
        public readonly string Artist;
        public readonly string Title;
        public readonly string Source;

        public NativeTrackSnapshot(string artist, string title, string source)
        {
            Artist = artist;
            Title = title;
            Source = source;
        }

        public bool IsUsable => !string.IsNullOrEmpty(Artist)
            && !string.IsNullOrEmpty(Title)
            && !string.IsNullOrEmpty(Source);

        public string Display => IsUsable ? Artist + " - " + Title : null;

        public static NativeTrackSnapshot FromOstClipPath(string clipPath)
        {
            if (string.IsNullOrEmpty(clipPath)) return null;
            int slash = Math.Max(clipPath.LastIndexOf('/'), clipPath.LastIndexOf('\\'));
            string title = slash >= 0 ? clipPath.Substring(slash + 1) : clipPath;
            return string.IsNullOrEmpty(title)
                ? null : new NativeTrackSnapshot("Terry Fail", title, "SlopWorld OST");
        }
    }

    public sealed class NativeLikeRecord
    {
        public readonly NativeTrackSnapshot Track;
        public readonly string At;

        internal NativeLikeRecord(NativeTrackSnapshot track, string at)
        {
            Track = track;
            At = at;
        }

        public string Display => Track.Display;
    }

    // Write likes for native playback without game dependencies.
    // Use the existing [[like]] schema, including original_* fields, to share the file with daemon playback.
    public static class JukeboxLikeWriter
    {
        public static bool TryAppend(string path, NativeTrackSnapshot track, DateTime utcNow,
                                      out NativeLikeRecord record, out string error)
        {
            record = null;
            error = null;
            if (track == null || !track.IsUsable)
            {
                error = "nothing is playing";
                return false;
            }

            string at = utcNow.ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(path,
                    "[[like]]" + Environment.NewLine +
                    "at = " + Toml.Quote(at) + Environment.NewLine +
                    "source = " + Toml.Quote(track.Source) + Environment.NewLine +
                    "artist = " + Toml.Quote(track.Artist) + Environment.NewLine +
                    "title = " + Toml.Quote(track.Title) + Environment.NewLine +
                    "original_artist = " + Toml.Quote(track.Artist) + Environment.NewLine +
                    "original_title = " + Toml.Quote(track.Title) + Environment.NewLine +
                    Environment.NewLine);
                record = new NativeLikeRecord(track, at);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
