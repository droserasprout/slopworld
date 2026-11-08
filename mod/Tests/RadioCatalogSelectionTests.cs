using System;
using System.Reflection;

namespace SlopWorld.Tests
{
    static class RadioCatalogSelectionTests
    {
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        public static void FirstCatalogRestoresSavedNondefaultStreamAndPreservesMute()
        {
            WithRadioState(() =>
            {
                Settings.S.radio = "station:high";
                SetField("_read", true);
                Radio.MutePlaybackTest();
                int revision = Revision;

                Radio.SetStations(Catalog(Station()));

                AssertEx.True(Radio.CatalogReadyTest, "first snapshot is ready before restoration");
                AssertEx.Equal(
                    Radio.Stations[0],
                    Selected,
                    "saved key selects the published object"
                );
                AssertEx.Equal(128, Selected.Rate, "saved stream overrides the default rate");
                AssertEx.Equal(
                    "high",
                    Selected.Path(Selected.Rate),
                    "saved stream key is restored"
                );
                AssertEx.True((bool)GetField("_muted"), "catalog restoration preserves mute");
                AssertEx.Equal(revision + 1, Revision, "restored selection invalidates playback");
            });
        }

        public static void ReloadPreservesLiveStreamByKeyAcrossRateAndMetadataChanges()
        {
            WithRadioState(() =>
            {
                SetField("_read", true);
                Radio.SetStations(Catalog(Station()));
                var old = Radio.Stations[0];
                Radio.SelectPlaybackTest(old, 128);
                Settings.S.radio = "unrelated:stale";
                int revision = Revision;
                var replacement = Station();
                replacement.Metadata.Name = "Renamed station";
                replacement.Streams[1].Rate = 256;

                Radio.SetStations(Catalog(replacement));

                AssertEx.False(
                    ReferenceEquals(old, Selected),
                    "reload replaces the station object"
                );
                AssertEx.Equal(Radio.Stations[0], Selected, "selection belongs to new snapshot");
                AssertEx.Equal("Renamed station", Selected.Name, "replacement metadata is visible");
                AssertEx.Equal(256, Selected.Rate, "stable key resolves the replacement rate");
                AssertEx.Equal(
                    "station:high",
                    Selected.SelectionKey(256),
                    "live key wins over settings"
                );
                AssertEx.Equal(revision + 1, Revision, "reload reapplies a retained stream");
            });
        }

        public static void ReloadFallsBackWhenSelectedStreamDisappearsEvenIfStationRemains()
        {
            WithRadioState(() =>
            {
                SetField("_read", true);
                Radio.SetStations(Catalog(Station()));
                Radio.SelectPlaybackTest(Radio.Stations[0], 128);
                var replacement = Station();
                replacement.Streams.RemoveAt(1);
                int revision = Revision;

                Radio.SetStations(Catalog(replacement));

                AssertEx.Equal(1, Radio.Stations.Length, "station remains available in the menu");
                AssertEx.Equal(
                    null,
                    Selected,
                    "missing stream falls back rather than changing rate"
                );
                AssertEx.Equal(revision + 1, Revision, "fallback invalidates previous playback");
            });
        }

        public static void MissingOrEmptySavedSelectionFallsBackButOstNeedsNoPush()
        {
            foreach (string saved in new[] { "", "ost", "missing:stream" })
            {
                WithRadioState(() =>
                {
                    Settings.S.radio = saved;
                    SetField("_read", true);
                    int revision = Revision;

                    Radio.SetStations(Catalog(Station()));

                    AssertEx.Equal(null, Selected, "unmatched selection leaves OST active");
                    AssertEx.Equal(
                        revision + (saved == "ost" ? 0 : 1),
                        Revision,
                        "missing or empty station selection requires a fallback push"
                    );
                });
            }
        }

