using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The plague's tell. Tagging a thing, stripping a plant and dropping a marked
    /// one all put up the same pink haze, so a spread reads as one thing happening
    /// in a lot of places rather than a heap of unrelated misfortunes - and so the
    /// edge of the circle is visible while it moves, which it otherwise is not.
    ///
    /// Flecks, not motes: a fleck is a struct submitted to the map's own batch
    /// instead of a spawned Thing. The look is SlopPlagueGas (see Defs/Flecks.xml),
    /// a translucent gas cloud that grows, drifts up-map and is gone inside a
    /// second. Colour and alpha belong to the def; nothing here sets instanceColor,
    /// because that is combined with a separate fade alpha and would throw away
    /// the transparency that makes this a haze.
    ///
    /// Flecks tick with the map and the map still ticks here, so these fade
    /// normally even with the sim stripped. They cost nothing off screen: every
    /// entry point gates on ShouldSpawnMotesAt first, and the plague eats the
    /// whole map, most of which the camera is not looking at.
    /// </summary>
    public static class PlagueFx
    {
        /// <summary>Something living just got marked. The loudest of the quiet ones:
        /// this is the moment the circle reached it.</summary>
        public static void Mark(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 8, 1.8f, 0.25f, 0.55f);

        /// <summary>A plant went bare or went away. The smallest of them, and it has
        /// to stay the smallest - there are thousands of these and they go ten a tick
        /// - but a single wisp per plant left the sweep invisible against grass, so
        /// this is a puff now rather than a hint of one.</summary>
        public static void Wither(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 5, 1.3f, 0.14f, 0.3f);

        /// <summary>A marked thing's roll came up something. Bleeding, retching and
        /// seizing look nothing alike; the haze is what says they are the same
        /// cause, so it wants to be thick enough to be read as the cause.</summary>
        public static void Act(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 12, 2.2f, 0.35f, 0.6f);

        /// <summary>A detonation. Vanilla's blast is orange and generic, so this goes
        /// wide enough to still be the thing you notice.</summary>
        public static void Burst(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 22, 2.9f, 0.70f, 1.1f);

        /// <summary>The core venting in the intro, in the seconds between it landing
        /// and the colony coming apart. Heavy, and it is the one time the core is
        /// heavy: nothing is marked yet, so this cloud is the only thing on screen
        /// making the claim that what follows came out of that object.</summary>
        public static void Fume(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 9, 2.6f, 0.40f, 1.1f);

        /// <summary>The core's stack, ever after - see <see cref="Plague.Vent"/>.
        /// Small, and small on purpose: this one never stops, so its job is to keep
        /// the core from ever being a quiet object in a field, not to be the thing
        /// you are looking at. A breath the size of <see cref="Fume"/> three times a
        /// second is a fog bank parked on the middle of the map, and everything the
        /// plague does out at the edge has to be read through it.</summary>
        public static void Vent(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 4, 1.15f, 0.25f, 0.6f);

        /// <summary>An agent arriving. The heaviest single puff there is: a clanker
        /// walks out of the same haze that took everything else, which is the whole
        /// claim the opening scene is making.</summary>
        public static void Arrive(Thing t) => At(SlopDefOf.SlopPlagueGas, t, 24, 2.8f, 0.60f, 1.1f);

        /// <summary>Shared body. Position comes from the thing rather than a passed
        /// cell so the haze lands under a pawn mid-stride instead of on the cell it
        /// is nominally standing in. <paramref name="spread"/> goes with the scale:
        /// big flecks dropped into the same handspan stack their alpha and give back
        /// the solid blob the gas cloud was chosen to avoid.
        ///
        /// The def is a parameter because the cat's aura puts up the same cloud in
        /// green (<see cref="Aura"/>): the scatter, the rotation and the off-screen
        /// gate are one piece of plumbing, and what the haze <em>means</em> is the
        /// def's business rather than this method's.</summary>
        public static void At(FleckDef def, Thing t, int count, float scale, float speed,
                              float spread = 0.4f)
        {
            if (def == null) return;

            if (t == null || !t.Spawned) return;

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
                d.rotationRate = Rand.Range(-12f, 12f);
                d.velocityAngle = Rand.Range(0, 360);
                // Kept low on purpose: the def's own acceleration carries it up-map,
                // and a fast fleck outruns that and reads as a spark instead.
                d.velocitySpeed = speed * Rand.Range(0.6f, 1.4f);
                map.flecks.CreateFleck(d);
            }
        }
    }
}
