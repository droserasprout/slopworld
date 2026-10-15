using System;
using System.Text;

namespace SlopWorld
{
    // Metadata policy for installed fonts. OpenTypeFace owns binary layout;
    // OpenTypeCollection owns conversion to the renderer's standalone format.
    static class InstalledFontNames
    {
        public static string Read(byte[] bytes, int face = 0)
        {
            try
            {
                var font = new OpenTypeFace(bytes, face);
                if (!font.TryGetTable(OpenTypeTags.Name, out var table))
                    return null;
                var names = new OpenTypeNames(table.Data);
                string best = null;
                int bestRank = -1;
                for (int index = 0; index < names.Count; index++)
                {
                    var record = names.Record(index);
                    if (!record.IsFamily || !record.IsUnicode)
                        continue;
                    int rank = (record.IsTypographicFamily ? 2 : 0) + (record.IsEnglish ? 1 : 0);
                    if (rank <= bestRank)
                        continue;
                    string family = Encoding
                        .BigEndianUnicode.GetString(names.Text(record))
                        .Trim('\0', ' ');
                    if (family.Length == 0)
                        continue;
                    best = family;
                    bestRank = rank;
                }
                return best;
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return null;
        }

        public static bool Monospaced(byte[] bytes, int face = 0)
        {
            try
            {
                var font = new OpenTypeFace(bytes, face);
                return font.TryGetTable(OpenTypeTags.Post, out var table) && table.IsFixedPitch;
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return false;
        }

        public static int Count(byte[] bytes)
        {
            try
            {
                return OpenTypeFace.FaceCount(bytes);
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return 0;
        }

        // Prefer upright, normal-width, 400-weight faces. OS/2 is authoritative
        // when valid; older fonts can fall back to the head table's style flags.
        public static int StyleDistance(byte[] bytes, int face = 0)
        {
            try
            {
                var font = new OpenTypeFace(bytes, face);
                int fallback = int.MaxValue;
                for (int index = 0; index < font.TableCount; index++)
                {
                    var table = font.Table(index);
                    if (table.Tag == OpenTypeTags.Os2 && table.HasSelection)
                    {
                        int weight = table.WeightClass,
                            width = table.WidthClass;
                        if (weight >= 1 && weight <= 1000 && width >= 1 && width <= 9)
                            return Distance(weight, width, table.IsItalicOrOblique);
                    }
                    if (table.Tag == OpenTypeTags.Head && table.HasMacStyle)
                        fallback = Distance(table.IsBold ? 700 : 400, 5, table.IsItalic);
                }
                return fallback;
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return int.MaxValue;
        }

        static int Distance(int weight, int width, bool italic) =>
            (italic ? 100_000 : 0) + Math.Abs(width - 5) * 10_000 + Math.Abs(weight - 400);
    }
}
