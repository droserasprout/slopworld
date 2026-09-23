using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Use shared gas effects to identify plague actions. Flecks use the map batch without spawning Things.
    // Set color and alpha in definitions because instanceColor combines with a separate fade alpha.
    // Select definitions for color variation and numeric values for speed variation.
    public static class PlagueFx
    {
        // Multiply fleck speed by a random value in this range.
        // Definition acceleration moves the cloud, while speed variation separates the flecks.
        const float SlowestFleck = 0.35f;
        const float FastestFleck = 1.0f;

        // Select one color definition per cloud so its flecks share a hue.
        // See Flecks.xml for the separate definitions.
        static FleckDef Gas
        {
            get
            {
                switch (Rand.Range(0, 3))
                {
                    case 0: return ModDefOf.SlopPlagueGasDeep;
                    case 1: return ModDefOf.SlopPlagueGasWarm;
                    default: return ModDefOf.SlopPlagueGas;
                }
            }
        }

        // Show when the plague first reaches a pawn.
        public static void Mark(Thing t) => At(Gas, t, 8, 1.8f, 0.25f, 0.55f);

        // Keep plant effects small but visible against grass.
        public static void Wither(Thing t) => At(Gas, t, 5, 1.3f, 0.14f, 0.3f);

        // Use green effects for new flowers when Grandma's visiting.
        // Keep them small because a colony can contain thousands of flowers.
        public static void Sprout(Thing t) => At(ModDefOf.SlopCleanAir, t, 6, 1.3f, 0.16f, 0.3f);

        // Use the same gas effect to identify the cause of different symptoms.
        public static void Act(Thing t) => At(Gas, t, 12, 2.2f, 0.35f, 0.6f);

        // Keep plague gas visible over the base game explosion.
        public static void Burst(Thing t) => At(Gas, t, 22, 2.9f, 0.70f, 1.1f);

        // Use a large effect to identify the core as the source before the plague starts.
        public static void Fume(Thing t) => At(Gas, t, 9, 2.6f, 0.40f, 1.1f);

        // Keep continuous vent effects small to avoid obscuring the map center.
        public static void Vent(Thing t) => At(Gas, t, 4, 1.15f, 0.25f, 0.6f);

        public static void Arrive(Thing t) => At(Gas, t, 24, 2.8f, 0.60f, 1.1f);

        // Use DrawPos so the effect follows a pawn during movement.
        // Increase spread with scale to avoid dense overlap between large flecks.
        // Accept a definition so restorative effects can use green clouds.
        public static void At(FleckDef def, Thing t, int count, float scale, float speed,
                              float spread = 0.4f)
        {
            if (def == null) return;

            if (t == null || !t.Spawned) return;

            // PaneOverDraw skips map drawing while a terminal pane covers it.
            if (TerminalWindow.Covering) return;

            var map = t.Map;
            if (map == null) return;

            var loc = t.DrawPos;
            if (!GenView.ShouldSpawnMotesAt(loc, map)) return;

            for (int i = 0; i < count; i++)
            {
                // Spread the flecks around the position to form a cloud.
                var at = loc + new Vector3(Rand.Range(-spread, spread), 0f,
                                           Rand.Range(-spread, spread));

                var d = FleckMaker.GetDataStatic(at, map, def,
                    scale * Rand.Range(0.7f, 1.3f));
                d.rotation = Rand.Range(0, 360); // the cloud texture is directional
                d.rotationRate = Rand.Range(-9f, 9f);
                d.velocityAngle = Rand.Range(0, 360);
                // Use low speed so definition acceleration controls the main direction of movement.
                d.velocitySpeed = speed * Rand.Range(SlowestFleck, FastestFleck);
                map.flecks.CreateFleck(d);
            }
        }
    }
}
