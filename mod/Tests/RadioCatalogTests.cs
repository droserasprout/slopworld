using System;

namespace SlopWorld.Tests
{
    static class RadioCatalogTests
    {
        public static void SlowTitleRegexRetainsRawMetadata()
        {
            var station = new Radio.Station("slow", "Slow station", "", "(?<artist>(a+)+)$",
                new[] { 128 }, new[] { "main" }, 128);
            string raw = new string('a', 10000) + "!";
            AssertEx.Equal(raw, station.FormatTitle(raw), "regex timeout retains the raw title");
            AssertEx.False(station.TryTitleParts(raw, out string artist, out string title), "timeout produces no recognized parts");
            AssertEx.Equal(null, artist, "timeout leaves artist unknown");
            AssertEx.Equal(raw, title, "timeout preserves the original metadata");
        }

        public static void ValidTitleRegexStillFormatsNamedParts()
        {
            var station = new Radio.Station("valid", "Valid station", "", "^(?<artist>.+)/(?<title>.+)$",
                new[] { 128 }, new[] { "main" }, 128);
            AssertEx.Equal("Artist - Song", station.FormatTitle(" Artist / Song "), "named groups are trimmed and formatted");
            AssertEx.Equal("raw", station.FormatTitle("raw"), "unmatched title is preserved");
        }

        public static void CatalogSkipsInvalidStationsAndPublishesValidReplacement()
        {
            Radio.ResetSpotifyTest();
            var catalog = new Wire.JukeboxCatalog();
            catalog.Stations.Add(new Wire.Station { Id = "invalid" });
            var valid = new Wire.Station
            {
                Id = "valid",
                Metadata = new Wire.StationMetadata { Name = "Valid station" },
                DefaultRate = 128,
            };
            valid.Streams.Add(new Wire.StationStream { Rate = 128, Key = "main" });
            catalog.Stations.Add(valid);
            Radio.SetStations(catalog);
            AssertEx.True(Radio.CatalogReadyTest, "completed snapshot marks the catalog ready");
            AssertEx.Equal(1, Radio.Stations.Length, "bad station does not discard good rows");
            AssertEx.Equal("valid:main", Radio.Stations[0].SelectionKey(128), "stream identity survives conversion");
            Radio.SetStations(null);
            AssertEx.Equal(1, Radio.Stations.Length, "missing catalog retains last snapshot");
            Radio.SetStations(new Wire.JukeboxCatalog());
            AssertEx.Equal(0, Radio.Stations.Length, "empty replacement clears the snapshot");
        }
    }
}
