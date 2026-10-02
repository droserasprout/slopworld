using System.Collections.Generic;

namespace SlopWorld
{
    // Presentation joins only: decoded daemon columns and copied text stay intact.
    static class TerminalSpriteSequences
    {
        public static void Join(List<SgrRun> runs, TextSpriteCatalog catalog)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                var first = runs[i];
                if (!catalog.HasLongerKey(first.Text)) continue;
                string key = first.Text;
                // Remember the longest complete key while the joined text remains a prefix.
                int bestEndIndex = i;
                string bestKey = key;
                int joinedColumns = first.Columns;
                int bestColumns = joinedColumns;
                for (int j = i + 1; j < runs.Count; j++)
                {
                    var next = runs[j];
                    var previous = runs[j - 1];
                    if (previous.Col + previous.Columns != next.Col ||
                        first.Fg != next.Fg || first.Bg != next.Bg ||
                        first.HasBg != next.HasBg || first.Bold != next.Bold ||
                        first.Url != next.Url) break;
                    key += next.Text;
                    joinedColumns += next.Columns;
                    if (catalog.Match(key, 0, out int length, out _) && length == key.Length)
                    { bestEndIndex = j; bestColumns = joinedColumns; bestKey = key; }
                    if (!catalog.HasLongerKey(key)) break;
                }
                if (bestEndIndex == i) continue;
                first.Text = bestKey;
                first.CellWidth = bestColumns;
                first.IsCluster = true;
                runs[i] = first;
                runs.RemoveRange(i + 1, bestEndIndex - i);
            }
        }

    }
}
