using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The plague's tell, shared by every one of its acts, so a spread reads as one
    // thing happening in a lot of places and the edge of the circle is visible while
    // it moves.
    //
    // Flecks, not motes: a struct submitted to the map's own batch rather than a
    // spawned Thing. Colour and alpha belong to the def, because instanceColor is
    // combined with a separate fade alpha and would throw away the transparency -
    // which is why the plague's hue varies by def (Gas) where its speed varies by
    // number. They cost nothing off screen, every entry point gating on
    // ShouldSpawnMotesAt.
    public static class PlagueFx
    {
        // What a fleck's stated speed is multiplied by. Well under one on both ends: the
        // def's own acceleration is what moves a cloud, and the jitter is only there so
        // that a puff drifts apart instead of expanding. Wide, because a spread of
        // speeds within one cloud is what keeps it from reading as a single object.
        const float SlowestFleck = 0.35f;
        const float FastestFleck = 1.0f;

        // One of the plague's three shades, rolled per cloud: the flecks of a single
        // puff share a hue, so what varies is the weather rather than the pixels. See
        // Flecks.xml for why this is three defs and not a colour.
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

        // It has to stay the smallest - thousands of these, ten a tick - but a single
        // wisp per plant left the sweep invisible against grass.
        public static void Wither(Thing t) => At(Gas, t, 5, 1.3f, 0.14f, 0.3f);

        // Bleeding, retching and seizing look nothing alike; the haze is what says they
        // are the same cause.
        public static void Act(Thing t) => At(Gas, t, 12, 2.2f, 0.35f, 0.6f);

        // Vanilla's blast is orange and generic, so this goes wide enough to still be the
        // thing you notice.
        public static void Burst(Thing t) => At(Gas, t, 22, 2.9f, 0.70f, 1.1f);

        // Heavy, and the one time the core is: nothing is marked yet, so this cloud is
        // the only thing on screen claiming that what follows came out of that object.
        public static void Fume(Thing t) => At(Gas, t, 9, 2.6f, 0.40f, 1.1f);

        // Small on purpose: this one never stops, so its job is to keep the core from
        // being a quiet object in a field. A breath the size of Fume three times a second
        // is a fog bank parked on the middle of the map.
        public static void Vent(Thing t) => At(Gas, t, 4, 1.15f, 0.25f, 0.6f);

        // The heaviest single puff there is: a clanker walks out of the same haze that
        // took everything else.
        public static void Arrive(Thing t) => At(Gas, t, 24, 2.8f, 0.60f, 1.1f);

        // Position comes from the thing rather than a passed cell, so the haze lands
        // under a pawn mid-stride. `spread` goes with the scale: big flecks dropped into
        // one handspan stack their alpha back into the solid blob a gas cloud was chosen
        // instead of. The def is a parameter because the cat's aura puts up the same
        // cloud in green - the plumbing is shared and the meaning is the def's.
        public static void At(FleckDef def, Thing t, int count, float scale, float speed,
                              float spread = 0.4f)
        {
            if (def == null) return;

            if (t == null || !t.Spawned) return;

            // The camera rule, harder: with a pane up the map is not drawn at all
            // (PaneOverDraw), so a fleck made here is one that fades out unseen.
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
                // Kept low on purpose: the def's own acceleration carries it up-map, and a fast
                // fleck outruns that and reads as a spark.
                d.velocitySpeed = speed * Rand.Range(SlowestFleck, FastestFleck);
                map.flecks.CreateFleck(d);
            }
        }
    }
}