        public static void CatalogBeforeSettingsLoadDoesNotRestoreOrInvalidatePlayback()
        {
            WithRadioState(() =>
            {
                Settings.S.radio = "station:high";
                int revision = Revision;

                Radio.SetStations(Catalog(Station()));

                AssertEx.True(
                    Radio.CatalogReadyTest,
                    "catalog still publishes before settings load"
                );
                AssertEx.Equal(null, Selected, "unread settings do not restore a selection");
                AssertEx.Equal(revision, Revision, "publishing alone does not invalidate playback");
            });
        }

        public static void CatalogReloadLeavesSpotifySelectionAndPlaybackAlone()
        {
            WithRadioState(() =>
            {
                Settings.S.radio = "station:high";
                SetField("_read", true);
                Radio.SelectPlaybackTest(spotify: true);
                int revision = Revision;

                Radio.SetStations(Catalog(Station()));

                AssertEx.Equal(
                    1,
                    Radio.Stations.Length,
                    "Spotify does not prevent menu publication"
                );
                AssertEx.True(Radio.SpotifyRequested, "catalog does not replace Spotify");
                AssertEx.Equal(null, Selected, "saved station cannot displace Spotify");
                AssertEx.Equal(revision, Revision, "catalog does not interrupt Spotify playback");
            });
        }

        public static void MissingCatalogRetainsSnapshotAndSelectionUntilEmptyReplacement()
        {
            WithRadioState(() =>
            {
                SetField("_read", true);
                Radio.SetStations(null);
                AssertEx.False(Radio.CatalogReadyTest, "missing first catalog stays unready");
                Radio.SetStations(Catalog(Station()));
                Radio.SelectPlaybackTest(Radio.Stations[0], 128);
                var snapshot = Radio.Stations;
                var selected = Selected;
                int revision = Revision;

                Radio.SetStations(null);

                AssertEx.Equal(snapshot, Radio.Stations, "disconnect retains the snapshot");
                AssertEx.Equal(selected, Selected, "disconnect retains the selected object");
                AssertEx.Equal(revision, Revision, "disconnect does not reapply playback");
                Radio.SetStations(new Wire.JukeboxCatalog());
                AssertEx.Equal(0, Radio.Stations.Length, "explicit empty snapshot clears stations");
                AssertEx.Equal(null, Selected, "removed station falls back");
                AssertEx.Equal(revision + 1, Revision, "removal invalidates playback");
            });
        }

        public static void InvalidStationVariantsDoNotDiscardValidNeighbors()
        {
            Action<Wire.Station>[] corruptions =
            {
                station => station.Id = "",
                station => station.Metadata = null,
                station => station.Streams.Clear(),
                station => station.Streams[0].Rate = 0,
                station => station.Streams[0].Key = "",
                station => station.Streams[1].Rate = station.Streams[0].Rate,
                station => station.Streams[1].Key = station.Streams[0].Key,
                station => station.DefaultRate = 999,
            };
            foreach (var corrupt in corruptions)
            {
                WithRadioState(() =>
                {
                    var invalid = Station("invalid");
                    corrupt(invalid);

                    Radio.SetStations(Catalog(Station("before"), invalid, Station("after")));

                    AssertEx.True(Radio.CatalogReadyTest, "bad row does not prevent publication");
                    AssertEx.Equal(2, Radio.Stations.Length, "only invalid row is discarded");
                    AssertEx.Equal("before", Radio.Stations[0].Id, "preceding station keeps order");
                    AssertEx.Equal("after", Radio.Stations[1].Id, "following station keeps order");
                });
            }
        }

