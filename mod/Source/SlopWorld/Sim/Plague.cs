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
    /// goes bare, everything smaller is gone. Whatever wanders in later, or grows
    /// back, is caught by a later sweep, because the circle never shrinks.
    ///
    /// The bands are the point of the thing: a map that is uniformly dead is one
    /// texture, and a map with a dead middle, a sick ring and a live rim reads as
    /// something spreading out of the core - which is what it is.
    ///
    /// Which means the bands must not have edges, and must not converge. The first
    /// cut of this had both faults: hard radii drew a circle on the ground you could
    /// trace with a finger, and both bands stripped plants the same way, so once the
    /// sweep had been round a few times the whole thing was one flat disc of dead
    /// ground. So severity is continuous - certain inside FullRadius, falling off to
    /// nothing by EdgeRadius - and it is dithered against Grit, a value each cell
    /// keeps forever. Ground that shrugs the plague off goes on shrugging it off, so
    /// the falloff is a texture rather than a delay, and the boundary dissolves into
    /// speckle instead of being drawn. The two bands also have to *look* different,
    /// which is why the weak one holds plants back instead of taking them.
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
            /// difference the eye actually reads: the two bands used to strip
            /// identically, so the only thing telling them apart was pawn effects
            /// nobody watches, and both ended up looking like the same dead ground.
            public bool Strips;

            /// What the weak band knocks a plant's growth back to.
            public float StuntTo;
        }

        // The plague at the core. Detonation and ignition are both rare on purpose:
        // one is loud enough to be an event and the other does not stop when the roll
        // is over, so either of them landing often would be the whole map at once.
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
        // band reads as thin and struggling instead of as more dead ground.
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
            StepPlants();
        }

        float MapSide => Mathf.Max(map.Size.x, map.Size.z);
        float FullRadius => MapSide * FullFrac;
        float EdgeRadius => MapSide * EdgeFrac;

        /// <summary>How hard the plague pushes at a cell, before the cell gets a say:
        /// 1 inside FullRadius, falling off to 0 at EdgeRadius, and 0 anywhere the
        /// front has not reached yet. Clamped against EdgeRadius as well as against
        /// the live radius, so a colony saved before any of this existed - with a
        /// circle already swallowing the map - comes back inside the falloff.</summary>
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
        /// over, so a chance re-rolled every pass converges on certainty. That is
        /// precisely the failure this replaces - a per-plant coin flip still ends
        /// with every plant inside the radius dead, only later, and the map still
        /// finishes as one flat circle. Seeded off the cell instead, ground that
        /// shrugged the plague off keeps shrugging it off, through a reload as
        /// well, and the falloff stays a texture rather than a delay.</summary>
        float Grit(IntVec3 cell) =>
            Rand.ValueSeeded(Gen.HashCombineInt(cell.GetHashCode(), _seed));

        /// <summary>Which band a cell is in: the push, dithered against the cell's
        /// own dice. Full needs to beat the bite outright and Weak only its square
        /// root, which is the larger of the two - so the certain core is solid, and
        /// past it the ground breaks up into full, weak and untouched in shifting
        /// proportions until there is nothing left of any of it. Across the falloff
        /// that averages out at roughly 45% stripped and 18% stunted, but it is 100/0
        /// where the certain core ends, 50/21 halfway out and 25/25 at three quarters,
        /// and nowhere along it is there an edge you could trace.</summary>
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

                var dose = band == Band.Full ? Full : Weak;
                bool tree = p.def.plant.IsTree;

                // Whether this pass has anything left to do here. The list is rebuilt
                // every sweep, so every plant comes back round forever: without this a
                // bare tree would smoke again on each pass, and - worse - a plant the
                // weak band has already held back would roll for ignition again on
                // each pass until it eventually caught.
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
            // is, and immunity has to mean immunity. This blast is wide enough that
            // the check matters far more than it used to.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn, dose); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage, ignoredThings: Untouchable());
        }

        // What a blast steps around. The core first of all: it is where the plague
        // comes from and it stands in the middle of the band that detonates hardest,
        // so at this radius it would eventually blow a hole in its own origin - and
        // a missing core reads as a bug rather than as the plague working. Built per
        // blast rather than cached, because blasts are rare now and the list is not.
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
            // scenario's starters are the intro's to kill, and a starter the plague
            // got to first dies off camera and leaves its corpse - and its slot in
            // the colonist bar - behind. The pets fall out of the same check and are
            // meant to: see Pets, they outlive the map on purpose.
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
        /// Fire stays inside the plague. Without this the bands are a lie the moment
        /// anything ignites: a Fire is a Thing with its own tick, the strip does not
        /// touch it, and a rainforest carries one to the map edge in minutes - so the
        /// third of the map that is supposed to be untouched would burn instead.
        ///
        /// TrySpread picks its own cell internally, so this can only allow or refuse
        /// the whole attempt: a fire already outside the circle never spreads, which
        /// contains the burn to the plague's reach plus the one cell it crossed on
        /// the way out. Fires nothing to do with us - a blast, a short circuit on a
        /// map with no plague on it - are left alone.
        /// </summary>
        [HarmonyPatch(typeof(Fire), "TrySpread")]
        public static class Patch_ContainFire
        {
            static bool Prefix(Fire __instance)
            {
                var plague = __instance.Map?.GetComponent<Plague>();
                if (plague == null || !plague.Active) return true;
                return plague.Reaches(__instance.Position);
            }
        }
    }
}
