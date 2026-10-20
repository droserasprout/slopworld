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

        public bool IsUsable =>
            !string.IsNullOrEmpty(Artist)
            && !string.IsNullOrEmpty(Title)
            && !string.IsNullOrEmpty(Source);

        public string Display => IsUsable ? Artist + " - " + Title : null;

        public static NativeTrackSnapshot FromOstClipPath(string clipPath)
        {
            if (string.IsNullOrEmpty(clipPath))
                return null;
            int slash = Math.Max(clipPath.LastIndexOf('/'), clipPath.LastIndexOf('\\'));
            string title = slash >= 0 ? clipPath.Substring(slash + 1) : clipPath;
            return string.IsNullOrEmpty(title)
                ? null
                : new NativeTrackSnapshot("Terry Fail", title, "SlopWorld OST");
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

    // Capture all persisted fields without requiring complete daemon metadata.
    // Original station values remain separate from effective recognized/formatted values.
    public sealed class JukeboxLikeRecord
    {
        public readonly string At;
        public readonly string Source;
        public readonly string Artist;
        public readonly string Title;
        public readonly string OriginalArtist;
        public readonly string OriginalTitle;

        public JukeboxLikeRecord(
            DateTime utcNow,
            string source,
            string artist,
            string title,
            string originalArtist,
            string originalTitle
        )
        {
            At = utcNow
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            Source = source;
            Artist = artist;
            Title = title;
            OriginalArtist = originalArtist;
            OriginalTitle = originalTitle;
        }
    }

    // Own the shared [[like]] schema and file persistence without game dependencies.
    // Radio owns metadata eligibility; JukeboxActions owns player notifications.
    public static class JukeboxLikeWriter
    {
        public static bool TryAppend(
            string path,
            NativeTrackSnapshot track,
            DateTime utcNow,
            out NativeLikeRecord record,
            out string error
        )
        {
            record = null;
            error = null;
            if (track == null || !track.IsUsable)
            {
                error = "nothing is playing";
                return false;
            }

            var like = new JukeboxLikeRecord(
                utcNow,
                track.Source,
                track.Artist,
                track.Title,
                track.Artist,
                track.Title
            );
            if (!TryAppend(path, like, out Exception failure))
            {
                error = failure.Message;
                return false;
            }
            record = new NativeLikeRecord(track, like.At);
            return true;
        }

        public static bool TryAppend(string path, JukeboxLikeRecord record, out Exception error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(
                    path,
                    "[[like]]"
                        + Environment.NewLine
                        + "at = "
                        + Toml.Quote(record.At)
                        + Environment.NewLine
                        + "source = "
                        + Toml.Quote(record.Source)
                        + Environment.NewLine
                        + "artist = "
                        + Toml.Quote(record.Artist)
                        + Environment.NewLine
                        + "title = "
                        + Toml.Quote(record.Title)
                        + Environment.NewLine
                        + "original_artist = "
                        + Toml.Quote(record.OriginalArtist)
                        + Environment.NewLine
                        + "original_title = "
                        + Toml.Quote(record.OriginalTitle)
                        + Environment.NewLine
                        + Environment.NewLine
                );
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }
    }
}
