using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // QuickStart may choose any tile, but this map needs green, open, walkable ground:
    // rock traps agents and gives the plague no targets. Choose a forest/swamp tile in the
    // map-generation long event, before InitGameStart reads `startingTile`.
    public static class LandingSite
    {
        // Swamp is the same climate with worse footing, so it is a fallback rather than
        // an equal.
        static readonly string[] Tropical = { "TropicalRainforest", "TropicalSwamp" };

        // Not tropical, still green. Some planets generate with no tropics at all.
        static readonly string[] Temperate = { "TemperateForest", "TemperateSwamp" };

        public static void Choose()
        {
            var init = Find.GameInitData;
            var layer = Find.WorldGrid?.Surface;
            if (init == null || layer == null) return;

            // The biome matters more than the ground does - a hilly rainforest still beats a
            // flat temperate one for the look of the thing.
            var tile = Pick(layer, Tropical, Hilliness.Flat)
                    ?? Pick(layer, Tropical, Hilliness.SmallHills)
                    ?? Pick(layer, Temperate, Hilliness.Flat)
                    ?? Pick(layer, Temperate, Hilliness.SmallHills);

            if (tile == null)
            {
                // Vanilla's tile is already in place, so leaving it alone is the fallback; say
                // so, because the colony will look wrong and this is why.
                Log.Warning("[SlopWorld] no green lowland tile on this planet; keeping the random one");
                return;
            }

            init.startingTile = tile.Value;
            Log.Message($"[SlopWorld] landing on {Find.WorldGrid[tile.Value].PrimaryBiome?.defName} " +
                        $"at tile {tile.Value}");
        }

        // Scans the layer once and picks among the hits rather than calling vanilla's
        // weighted finder with a predicate: that logs an error when it comes up empty,
        // and coming up empty is the normal case here.
        static PlanetTile? Pick(PlanetLayer layer, string[] biomes, Hilliness maxHills)
        {
            var hits = new List<PlanetTile>();
            var grid = Find.WorldGrid;

            for (int i = 0; i < layer.TilesCount; i++)
            {
                var t = new PlanetTile(i, layer);
                var tile = grid[t];
                if (tile == null) continue;
                // Undefined sorts below Flat, so it would pass a plain <= test. It means the tile
                // never had terrain generated - ocean, mostly.
                if (tile.hilliness == Hilliness.Undefined) continue;
                if (tile.hilliness > maxHills) continue;

                var biome = tile.PrimaryBiome;
                if (biome == null) continue;
                if (System.Array.IndexOf(biomes, biome.defName) < 0) continue;

                // Last, because it is much the most expensive of the four: it walks the tile's
                // neighbours, roads and existing settlements.
                if (!TileFinder.IsValidTileForNewSettlement(t)) continue;

                hits.Add(t);
            }

            return hits.Count > 0 ? hits.RandomElement() : (PlanetTile?)null;
        }
    }
}