        public static void StationConversionRetainsMetadataAndRateKeyMapping()
        {
            WithRadioState(() =>
            {
                var wire = Station();
                wire.Metadata.Donate = "https://example.com/donate";
                wire.Metadata.TitleRegex = "^(?<artist>.+)/(?<title>.+)$";
                Radio.SetStations(Catalog(wire));
                var station = Radio.Stations[0];

                AssertEx.Equal("Station", station.Name, "name survives conversion");
                AssertEx.Equal(
                    wire.Metadata.Donate,
                    station.Metadata.Donate,
                    "donation is retained"
                );
                AssertEx.Equal(
                    wire.Metadata.TitleRegex,
                    station.Metadata.TitleRegex,
                    "pattern is retained"
                );
                AssertEx.Equal(64, station.Rate, "default rate is applied");
                AssertEx.Equal(64, station.Rates[0], "first rate retains catalog order");
                AssertEx.Equal(128, station.Rates[1], "second rate retains catalog order");
                AssertEx.Equal("low", station.Path(64), "first key matches its rate");
                AssertEx.Equal("high", station.Path(128), "second key matches its rate");
                AssertEx.Equal(null, station.Path(999), "unknown rate has no stream");
                AssertEx.Equal(
                    "Artist - Song",
                    station.FormatTitle(" Artist / Song "),
                    "converted pattern is active"
                );
            });
        }

        public static void TitleParsingRejectsAbsentOrEmptyNamedPartsAndPreservesRawText()
        {
            foreach (
                string pattern in new[]
                {
                    "",
                    "[",
                    "^(.*)/(.*)$",
                    "^(?<artist>.+)/.+$",
                    "^.+/(?<title>.+)$",
                    "^(?<artist> *)/(?<title>.*)$",
                    "^(?<artist>.*)/(?<title> *)$",
                    "^unmatched$",
                }
            )
            {
                var station = TitleStation(pattern);
                foreach (string raw in new[] { null, "", " / ", "Artist / Song" })
                {
                    AssertEx.False(
                        station.TryTitleParts(raw, out _, out _),
                        "incomplete match is not a recognized song"
                    );
                    AssertEx.Equal(
                        raw,
                        station.FormatTitle(raw),
                        "unrecognized metadata is preserved exactly"
                    );
                }
            }
        }

        public static void TitleParsingTrimsNamedPartsAndMatchesWithoutCaseSensitivity()
        {
            var station = TitleStation("^artist: (?<artist>.+); title: (?<title>.+)$");
            const string raw = "ARTIST:  Performer ; TITLE:  Song  ";
            AssertEx.True(
                station.TryTitleParts(raw, out string artist, out string title),
                "named parts match case-insensitively"
            );
            AssertEx.Equal("Performer", artist, "artist is trimmed");
            AssertEx.Equal("Song", title, "title is trimmed");
            AssertEx.Equal(
                "Performer - Song",
                station.FormatTitle(raw),
                "formatted metadata uses both parts"
            );
        }

        static Radio.Station TitleStation(string pattern) =>
            new Radio.Station("title", "Title", null, pattern, new[] { 64 }, new[] { "low" }, 64);

        static Wire.Station Station(string id = "station")
        {
            var station = new Wire.Station
            {
                Id = id,
                Metadata = new Wire.StationMetadata { Name = "Station" },
                DefaultRate = 64,
            };
            station.Streams.Add(new Wire.StationStream { Rate = 64, Key = "low" });
            station.Streams.Add(new Wire.StationStream { Rate = 128, Key = "high" });
            return station;
        }

        static Wire.JukeboxCatalog Catalog(params Wire.Station[] stations)
        {
            var catalog = new Wire.JukeboxCatalog();
            catalog.Stations.Add(stations);
            return catalog;
        }

        // The harness substitutes Read/Save. Set only the loaded-settings precondition;
        // all catalog conversion, restoration and playback invalidation run production code.
        static void SetField(string name, object value) =>
            typeof(Radio).GetField(name, PrivateStatic).SetValue(null, value);

        static object GetField(string name) =>
            typeof(Radio).GetField(name, PrivateStatic).GetValue(null);

        static Radio.Station Selected => (Radio.Station)GetField("_station");
        static int Revision => (int)GetField("_selectionRevision");

        static void WithRadioState(Action test)
        {
            string saved = Settings.S.radio;
            Radio.ResetSpotifyTest();
            Settings.S.radio = "ost";
            try
            {
                test();
            }
            finally
            {
                Radio.ResetSpotifyTest();
                Settings.S.radio = saved;
            }
        }
    }
}
