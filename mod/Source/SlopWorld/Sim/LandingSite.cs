using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Which tile the colony lands on.
    ///
    /// The quick start takes whatever TileFinder.RandomStartingTile hands back, which
    /// is any tile a settlement could legally go on - ice sheet, extreme desert, the
    /// side of a mountain. That is fine for a game about surviving one and wrong for
    /// this: the map is scenery for a row of terminals, so it wants to be green, open
    /// and walkable. Rock in particular is the enemy twice over - it is what agents
    /// get sealed inside of (see SpawnSpot) and it is what the plague's circle
    /// crawls over without anything to kill.
    ///
    /// So: tropics if the planet has any, temperate forest if not, flat before hilly,
    /// and vanilla's own pick if neither exists. Deserts, ice and bare rock are never
    /// chosen. This runs off the main thread inside the map-generation long event,
    /// after SetupForQuickTestPlay has built the world and before InitGameStart reads
    /// the tile, so overwriting startingTile is all it takes - no patch needed.
    /// </summary>
    public static class LandingSite
    {
        // Green, wet, and thick with things for the plague to strip. Rainforest
        // first; swamp is the same climate with worse footing, so it is a fallback
        // rather than an equal.
        static readonly string[] Tropical = { "TropicalRainforest", "TropicalSwamp" };

        // Not tropical, still green. Some planets generate with no tropics at all.
        static readonly string[] Temperate = { "TemperateForest", "TemperateSwamp" };

        public static void Choose()
        {
            var init = Find.GameInitData;
            var layer = Find.WorldGrid?.Surface;
            if (init == null || layer == null) return;

            // Flat before hilly, tropical before temperate, and the biome matters
            // more than the ground does - a hilly rainforest still beats a flat
            // temperate one for the look of the thing.
            var tile = Pick(layer, Tropical, Hilliness.Flat)
                    ?? Pick(layer, Tropical, Hilliness.SmallHills)
                    ?? Pick(layer, Temperate, Hilliness.Flat)
                    ?? Pick(layer, Temperate, Hilliness.SmallHills);

            if (tile == null)
            {
                // Nothing green anywhere. Vanilla's tile is already in place, so
                // leaving it alone is the fallback; say so, because the colony will
                // look wrong and this is why.
                Log.Warning("[SlopWorld] no green lowland tile on this planet; keeping the random one");
                return;
            }

            init.startingTile = tile.Value;
            Log.Message($"[SlopWorld] landing on {Find.WorldGrid[tile.Value].PrimaryBiome?.defName} " +
                        $"at tile {tile.Value}");
        }

        /// <summary>A random tile matching one of <paramref name="biomes"/> at
        /// <paramref name="maxHills"/> or flatter. Scans the layer once and picks
        /// among the hits rather than calling vanilla's weighted finder with a
        /// predicate: that logs an error when it comes up empty, and coming up empty
        /// is the normal case here for three of the four passes.</summary>
        static PlanetTile? Pick(PlanetLayer layer, string[] biomes, Hilliness maxHills)
        {
            var hits = new List<PlanetTile>();
            var grid = Find.WorldGrid;

            for (int i = 0; i < layer.TilesCount; i++)
            {
                var t = new PlanetTile(i, layer);
                var tile = grid[t];
                if (tile == null) continue;
                // Undefined sorts below Flat, so it would pass a plain <= test. It
                // means the tile never had terrain generated - ocean, mostly.
                if (tile.hilliness == Hilliness.Undefined) continue;
                if (tile.hilliness > maxHills) continue;

                var biome = tile.PrimaryBiome;
                if (biome == null) continue;
                if (System.Array.IndexOf(biomes, biome.defName) < 0) continue;

                // Last, because it is much the most expensive of the four: it walks
                // the tile's neighbours, roads and existing settlements.
                if (!TileFinder.IsValidTileForNewSettlement(t)) continue;

                hits.Add(t);
            }

            return hits.Count > 0 ? hits.RandomElement() : (PlanetTile?)null;
        }
    }
}
