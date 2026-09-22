using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public enum MountMode { None, Ro, Rw, Cache }

    public class MountEntry
    {
        public string From = "";
        public string To = "";
        public readonly string FieldId = System.Guid.NewGuid().ToString("N");
        public MountMode Mode = MountMode.Rw;

        public static MountMode ParseMode(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case WireProtocol.MountMode.Ro: return MountMode.Ro;
                case WireProtocol.MountMode.Rw: return MountMode.Rw;
                case WireProtocol.MountMode.Cache: return MountMode.Cache;
                default: return MountMode.Rw;
            }
        }

        public static string ModeName(MountMode mode) =>
            mode == MountMode.Cache ? WireProtocol.MountMode.Cache :
            mode == MountMode.Ro ? WireProtocol.MountMode.Ro : WireProtocol.MountMode.Rw;

        public static string ModeLabel(MountMode mode) =>
            mode == MountMode.Cache ? "Cache" : mode == MountMode.None ? "None" : mode == MountMode.Ro ? "Read-only" : "Read-write";

        public static MountEntry FromWire(Wire.Mount j) => new MountEntry
        {
            From = j.From,
            To = j.To,
            Mode = ParseMode(j.Mode),
        };
        public static List<MountEntry> ListFromWire(IEnumerable<Wire.Mount> mounts) =>
            mounts.Select(FromWire).ToList();
        public Wire.Mount ToWire() => new Wire.Mount { From = From, To = To, Mode = ModeName(Mode) };
    }
}
