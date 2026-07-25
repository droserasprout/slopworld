using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The plague's tell. Tagging a thing, stripping a plant and dropping a marked
    /// one all put up the same pink smoke, so a spread reads as one thing happening
    /// in a lot of places rather than a heap of unrelated misfortunes - and so the
    /// edge of the circle is visible while it moves, which it otherwise is not.
    ///
    /// Flecks, not motes: the mote classes are gone from 1.6's throw helpers, and a
    /// fleck is a struct submitted to the map's own batch instead of a spawned Thing.
    /// The colour is per-instance rather than a def of our own, because vanilla's
    /// Smoke fleck is a white texture and instanceColor multiplies straight through
    /// it - a private FleckDef would only be the same texture with our tint baked in.
    ///
    /// Flecks tick with the map and the map still ticks here, so these fade normally
    /// even with the sim stripped. They cost nothing off screen: every entry point
    /// gates on ShouldSpawnMotesAt first, and the plague eats the whole map, most of
    /// which the camera is not looking at.
    /// </summary>
    public static class PlagueFx
    {
        // Two ends of the same bruise. Every puff picks somewhere between them so a
        // cloud has depth instead of reading as one flat decal.
        static readonly Color Hot = new Color(1.00f, 0.18f, 0.72f);
        static readonly Color Deep = new Color(0.66f, 0.22f, 0.92f);

        /// <summary>Something living just got marked. The loudest of the quiet ones:
        /// this is the moment the circle reached it.</summary>
        public static void Mark(Thing t) => At(t, 4, 1.2f, 0.30f);

        /// <summary>A plant went bare or went away. Small - there are thousands of
        /// these and they go one per tick, so a sweep should look like a haze
        /// crossing the field, not a barrage.</summary>
        public static void Wither(Thing t) => At(t, 2, 0.85f, 0.16f);

        /// <summary>A marked thing's roll came up something. Bleeding, retching and
        /// seizing look nothing alike; the puff is what says they are the same
        /// cause.</summary>
        public static void Act(Thing t) => At(t, 5, 1.5f, 0.45f);

        /// <summary>A detonation. Vanilla's blast is orange and generic, so this goes
        /// wide enough to still be the thing you notice.</summary>
        public static void Burst(Thing t) => At(t, 9, 2.0f, 0.90f);

        /// <summary>Shared body. Position comes from the thing rather than a passed
        /// cell so the puff lands under a pawn mid-stride instead of on the cell it
        /// is nominally standing in.</summary>
        static void At(Thing t, int count, float scale, float speed)
        {
            if (t == null || !t.Spawned) return;

            var map = t.Map;
            if (map == null) return;

            var loc = t.DrawPos;
            if (!GenView.ShouldSpawnMotesAt(loc, map)) return;

            for (int i = 0; i < count; i++)
            {
                // Scatter inside about a cell. Every fleck starting on the same point
                // gives a single hard blob however many of them there are.
                var at = loc + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f));

                var d = FleckMaker.GetDataStatic(at, map, FleckDefOf.Smoke,
                    scale * Rand.Range(0.7f, 1.3f));
                d.instanceColor = Color.Lerp(Hot, Deep, Rand.Value);
                d.rotationRate = Rand.Range(-24f, 24f);
                d.velocityAngle = Rand.Range(0, 360);
                d.velocitySpeed = speed * Rand.Range(0.6f, 1.4f);
                map.flecks.CreateFleck(d);
            }
        }
    }
}
