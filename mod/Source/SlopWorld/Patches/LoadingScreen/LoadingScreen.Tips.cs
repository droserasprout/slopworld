using System;
using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    public static partial class Patch_LoadingTips
    {
        // Grandma's visiting hides tips that end with ` (`.
        // Both modes show unmarked tips. Strip removes the marker before display.
        const string Sad = " (";

        static readonly LoadingTipSelection StreamSelection = new LoadingTipSelection();

        static string NextStreamTip(int columns, int occupied)
        {
            var pool = new List<string>();
            foreach (string tip in Tips)
                if (Shown(tip, Settings.GrandmaMode))
                    pool.Add(Strip(tip));
            return StreamSelection.Pick(pool, columns, occupied, Dice);
        }

        static bool Shown(string tip, bool grandma) =>
            !grandma || !tip.EndsWith(Sad, StringComparison.Ordinal);

        static string Strip(string tip)
        {
            if (tip.EndsWith(Sad, StringComparison.Ordinal))
                return tip.Substring(0, tip.Length - Sad.Length);
            return tip;
        }

        static readonly List<string> Tips = LoadTips();

        static List<string> LoadTips()
        {
            using (
                var stream = typeof(Patch_LoadingTips).Assembly.GetManifestResourceStream(
                    "SlopWorld.LoadingTips"
                )
            )
            using (var reader = new System.IO.StreamReader(stream))
            {
                var tips = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (
                        !string.IsNullOrWhiteSpace(line)
                        && !line.StartsWith("#", StringComparison.Ordinal)
                    )
                        tips.Add(line);
                return tips;
            }
        }

        // The other place a tip turns up is the persona core's hover bubble (CoreTip). Rolled
        // rather than filtered, this being one line and not the wall: keep drawing until one
        // comes up that this mode is allowed to see.
        public static string RandomTip
        {
            get
            {
                bool grandma = Settings.GrandmaMode;
                string tip;
                do
                {
                    tip = Tips.RandomElement();
                } while (!Shown(tip, grandma));
                return Strip(tip);
            }
        }

        // RandomTips draws without replacement. Twelve covers the shipped five-tip
        // prompt and prompts containing several `{{ random_tip }}` mentions.
        public const int TipBatch = 12;

        public static List<string> RandomTips(int n)
        {
            bool grandma = Settings.GrandmaMode;
            var pool = new List<string>();
            foreach (string tip in Tips)
                if (Shown(tip, grandma))
                    pool.Add(Strip(tip));
            var picked = new List<string>();
            for (int i = 0; i < n && pool.Count > 0; i++)
            {
                int at = Dice.Next(pool.Count);
                picked.Add(pool[at]);
                pool.RemoveAt(at);
            }
            return picked;
        }
    }
}
