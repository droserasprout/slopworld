using System;

namespace SlopWorld
{
    // A playback plan captures source identity and its payload together. Dispatch never
    // rereads mutable selection state after deciding which source should be sent.
    internal readonly struct RadioPlaybackPlan
    {
        internal enum SourceKind { Stop, Spotify, Station, File }
        internal readonly SourceKind Kind;
        internal readonly string Id, Path;
        internal string Identity => Kind == SourceKind.Station ? "station:" + Id + ":" + Path
            : Kind == SourceKind.File ? "file:" + Path : Kind == SourceKind.Spotify ? "ncspot" : "stop";
        RadioPlaybackPlan(SourceKind kind, string id = null, string path = null)
        { Kind = kind; Id = id; Path = path; }
        internal static RadioPlaybackPlan Stop => new RadioPlaybackPlan(SourceKind.Stop);
        internal static RadioPlaybackPlan Spotify => new RadioPlaybackPlan(SourceKind.Spotify);
        internal static RadioPlaybackPlan Station(string id, string stream) => id == null || stream == null
            ? Stop : new RadioPlaybackPlan(SourceKind.Station, id, stream);
        internal static RadioPlaybackPlan File(string path) => path == null
            ? Stop : new RadioPlaybackPlan(SourceKind.File, path: path);
    }

    // Own send cadence and reconnect deduplication. Native music, persistence, reporting
    // and socket effects stay with Radio's adapters. Call on the main thread.
    internal sealed class RadioPlayback
    {
        internal const double UpdateInterval = 1.0 / 6.0;
        internal enum UpdateKind { None, Selection, Volume }
        PeriodicWork _steady;
        bool _told;
        string _sent;
        float _volume = -1f;

        internal void Invalidate() => _told = false;

        internal UpdateKind Step(double now, bool online, Func<RadioPlaybackPlan> resolvePlan,
            float volume, out RadioPlaybackPlan plan)
        {
            plan = default;
            if (!online)
            {
                _told = false;
                _volume = -1f;
                return UpdateKind.None;
            }
            if (!_told)
            {
                _steady.Delay(now, UpdateInterval);
                plan = resolvePlan();
                return Select(plan, volume);
            }
            if (!_steady.Due(now, UpdateInterval)) return UpdateKind.None;
            plan = resolvePlan();
            if (plan.Identity != _sent) return Select(plan, volume);
            if (Math.Abs(volume - _volume) < 0.01f) return UpdateKind.None;
            _volume = volume;
            return UpdateKind.Volume;
        }

        UpdateKind Select(RadioPlaybackPlan plan, float volume)
        {
            _told = true;
            _sent = plan.Identity;
            _volume = volume;
            return UpdateKind.Selection;
        }
    }
}
