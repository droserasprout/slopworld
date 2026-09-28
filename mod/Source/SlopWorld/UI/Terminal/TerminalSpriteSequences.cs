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
                int best = i;
                string bestKey = key;
                int columns = first.Columns;
                int bestColumns = columns;
                for (int j = i + 1; j < runs.Count; j++)
                {
                    var next = runs[j];
                    var previous = runs[j - 1];
                    if (previous.Col + previous.Columns != next.Col ||
                        first.Fg != next.Fg || first.Bg != next.Bg ||
                        first.HasBg != next.HasBg || first.Bold != next.Bold ||
                        first.Url != next.Url) break;
                    key += next.Text;
                    columns += next.Columns;
                    if (catalog.Match(key, 0, out int length, out _) && length == key.Length)
                    { best = j; bestColumns = columns; bestKey = key; }
                    if (!catalog.HasLongerKey(key)) break;
                }
                if (best == i) continue;
                first.Text = bestKey;
                first.CellWidth = bestColumns;
                first.IsCluster = true;
                runs[i] = first;
                runs.RemoveRange(i + 1, best - i);
            }
        }

    }
}
