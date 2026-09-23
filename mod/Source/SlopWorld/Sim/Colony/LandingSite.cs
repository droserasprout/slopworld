using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Choose a forest or swamp tile with open ground for agents and plague targets.
    // Run during map generation, before InitGameStart reads startingTile.
    public static class LandingSite
    {
        // Consider tropical rainforest and swamp tiles.
        static readonly string[] Tropical = { "TropicalRainforest", "TropicalSwamp" };

        // Use temperate biomes if no suitable tropical tile exists.
        static readonly string[] Temperate = { "TemperateForest", "TemperateSwamp" };

        public static void Choose()
        {
            var init = Find.GameInitData;
            var layer = Find.WorldGrid?.Surface;
            if (init == null || layer == null) return;

            // Prefer tropical biomes, even if they have more hills than a temperate tile.
            var tile = Pick(layer, Tropical, Hilliness.Flat)
                    ?? Pick(layer, Tropical, Hilliness.SmallHills)
                    ?? Pick(layer, Temperate, Hilliness.Flat)
                    ?? Pick(layer, Temperate, Hilliness.SmallHills);

            if (tile == null)
            {
                // Keep the random tile if no suitable tile exists. Log the reason for the fallback.
                Log.Warning("[SlopWorld] No green lowland tile exists on this planet. Keeping the random tile.");
                return;
            }

            init.startingTile = tile.Value;
            Log.Message($"[SlopWorld] landing on {Find.WorldGrid[tile.Value].PrimaryBiome?.defName} " +
                        $"at tile {tile.Value}");
        }

        // Scan the layer once and select a matching tile at random.
        // The base game finder logs an error if no tile matches. Here, no match is an expected result.
        static PlanetTile? Pick(PlanetLayer layer, string[] biomes, Hilliness maxHills)
        {
            var hits = new List<PlanetTile>();
            var grid = Find.WorldGrid;

            for (int i = 0; i < layer.TilesCount; i++)
            {
                var t = new PlanetTile(i, layer);
                var tile = grid[t];
                if (tile == null) continue;
                // Undefined sorts below Flat, so a comparison with maxHills alone would accept it.
                // Exclude tiles without generated terrain, such as ocean tiles.
                if (tile.hilliness == Hilliness.Undefined) continue;
                if (tile.hilliness > maxHills) continue;

                var biome = tile.PrimaryBiome;
                if (biome == null) continue;
                if (System.Array.IndexOf(biomes, biome.defName) < 0) continue;

                // Check settlement validity last because it also checks nearby tiles, roads, and settlements.
                if (!TileFinder.IsValidTileForNewSettlement(t)) continue;

                hits.Add(t);
            }

            return hits.Count > 0 ? hits.RandomElement() : (PlanetTile?)null;
        }
    }
}
