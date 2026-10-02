using System;
using System.Collections.Generic;

namespace SlopWorld
{
    public static partial class Patch_LoadingTips
    {
        static readonly Random Dice = new Random(1);
    }
}

namespace Verse
{
    public static class TipRandomSelection
    {
        static readonly Random Random = new Random(1);
        public static T RandomElement<T>(this List<T> items) => items[Random.Next(items.Count)];
    }
}
