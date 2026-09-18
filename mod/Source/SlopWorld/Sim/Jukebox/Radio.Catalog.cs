using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace SlopWorld
{
    // The station catalog and the nested types that describe one station. The daemon owns the
    // catalog and sends metadata and stable stream keys, never URLs; the mod selects an opaque
    // station key. Kept beside the playback core in its own partial so the wire logic stays read.
    public static partial class Radio
    {
        // The part of a station the UI will eventually use in addition to its streams. Keep
        // donation links here, beside the name, rather than throwing them away in the loader.
        public sealed class Metadata
        {
            public readonly string Name;
            public readonly string Donate;
            public readonly string TitleRegex;

            internal Metadata(string name, string donate, string titleRegex)
            {
                Name = name;
                Donate = donate ?? "";
                TitleRegex = titleRegex ?? "";
            }
        }

        // A station from the daemon's catalog: metadata and the qualities it serves. URLs stay
        // out of selection messages; only stable ids and stream keys cross that boundary.
        public sealed class Station
        {
            public readonly string Id;
            public readonly Metadata Metadata;

            public string Name => Metadata.Name;

            // The qualities this one answers on. One entry is a whole list, and the menu
            // is built the same way either way - see Jukebox.Presets.
            public readonly int[] Rates;

            readonly string[] _keys;
            readonly Regex _titleRegex;

            // The quality it was last left on, so a switch away and back comes up where it
            // was. Kept per station rather than as one number for the lot of them: the
            // lists do not overlap, so one station's quality is not assumed to work for another.
            public int Rate;

            internal Station(string id, string name, string donate, string titleRegex,
                             int[] rates, string[] keys, int defaultRate)
            {
                Id = id;
                Metadata = new Metadata(name, donate, titleRegex);
                Rates = rates;
                _keys = keys;
                Rate = defaultRate;

                if (string.IsNullOrEmpty(Metadata.TitleRegex)) return;
                try
                {
                    _titleRegex = new Regex(Metadata.TitleRegex,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException e)
                {
                    Log.Warning($"[SlopWorld] jukebox: invalid title regex for {Id}: {e.Message}");
                }
            }

            // The stream's stable key at a rate, as supplied by the user definition.
            // It gives the setting a human-editable tail.
            public string Path(int rate)
            {
                int index = Find(rate);
                return index < 0 ? null : _keys[index];
            }

            public string SelectionKey(int rate) => Id + ":" + Path(rate);

            internal string FormatTitle(string title)
            {
                string artist;
                string song;
                return TryTitleParts(title, out artist, out song)
                    ? artist + " - " + song
                    : title;
            }

            internal bool TryTitleParts(string title, out string artist, out string song)
            {
                artist = null;
                song = title;
                if (_titleRegex == null || string.IsNullOrEmpty(title)) return false;
                Match match = _titleRegex.Match(title);
                if (!match.Success) return false;

                Group artistGroup = match.Groups["artist"];
                Group titleGroup = match.Groups["title"];
                if (!artistGroup.Success || !titleGroup.Success) return false;

                artist = artistGroup.Value.Trim();
                song = titleGroup.Value.Trim();
                return !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(song);
            }

            int Find(int rate)
            {
                for (int i = 0; i < Rates.Length; i++)
                    if (Rates[i] == rate) return i;
                return -1;
            }
        }

        static Station[] _stations = new Station[0];
        // Before the first catalog, a null station can still be a saved radio selection.
        static bool _catalogReady;

        // Stations in daemon catalog order. The last catalog remains in memory while the
        // socket reconnects, so a temporary daemon restart does not empty an open menu.
        public static Station[] Stations => _stations;

        // The daemon sends metadata and stable keys, never stream URLs. Invalid entries are
        // ignored individually so one bad user definition cannot take the whole menu down.
        public static void SetStations(Wire.JukeboxCatalog catalog)
        {
            if (catalog == null) return;
            _catalogReady = true;

            string saved = null;
            if (_station != null) saved = _station.SelectionKey(_station.Rate);
            else if (_read) saved = Settings.Radio;

            var next = new List<Station>();
            foreach (var item in catalog.Stations)
            {
                try
                {
                    string id = item.Id;
                    if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("missing id");

                    var metadata = item.Metadata;
                    string name = metadata.Name;
                    string donate = metadata.Donate;
                    string titleRegex = metadata.TitleRegex;

                    var rates = new List<int>();
                    var keys = new List<string>();
                    foreach (var stream in item.Streams)
                    {
                        int rate = (int)stream.Rate;
                        string key = stream.Key;
                        if (rate <= 0 || string.IsNullOrEmpty(key))
                            throw new InvalidOperationException("stream needs a positive rate and key");
                        if (rates.Contains(rate) || keys.Contains(key))
                            throw new InvalidOperationException("duplicate stream rate or key");
                        rates.Add(rate);
                        keys.Add(key);
                    }
                    if (rates.Count == 0) throw new InvalidOperationException("no streams");

                    int defaultRate = (int)item.DefaultRate;
                    if (!rates.Contains(defaultRate))
                        throw new InvalidOperationException("default rate has no stream");

                    next.Add(new Station(id, name, donate, titleRegex,
                        rates.ToArray(), keys.ToArray(), defaultRate));
                }
                catch (Exception e)
                {
                    Log.Warning("[SlopWorld] jukebox: ignoring catalog station: " + e.Message);
                }
            }

            _stations = next.ToArray();
            if (!_read) return;

            _station = FindSelection(saved);
            string nextSelection = _station?.SelectionKey(_station.Rate);
            if (nextSelection == null && saved != null && saved != "ost")
            {
                Save();
                Push();
            }
            // This also covers the first catalog arriving after Read selected the OST
            // provisionally, and a reload that changes the URL behind the same stable key.
            if (nextSelection != null) Push();
        }

        static Station FindSelection(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return null;
            foreach (var station in Stations)
            {
                foreach (int rate in station.Rates)
                {
                    if (saved != station.SelectionKey(rate)) continue;
                    station.Rate = rate;
                    return station;
                }
            }
            return null;
        }
    }
}
