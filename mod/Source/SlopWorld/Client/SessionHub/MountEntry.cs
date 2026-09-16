using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public enum MountMode { None, Ro, Rw }

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
                default: return MountMode.Rw;
            }
        }

        public static string ModeName(MountMode mode) =>
            mode == MountMode.Ro ? WireProtocol.MountMode.Ro : WireProtocol.MountMode.Rw;

        public static string ModeLabel(MountMode mode) =>
            mode == MountMode.None ? "None" : mode == MountMode.Ro ? "Read-only" : "Read-write";

        public static MountEntry FromJson(JVal j) => new MountEntry
        {
            From = j["from"].AsString(),
            To = j["to"].AsString(),
            Mode = ParseMode(j["mode"].AsString(WireProtocol.MountMode.Rw)),
        };

        public static List<MountEntry> ListFromJson(JVal j) =>
            j.Items.Select(FromJson).ToList();

        public string ToJson() =>
            $"{{\"from\":{JVal.Q(From)},\"to\":{JVal.Q(To)},\"mode\":{JVal.Q(ModeName(Mode))}}}";

        public static string ListToJson(List<MountEntry> mounts) =>
            "[" + string.Join(",", mounts.Select(m => m.ToJson()).ToArray()) + "]";
    }
}
