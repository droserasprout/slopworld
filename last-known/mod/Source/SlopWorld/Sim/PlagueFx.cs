using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The plague's tell, shared by every one of its acts. Flecks rather than motes: a struct
    // submitted to the map's own batch rather than a spawned Thing. Color and alpha belong to
    // the def, because instanceColor is combined with a separate fade alpha and would throw
    // the transparency away - hence the hue varying by def where the speed varies by number.
    public static class PlagueFx
    {
        // What a fleck's stated speed is multiplied by. Under one at both ends: the def's own
        // acceleration moves the cloud, and the jitter is only so a puff drifts apart rather
        // than expanding.
        const float SlowestFleck = 0.35f;
        const float FastestFleck = 1.0f;

        // Rolled per cloud, so the flecks of one puff share a hue. See Flecks.xml for why this
        // is three defs and not a color.
        static FleckDef Gas
        {
            get
            {
                switch (Rand.Range(0, 3))
                {
                    case 0: return SlopDefOf.SlopPlagueGasDeep;
                    case 1: return SlopDefOf.SlopPlagueGasWarm;
                    default: return SlopDefOf.SlopPlagueGas;
                }
            }
        }

        // The loudest of the quiet ones: the moment the circle reached it.
        public static void Mark(Thing t) => At(Gas, t, 8, 1.8f, 0.25f, 0.55f);

        // The smallest - thousands of these, ten a tick - but a single wisp per plant left the
        // sweep invisible against grass.
        public static void Wither(Thing t) => At(Gas, t, 5, 1.3f, 0.14f, 0.3f);

        // Wither's opposite number, and the only one of these in the cat's green rather than
        // the violet: a flowerbed opening where grandma is visiting. The same size as the wisp
        // it stands in for, and for the same reason - there are thousands of them over a colony.
        public static void Sprout(Thing t) => At(SlopDefOf.SlopCleanAir, t, 6, 1.3f, 0.16f, 0.3f);

        // Bleeding, retching and seizing look nothing alike; the haze says one cause.
        public static void Act(Thing t) => At(Gas, t, 12, 2.2f, 0.35f, 0.6f);

        // Wide enough to still be the thing you notice over vanilla's orange blast.
        public static void Burst(Thing t) => At(Gas, t, 22, 2.9f, 0.70f, 1.1f);

        // Heavy: nothing is marked yet, so this is the only thing on screen claiming that
        // what follows came out of that object.
        public static void Fume(Thing t) => At(Gas, t, 9, 2.6f, 0.40f, 1.1f);

        // Small: this one never stops, and a breath the size of Fume three times a second is a
        // fog bank parked on the middle of the map.
        public static void Vent(Thing t) => At(Gas, t, 4, 1.15f, 0.25f, 0.6f);

        public static void Arrive(Thing t) => At(Gas, t, 24, 2.8f, 0.60f, 1.1f);

        // Position off the thing rather than a passed cell, so the haze lands under a pawn
        // mid-stride. `spread` goes with the scale: big flecks in one handspan stack their
        // alpha back into a solid blob. The def is a parameter because the cat's aura puts up
        // the same cloud in green.
        public static void At(FleckDef def, Thing t, int count, float scale, float speed,
                              float spread = 0.4f)
        {
            if (def == null) return;

            if (t == null || !t.Spawned) return;

            // With a pane up the map is not drawn at all (PaneOverDraw).
            if (TerminalWindow.Covering) return;

            var map = t.Map;
            if (map == null) return;

            var loc = t.DrawPos;
            if (!GenView.ShouldSpawnMotesAt(loc, map)) return;

            for (int i = 0; i < count; i++)
            {
                // Scatter, so the flecks are a cloud rather than a pile.
                var at = loc + new Vector3(Rand.Range(-spread, spread), 0f,
                                           Rand.Range(-spread, spread));

                var d = FleckMaker.GetDataStatic(at, map, def,
                    scale * Rand.Range(0.7f, 1.3f));
                d.rotation = Rand.Range(0, 360); // the cloud texture is directional
                d.rotationRate = Rand.Range(-9f, 9f);
                d.velocityAngle = Rand.Range(0, 360);
                // Low: the def's own acceleration carries it up-map, and a fast fleck outruns
                // that and reads as a spark.
                d.velocitySpeed = speed * Rand.Range(SlowestFleck, FastestFleck);
                map.flecks.CreateFleck(d);
            }
        }
    }
}
