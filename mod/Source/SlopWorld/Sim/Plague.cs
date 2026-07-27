using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    /// <summary>
    /// What the machine persona does to a living world. A circle centred on the core
    /// widens until it stops, and how hard it bites depends on how close to the core
    /// you are: near the middle it is the full thing, further out it is a fever, and
    /// past the edge of its reach the map is simply left alone. Anything alive inside
    /// the circle is marked, and marked things come apart - bleeding, burning,
    /// detonating, retching. Plants do not get a slow death: a tree the circle reaches
    /// goes bare, everything smaller is gone.
    ///
    /// The bands are the point of the thing: a map that is uniformly dead is one
    /// texture, and a dead middle, a sick ring and a live rim reads as something
    /// spreading out of the core.
    ///
    /// Which means the bands must not have edges, and must not converge. Hard radii
    /// drew a circle on the ground you could trace with a finger, and a chance
    /// re-rolled each sweep still ends in one flat dead disc, just later. So severity
    /// is continuous - certain inside FullRadius, falling off to nothing by
    /// EdgeRadius - and it is dithered against Grit, a value each cell keeps forever.
    /// Ground that shrugs the plague off goes on shrugging it off, so the falloff is
    /// a texture rather than a delay, and the boundary dissolves into speckle. The
    /// two bands also have to *look* different, which is why the weak one holds
    /// plants back instead of taking them.
    ///
    /// Agents are immune, wholly: never marked, never harmed, and never standing near
    /// a detonation we chose to start. An agent's colonist dying would leave its
    /// session pointing at a corpse.
    ///
    /// Effects are applied directly rather than left to the sim, because there is no
    /// sim: Patch_Health skips health ticks, so a hediff never worsens and nothing
    /// ever bleeds out on its own. The mark is a tag; the killing is done here. Fire
    /// is the exception and deliberately so - a Fire is a Thing with its own tick,
    /// which the strip does not touch, so an ignition is the one effect that goes on
    /// happening after we walk away from it.
    ///
    /// MapComponents are instantiated for every subclass, so this needs no def.
    /// </summary>
    public class Plague : MapComponent
    {
        // How the circle grows: a step every interval, until it hits EdgeFrac.
        const int SpreadInterval = 60;
        const float SpreadStep = 4f;
        const float StartRadius = 4f;

        // How far the plague reaches, as fractions of the map's side. Inside
        // FullFrac it is certain; from there to EdgeFrac it thins out to nothing.
        // Fractions rather than cells so the shape holds on any map size.
        const float FullFrac = 0.2f;
        const float EdgeFrac = 0.4f;

        // How often a marked thing rolls for an effect. What is left over after the
        // odds below is a quiet tick.
        const int EffectInterval = 300;

        // Blast geometry. Rare and enormous, rather than common and a firecracker:
        // when the plague does this it should take the ground with it.
        const float BlastRadius = 5.5f;
        const int BlastDamage = 200;
        const float BlastSafeRadius = 9f; // no closer to an agent than this, > BlastRadius

        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;

        // Withering is cheap, but a grown map has thousands of plants.
        const int PlantsPerTick = 10;

        // Ticks between breaths of the core's stack: three a second at normal speed,
        // of the smallest puff there is. The core should never be a quiet object
        // sitting in a field, and that is the whole of what this is for - a plume
        // any heavier is a fog bank parked on the middle of the map, with everything
        // the plague does out at the edge read through it. It costs nothing off
        // screen either way, PlagueFx.At dropping whatever ShouldSpawnMotesAt
        // refuses, and the whole stack is one cell's worth of flecks.
        const int VentInterval = 20;

        /// <summary>How hard the plague lands somewhere. Read from the cell, not from
        /// the mark, so a marked animal that wanders out of reach goes quiet and
        /// starts up again when it wanders back.</summary>
        public enum Band { None, Weak, Full }

        /// <summary>One band's worth of plague. Probabilities are per roll and share
        /// one pass, so they add rather than compose; the remainder is nothing
        /// happening, which is most of it.</summary>
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            /// Whether the band takes plants or only holds them back. This is the
            /// difference the eye actually reads: stripping both bands identically
            /// left nothing telling them apart but pawn effects nobody watches.
            public bool Strips;

            /// What the weak band knocks a plant's growth back to.
            public float StuntTo;
        }

        // The plague at the core. Detonation and ignition are both rare on purpose:
        // one is loud enough to be an event and the other does not stop when the roll
        // is over, so either landing often would be the whole map at once.
        static readonly Dose Full = new Dose
        {
            PExplode = 0.015f,
            PIgnite = 0.020f,
            PBleed = 0.300f,
            PVomit = 0.250f,
            BleedMin = 8f,
            BleedMax = 18f,
            FireSize = 1.0f,
            // ~15k plants fall inside the circle on a default map and each is rolled
            // once, so this is a handful of ignitions over the whole first sweep.
            // Fire does the rest by itself.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // What is left of it out at the edge: fewer rolls come up anything, the cuts
        // are shallow, and nothing detonates - a blast is the plague at full strength
        // and there is no half of one. Plants are held back rather than taken, so the
        // band reads as thin instead of as more dead ground.
        static readonly Dose Weak = new Dose
        {
            PExplode = 0f,
            PIgnite = 0.005f,
            PBleed = 0.120f,
            PVomit = 0.150f,
            BleedMin = 3f,
            BleedMax = 7f,
            FireSize = 0.4f,
            PlantIgnite = 0.00008f,
            Strips = false,
            StuntTo = 0.15f,
        };

        // Persisted. The seed is what makes the dither this colony's rather than
        // every colony's: without it two maps would thin out through the exact same
        // speckle, because the noise is a pure function of the cell.
        IntVec3 _origin = IntVec3.Invalid;
        float _radius;
        bool _active;
        int _seed;

        // Runtime: a rolling cursor over the map's plants. Rebuilt when it runs off
        // the end, which is also how regrowth gets caught.
        List<Plant> _plants;
        int _plantIdx;

        // The cat's aura, looked up once. See Aura: the one thing on this map that
        // takes ground back off the circle.
        Aura _aura;

        // The core, looked up until it is found. Held rather than asked for every
        // eighth tick, and dropped if it ever stops being spawned.
        Thing _core;

        public Plague(Map map) : base(map) { }

        /// <summary>Starts the spread from the core's cell. Called by the intro once
        /// the core is standing; a reload picks up from the persisted state.</summary>
        public void Arm(IntVec3 origin)
        {
            if (_active) return;
            _origin = origin;
            _radius = StartRadius;
            _seed = Rand.Int;
            _active = true;
            Log.Message($"[SlopWorld] plague seeded at {origin}, " +
                        $"certain to {FullRadius:F0} cells, thinning out by {EdgeRadius:F0}");
        }

        public override void MapComponentTick()
        {
            if (!_active) return;

            int t = Find.TickManager.TicksGame;
            if (t % SpreadInterval == 0) Spread();
            if (t % EffectInterval == 0) Effects();
            if (t % VentInterval == 0) Vent();
            StepPlants();
        }

        /// <summary>
        /// The core, venting, for as long as the colony lasts: the intro's own
        /// venting beat (<see cref="IntroDirector"/>, in the seconds before anything
        /// is marked) never quite switched off. The plume is what makes the thing in
        /// the middle of the map the source of what is happening to it rather than a
        /// prop the plague was seeded next to - which takes a wisp that is always
        /// there rather than a column, so this is <see cref="PlagueFx.Vent"/> and not
        /// the intro's <see cref="PlagueFx.Fume"/>.
        /// </summary>
        void Vent()
        {
            if (_core == null || _core.Destroyed || !_core.Spawned)
            {
                var found = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
                _core = found != null && found.Count > 0 ? found[0] : null;
                if (_core == null) return;
            }

            PlagueFx.Vent(_core);
        }

        float MapSide => Mathf.Max(map.Size.x, map.Size.z);
        float FullRadius => MapSide * FullFrac;
        float EdgeRadius => MapSide * EdgeFrac;

        /// <summary>How hard the plague pushes at a cell, before the cell gets a say:
        /// 1 inside FullRadius, falling off to 0 at EdgeRadius, and 0 anywhere the
        /// front has not reached yet. Clamped against EdgeRadius as well as against
        /// the live radius, so a colony saved before any of this existed comes back
        /// inside the falloff.</summary>
        float Bite(IntVec3 cell)
        {
            float d = cell.DistanceTo(_origin);
            if (d > Mathf.Min(_radius, EdgeRadius)) return 0f;
            if (d <= FullRadius) return 1f;
            return 1f - (d - FullRadius) / Mathf.Max(EdgeRadius - FullRadius, 1f);
        }

        /// <summary>A cell's own dice, rolled once and the same forever.
        ///
        /// Everything probabilistic about the plague reads this rather than
        /// Rand.Value, and it has to: the plant sweep walks the whole map over and
        /// over, so a chance re-rolled every pass converges on certainty - a
        /// per-plant coin flip still ends with every plant inside the radius dead,
        /// only later. Seeded off the cell instead, ground that shrugged the
        /// plague off keeps shrugging it off, through a reload as well, and the
        /// falloff stays a texture rather than a delay.</summary>
        float Grit(IntVec3 cell) =>
            Rand.ValueSeeded(Gen.HashCombineInt(cell.GetHashCode(), _seed));

        /// <summary>Which band a cell is in: the push, dithered against the cell's
        /// own dice. Full needs to beat the bite outright and Weak only its square
        /// root, which is the larger of the two - so the certain core is solid, and
        /// past it the ground breaks up into full, weak and untouched in shifting
        /// proportions until there is nothing left of any of it. Across the falloff
        /// that averages out at roughly 45% stripped and 18% stunted, but it is
        /// 100/0 where the certain core ends, 50/21 halfway out and 25/25 at three
        /// quarters, and nowhere along it is there an edge you could trace.</summary>
        public Band BandAt(IntVec3 cell)
        {
            if (!_active || !_origin.IsValid) return Band.None;

            float bite = Bite(cell);
            if (bite <= 0f) return Band.None;
            if (bite >= 1f) return Band.Full; // the certain core: no dice at all

            float grit = Grit(cell);
            if (grit < bite) return Band.Full;
            if (grit < Mathf.Sqrt(bite)) return Band.Weak;
            return Band.None;
        }

        /// <summary>Whether a cell is inside the circle at all. What Patch_ContainFire
        /// asks before letting a fire creep another cell out - deliberately the plain
        /// geometry rather than BandAt, because a fire that could not cross a cell the
        /// dither happened to spare would never get anywhere.</summary>
        public bool Reaches(IntVec3 cell) =>
            _active && _origin.IsValid &&
            cell.DistanceTo(_origin) <= Mathf.Min(_radius, EdgeRadius);

        /// <summary>Whether the cat is holding this one off. Asked of a thing rather
        /// than of a cell, because the aura's grace outlives the cat walking away and
        /// so belongs to what was standing there.</summary>
        bool Spared(Thing t) => (_aura ?? (_aura = Aura.Of(map)))?.Spares(t) == true;

        /// <summary>Whether the spread has been armed on this map. A map with no
        /// plague running on it - the menu's background, an unfinished intro - is not
        /// one whose fires we have any business containing.</summary>
        public bool Active => _active;

        // Widen the circle and mark every living thing that now falls inside it. The
        // radius stops at the edge; the pass does not, because things wander.
        void Spread()
        {
            _radius = Mathf.Min(_radius + SpreadStep, EdgeRadius);

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Infectable(pawn)) continue;
                if (BandAt(pawn.Position) == Band.None) continue;
                if (Marked(pawn)) continue;
                if (Spared(pawn)) continue; // the cat has it, for now
                pawn.health.AddHediff(SlopDefOf.SlopPlague);
                PlagueFx.Mark(pawn);
            }
        }

        // One tick of withering. The list is walked in slices and rebuilt at the end
        // of each pass, so a plant that grew since last time still gets its turn.
        void StepPlants()
        {
            if (_plants == null || _plantIdx >= _plants.Count)
            {
                _plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)
                    .OfType<Plant>().ToList();
                _plantIdx = 0;
                return; // keep the allocation off the same tick as the work
            }

            int budget = PlantsPerTick;
            while (_plantIdx < _plants.Count && budget-- > 0)
            {
                var p = _plants[_plantIdx++];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.def.plant == null) continue;

                var band = BandAt(p.Position);
                if (band == Band.None) continue;
                if (Spared(p)) continue;

                var dose = band == Band.Full ? Full : Weak;
                bool tree = p.def.plant.IsTree;

                // Whether this pass has anything left to do here. Every plant
                // comes back round forever: without this a bare tree would smoke
                // again on each pass, and a plant the weak band has already held
                // back would roll for ignition again until it eventually caught.
                bool todo = dose.Strips
                    ? !(tree && p.LeaflessNow)
                    : !tree && p.Growth > dose.StuntTo;
                if (!todo) continue;

                // Rarely the plant catches instead. The roll goes first because it has
                // to: TryStartFireIn weighs what is flammable in the cell, and
                // stripping the plant out from under it is exactly what leaves nothing
                // there to light. This one stays a live roll rather than reading Grit -
                // a fire is an event, not a property of the ground.
                if (Rand.Value < dose.PlantIgnite &&
                    FireUtility.TryStartFireIn(p.Position, map, dose.FireSize, null))
                    continue;

                if (!dose.Strips)
                {
                    // Held back, not taken. Trees in the weak band are left alone
                    // entirely: a bare tree is the core's look, and giving the falloff
                    // one too is most of what made the two bands indistinguishable.
                    PlagueFx.Wither(p);
                    p.Growth = dose.StuntTo;
                    // Growth is printed into the map mesh and the setter does not
                    // dirty it, so a stunted plant would keep drawing at full size
                    // until something else happened to that cell.
                    map.mapDrawer?.MapMeshDirty(p.Position, MapMeshFlagDefOf.Things);
                }
                else if (tree)
                {
                    PlagueFx.Wither(p);
                    p.MakeLeafless(Plant.LeaflessCause.Poison, false);
                }
                else
                {
                    PlagueFx.Wither(p); // before the destroy - a despawned plant has no DrawPos
                    p.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // Every marked thing rolls once: mostly nothing, sometimes something fatal.
        // The dose comes from where it is standing now, so the same mark means less
        // out at the edge and nothing at all past it.
        void Effects()
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position);
                if (band == Band.None) continue;
                // Belt and braces over Spread: a marked animal that has just walked
                // into the aura is unmarked at the aura's next sweep and not before,
                // and a detonation in that half second would be the one thing the cat
                // is for going visibly wrong.
                if (Spared(pawn)) continue;
                var dose = band == Band.Full ? Full : Weak;

                float roll = Rand.Value;
                if ((roll -= dose.PExplode) < 0f) Detonate(pawn, dose);
                else if ((roll -= dose.PIgnite) < 0f) Ignite(pawn, dose);
                else if ((roll -= dose.PBleed) < 0f) Bleed(pawn, dose);
                else if (roll - dose.PVomit < 0f) Vomit(pawn);
            }
        }

        void Detonate(Pawn pawn, Dose dose)
        {
            // Never light one off next to an agent: the blast does not care who it
            // is, and immunity has to mean immunity.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn, dose); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage, ignoredThings: Untouchable());
        }

        // What a blast steps around. The core first of all: it is where the plague
        // comes from and it stands in the middle of the band that detonates hardest,
        // so at this radius it would eventually blow a hole in its own origin. Built
        // per blast rather than cached, because blasts are rare now and the list is
        // not.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            return spared;
        }

        // The one effect that outlives the roll. FireUtility declines quietly for
        // anything that cannot burn or is already alight, so this needs no guard of
        // its own; the haze goes up first so the tell is the plague's rather than
        // just an animal that happens to be on fire.
        static void Ignite(Pawn pawn, Dose dose)
        {
            PlagueFx.Act(pawn);
            pawn.TryAttachFire(dose.FireSize, null);
        }

        // No bleed-out without health ticks, so the damage is the death: a few of
        // these in a row and whatever it is falls over.
        void Bleed(Pawn pawn, Dose dose)
        {
            var pos = pawn.Position;
            PlagueFx.Act(pawn); // before the damage, which may be the one that drops it
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, Rand.Range(dose.BleedMin, dose.BleedMax)));

            int cells = GenRadial.NumCellsInRadius(BloodRadius);
            for (int i = 0; i < BloodPerBleed; i++)
            {
                var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (c.InBounds(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, pawn.LabelShort, 1);
            }
        }

        // The harmless one checks whether it can land before it smokes: a pawn with no
        // job tracker, or one already retching, is a roll that did nothing, and a puff
        // over it would advertise an effect that never came.
        static void Vomit(Pawn pawn)
        {
            if (pawn.jobs == null) return;
            if (pawn.CurJobDef == JobDefOf.Vomit) return;
            PlagueFx.Act(pawn);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced);
        }

        static bool Marked(Pawn pawn) =>
            pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague) != null;

        static bool Infectable(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;
            if (pawn.health?.hediffSet == null) return false;
            if (pawn.RaceProps == null) return false;
            if (!pawn.RaceProps.Animal && !pawn.RaceProps.Humanlike) return false;

            // Nobody in the colony, ever. Agents are immune by design; the
            // scenario's starters are the intro's to kill; the pets fall out of the
            // same check and are meant to: see Pets, they outlive the map on purpose.
            return pawn.Faction == null || !pawn.Faction.IsPlayer;
        }

        bool AgentNear(IntVec3 cell, float radius)
        {
            var colony = AgentColony.Current;
            if (colony == null) return false;

            foreach (var kv in colony.All)
            {
                var p = kv.Value;
                if (p == null || !p.Spawned || p.Map != map) continue;
                if (p.Position.DistanceTo(cell) <= radius) return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _origin, "plagueOrigin", IntVec3.Invalid);
            Scribe_Values.Look(ref _radius, "plagueRadius", 0f);
            Scribe_Values.Look(ref _active, "plagueActive", false);
            Scribe_Values.Look(ref _seed, "plagueSeed", 0);
        }

        /// <summary>
        /// Nothing grows back where the plague takes plants. Every wild plant on a
        /// map arrives through this one method - the tick, the map generator and the
        /// mutators all end here - so refusing the cell is the whole of it.
        ///
        /// The alternative is the sweep, and the sweep alone loses: it walks the
        /// map forever and the spawner refills behind it, so the certain core spends
        /// the rest of the colony's life growing grass and having it torn out again.
        ///
        /// Gated on <see cref="Band.Full"/> rather than on <see cref="Reaches"/>, so
        /// the falloff stays a texture. The weak band holds plants back instead of
        /// taking them and has to keep being able to grow the ones it is holding; a
        /// cell the dither spared is untouched ground and grows what untouched
        /// ground grows. Both answers are stable per cell, because <c>Grit</c> is -
        /// so a cell is either sterile forever or fertile forever, and never
        /// flickers between the two.
        /// </summary>
        [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
        public static class Patch_NoRegrowth
        {
            static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
            {
                var plague = ___map?.GetComponent<Plague>();
                if (plague == null || plague.BandAt(c) != Band.Full) return true;

                // Except where the cat is standing. This is the only thing that ever
                // takes a cell back off the core, and it is what makes the aura read
                // as anything: the sweep stops stripping, the spawner starts filling,
                // and a green disc grows under the animal.
                if (Aura.Of(___map)?.Covers(c) == true) return true;

                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Fire stays inside the plague. Without this the bands are a lie the moment
        /// anything ignites: a Fire is a Thing with its own tick, the strip does not
        /// touch it, and a rainforest carries one to the map edge in minutes - so the
        /// third of the map that is supposed to be untouched would burn instead.
        ///
        /// TrySpread picks its own cell internally, so this can only allow or refuse
        /// the whole attempt: a fire already outside the circle never spreads. Fires
        /// nothing to do with us - a blast, a short circuit on a map with no plague
        /// on it - are left alone.
        /// </summary>
        [HarmonyPatch(typeof(Fire), "TrySpread")]
        public static class Patch_ContainFire
        {
            static bool Prefix(Fire __instance)
            {
                var plague = __instance.Map?.GetComponent<Plague>();
                if (plague == null || !plague.Active) return true;
                // A fire under the cat is going out at the next sweep anyway; this is
                // what stops it taking the aura's plants with it on the way.
                if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
                return plague.Reaches(__instance.Position);
            }
        }
    }
}
