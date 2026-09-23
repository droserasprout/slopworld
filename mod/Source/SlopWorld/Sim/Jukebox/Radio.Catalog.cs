using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace SlopWorld
{
    // Describe stations from the daemon catalog.
    // The daemon sends metadata and stable stream keys without URLs. The mod selects stations by key.
    public static partial class Radio
    {
        // Retain station metadata, including donation links, for UI use.
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

        // Store station metadata and available stream rates.
        // Selection messages contain stable identifiers and stream keys without URLs.
        public sealed class Station
        {
            public readonly string Id;
            public readonly Metadata Metadata;

            public string Name => Metadata.Name;

            // Available stream rates. Jukebox.Presets uses the same menu logic for one or multiple rates.
            public readonly int[] Rates;

            readonly string[] _keys;
            readonly Regex _titleRegex;

            // Remember the selected rate for each station. Different stations can offer different rates.
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

            // Return the stable stream key for a rate. User definitions supply these keys.
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
        // Before the first catalog arrives, a saved radio selection can have no matching station object.
        static bool _catalogReady;

        // Return stations in daemon catalog order.
        // Retain the previous catalog during reconnection so a daemon restart does not empty the menu.
        public static Station[] Stations => _stations;

        // Load metadata and stable stream keys. Skip invalid stations individually so other stations remain available.
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
            if (!_read || _spotify) return;

            _station = FindSelection(saved);
            string nextSelection = _station?.SelectionKey(_station.Rate);
            if (nextSelection == null && saved != null && saved != "ost")
            {
                Save();
                Push();
            }
            // Apply the selection when the first catalog replaces the temporary OST choice.
            // Also apply it after a reload changes the URL for an existing key.
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
