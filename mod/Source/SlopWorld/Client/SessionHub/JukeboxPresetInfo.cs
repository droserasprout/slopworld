using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Complete user-owned station definitions used only by the Settings editor. Radio's live
    // catalog intentionally keeps URLs out of its client model.
    public sealed class JukeboxPresetInfo
    {
        public string Id = "";
        public string Name = "";
        public string Donate = "";
        public string TitleRegex = "";
        public int DefaultRate;
        public List<JukeboxStreamInfo> Streams = new List<JukeboxStreamInfo>();

        public JukeboxPresetInfo Copy() => new JukeboxPresetInfo
        {
            Id = Id,
            Name = Name,
            Donate = Donate,
            TitleRegex = TitleRegex,
            DefaultRate = DefaultRate,
            Streams = (Streams ?? new List<JukeboxStreamInfo>()).Select(s => s.Copy()).ToList(),
        };

        public Wire.Station ToWire()
        {
            var station = new Wire.Station
            {
                Id = Id ?? "",
                DefaultRate = (uint)Math.Max(0, DefaultRate),
                Metadata = new Wire.StationMetadata
                {
                    Name = Name ?? "",
                    Donate = Donate ?? "",
                    TitleRegex = TitleRegex ?? "",
                },
            };
            foreach (var stream in Streams ?? new List<JukeboxStreamInfo>())
                station.Streams.Add(new Wire.StationStream
                {
                    Rate = (uint)Math.Max(0, stream.Rate),
                    Key = stream.Key ?? "",
                    Url = stream.Url ?? "",
                });
            return station;
        }

        public static JukeboxPresetInfo FromWire(Wire.Station station)
        {
            var metadata = station.Metadata ?? new Wire.StationMetadata();
            return new JukeboxPresetInfo
            {
                Id = station.Id,
                Name = metadata.Name,
                Donate = metadata.Donate,
                TitleRegex = metadata.TitleRegex,
                DefaultRate = (int)station.DefaultRate,
                Streams = station.Streams.Select(stream => new JukeboxStreamInfo
                {
                    Rate = (int)stream.Rate,
                    Key = stream.Key,
                    Url = stream.Url,
                }).ToList(),
            };
        }

        public static string NewId(IEnumerable<JukeboxPresetInfo> existing)
        {
            var names = new HashSet<string>((existing ?? Enumerable.Empty<JukeboxPresetInfo>())
                .Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            string baseName = "new-source";
            string id = baseName;
            int suffix = 2;
            while (names.Contains(id)) id = baseName + "-" + suffix++;
            return id;
        }
    }

    public sealed class JukeboxStreamInfo
    {
        public int Rate;
        public string Key = "";
        public string Url = "";

        public JukeboxStreamInfo Copy() => new JukeboxStreamInfo
        {
            Rate = Rate,
            Key = Key,
            Url = Url,
        };
    }

    public static class JukeboxPresetStore
    {
        public static readonly List<JukeboxPresetInfo> Items = new List<JukeboxPresetInfo>();
        public static bool Loading { get; private set; }
        public static string Error { get; private set; }

        public static JukeboxPresetInfo Find(string id) =>
            Items.FirstOrDefault(p => p.Id == id);

        public static void Refresh(Action ok = null, Action<string> fail = null)
        {
            Loading = true;
            Error = null;
            DaemonClient.Get<Wire.JukeboxCatalog>(WireProtocol.Routes.JukeboxPresets,
                catalog =>
                {
                    Loading = false;
                    Items.Clear();
                    Items.AddRange(catalog.Stations.Select(JukeboxPresetInfo.FromWire));
                    ok?.Invoke();
                },
                error =>
                {
                    Loading = false;
                    Error = error;
                    fail?.Invoke(error);
                });
        }

        public static void Save(JukeboxPresetInfo preset, bool isNew, string originalId,
                                Action ok, Action<string> fail)
        {
            Action<Wire.Ack> done = _ => Refresh(ok, fail);
            if (isNew)
                DaemonClient.Post(WireProtocol.Routes.JukeboxPresets, preset.ToWire(), done, fail);
            else
                DaemonClient.Put(WireProtocol.Routes.JukeboxPresets + "/" +
                    Uri.EscapeDataString(originalId ?? preset.Id), preset.ToWire(), done, fail);
        }

        public static void Remove(string id, Action ok, Action<string> fail)
        {
            DaemonClient.Delete(WireProtocol.Routes.JukeboxPresets + "/" +
                Uri.EscapeDataString(id), _ => Refresh(ok, fail), fail);
        }
    }
}
