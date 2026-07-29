using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // A circle centred on the core widens until it stops, biting hardest in the
    // middle: certain inside FullRadius, falling off to nothing by EdgeRadius.
    //
    // The bands must not have edges and must not converge. Hard radii draw a circle
    // you can trace with a finger, and a chance re-rolled each sweep still ends in
    // one flat dead disc, just later - so the falloff is dithered against Grit, a
    // value each cell keeps forever, and the weak band holds plants back instead of
    // taking them.
    //
    // Agents are immune wholly: an agent's colonist dying would leave its session
    // pointing at a corpse. Effects are applied directly because there is no sim -
    // Patch_Health skips health ticks, so nothing ever bleeds out on its own. Fire is
    // the exception: a Fire is a Thing with its own tick, which the strip does not
    // touch.
    public class Plague : MapComponent
    {
        // How the circle grows: a step every interval, until it hits EdgeFrac.
        const int SpreadInterval = 180;
        const float SpreadStep = 0.25f;
        const float StartRadius = 1f;

        // Fractions of the map's side rather than cells, so the shape holds on any map
        // size.
        const float FullFrac = 0.3f;
        const float EdgeFrac = 0.33f;

        // What is left over after the odds below is a quiet tick.
        const int EffectInterval = 300;

        // Rare and enormous rather than common and a firecracker.
        const float BlastRadius = 5.5f;
        const int BlastDamage = 200;
        const float BlastSafeRadius = 9f; // no closer to an agent than this, > BlastRadius

        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;

        // Withering is cheap, but a grown map has thousands of plants.
        const int PlantsPerTick = 5;

        // Three breaths a second of the smallest puff there is. Anything heavier is a fog
        // bank parked on the middle of the map, with everything the plague does out at
        // the edge read through it.
        const int VentInterval = 20;

        // Read from the cell, not from the mark, so a marked animal that wanders out of
        // reach goes quiet and starts up again when it wanders back.
        public enum Band { None, Weak, Full }

        // Probabilities are per roll and share one pass, so they add rather than compose.
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            // The difference the eye actually reads: stripping both bands identically left
            // nothing telling them apart but pawn effects nobody watches.
            public bool Strips;

            // What the weak band knocks a plant's growth back to.
            public float StuntTo;

            // The gap is load-bearing. Nothing stops a plant ticking here - the strip takes
            // needs, health, age and the storyteller, not Plant.TickLong - so a test against
            // StuntTo comes true again within one pass of the sweep: every weak plant puffed,
            // re-stunted by a fraction of a percent and re-rolled for ignition every half
            // minute, forever, for no visible change.
            public float StuntFrom;
        }

        // Detonation and ignition are both rare on purpose: one is loud enough to be an
        // event and the other does not stop when the roll is over.
        static readonly Dose Full = new Dose
        {
            PExplode = 0.015f,
            PIgnite = 0.020f,
            PBleed = 0.300f,
            PVomit = 0.250f,
            BleedMin = 8f,
            BleedMax = 18f,
            FireSize = 1.0f,
            // ~15k plants fall inside the circle on a default map and each is rolled once, so
            // this is a handful of ignitions over the whole first sweep.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // Nothing detonates out here: a blast is the plague at full strength and there is
        // no half of one. Plants are held back rather than taken, so the band reads as
        // thin instead of as more dead ground.
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
            StuntFrom = 0.45f,
        };

        // The seed is what makes the dither this colony's rather than every colony's:
        // without it two maps thin out through the exact same speckle.
        IntVec3 _origin = IntVec3.Invalid;
        float _radius;
        bool _active;
        int _seed;

        // Refilled when it runs off the end, which is also how regrowth gets caught.
        // Cleared rather than rebuilt: a lap is a minute and a grown map is tens of
        // thousands of plants.
        readonly List<Plant> _plants = new List<Plant>();
        int _plantIdx;

        // Effects has to walk a copy: an effect can despawn the pawn it lands on.
        readonly List<Pawn> _rolling = new List<Pawn>();

        // See Aura: the one thing on this map that takes ground back off the circle.
        Aura _aura;

        // Held rather than asked for every eighth tick, and dropped if it stops being
        // spawned.
        Thing _core;

        public Plague(Map map) : base(map) { }

        // Called by the intro once the core is standing; a reload picks up from the
        // persisted state.
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

        // The plume is what makes the thing in the middle of the map the source of what
        // is happening to it rather than a prop the plague was seeded next to.
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

        // Clamped against EdgeRadius as well as the live radius, so a colony saved before
        // any of this existed comes back inside the falloff.
        float Bite(IntVec3 cell)
        {
            float d = cell.DistanceTo(_origin);
            if (d > Mathf.Min(_radius, EdgeRadius)) return 0f;
            if (d <= FullRadius) return 1f;
            return 1f - (d - FullRadius) / Mathf.Max(EdgeRadius - FullRadius, 1f);
        }

        // Everything probabilistic reads this rather than Rand.Value, and it has to: the
        // plant sweep walks the whole map over and over, so a chance re-rolled every pass
        // converges on certainty. Seeded off the cell, ground that shrugged the plague
        // off keeps shrugging it off, through a reload as well.
        //
        // Hashed (lowbias32, top 24 bits) rather than Rand.ValueSeeded, which pushes the
        // global RNG state onto a stack, reseeds it, draws and pops.
        float Grit(IntVec3 cell)
        {
            uint h = (uint)Gen.HashCombineInt(cell.GetHashCode(), _seed);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return (h >> 8) * (1f / 16777216f);
        }

        // Full needs to beat the bite outright and Weak only its square root, so the
        // certain core is solid and past it the ground breaks up into full, weak and
        // untouched in shifting proportions - 100/0 where the core ends, 50/21 halfway
        // out, 25/25 at three quarters, and nowhere an edge you could trace.
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

        // Deliberately the plain geometry rather than BandAt: a fire that could not cross
        // a cell the dither happened to spare would never get anywhere.
        public bool Reaches(IntVec3 cell) =>
            _active && _origin.IsValid &&
            cell.DistanceTo(_origin) <= Mathf.Min(_radius, EdgeRadius);

        // Asked of a thing rather than a cell, because the grace outlives the cat walking
        // away and so belongs to what was standing there.
        bool Spared(Thing t) => (_aura ?? (_aura = Aura.Of(map)))?.Spares(t) == true;

        // A map with no plague running on it - the menu's background, an unfinished intro
        // - is not one whose fires we have any business containing.
        public bool Active => _active;

        // The circle stated rather than asked about, for Worksite: it is the one thing
        // here that wants to work *inside* the plague rather than be told whether it is
        // caught, and it puts its errands in the half of the circle nearest the core.
        public IntVec3 Heart => _origin;
        public float Reach => Mathf.Min(_radius, EdgeRadius);

        // The radius stops at the edge; the pass does not, because things wander.
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

        // Walked in slices and rebuilt at the end of each pass, so a plant that grew
        // since last time still gets its turn.
        void StepPlants()
        {
            if (_plantIdx >= _plants.Count)
            {
                _plants.Clear();
                var all = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] is Plant p) _plants.Add(p);
                _plantIdx = 0;
                return; // keep the refill off the same tick as the work
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

                // Every plant comes back round forever: without this a bare tree smokes again on
                // each pass, and a plant the weak band already held back rolls for ignition until
                // it catches. Which is why the weak band asks about StuntFrom rather than the
                // figure it stunts to.
                bool todo = dose.Strips
                    ? !(tree && p.LeaflessNow)
                    : !tree && p.Growth > dose.StuntFrom;
                if (!todo) continue;

                // After the todo check, not before: same answer, and in the steady state
                // nearly every plant the sweep walks past is one the band has finished with.
                if (Spared(p)) continue;

                // The roll goes first because TryStartFireIn weighs what is flammable in the
                // cell, and stripping the plant is what leaves nothing there to light.
                if (Rand.Value < dose.PlantIgnite &&
                    FireUtility.TryStartFireIn(p.Position, map, dose.FireSize, null))
                    continue;

                if (!dose.Strips)
                {
                    // Trees in the weak band are left alone entirely: a bare tree is the core's look,
                    // and giving the falloff one too is what made the two bands indistinguishable.
                    PlagueFx.Wither(p);
                    p.Growth = dose.StuntTo;
                    // Growth is printed into the map mesh and the setter does not dirty it, so a
                    // stunted plant keeps drawing at full size.
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

        // The dose comes from where it is standing now, so the same mark means less out
        // at the edge and nothing at all past it.
        void Effects()
        {
            _rolling.Clear();
            _rolling.AddRange(map.mapPawns.AllPawnsSpawned);

            foreach (var pawn in _rolling)
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position);
                if (band == Band.None) continue;
                // Belt and braces over Spread: a marked animal that walked into the aura is
                // unmarked at the aura's next sweep and not before, and a detonation in that half
                // second is the one thing the cat is for going visibly wrong.
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
            // The blast does not care who it is, and immunity has to mean immunity.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn, dose); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage, ignoredThings: Untouchable());
        }

        // The core first of all: it stands in the middle of the band that detonates
        // hardest, so at this radius it would eventually blow a hole in its own origin.
        //
        // Then everything the agents have built, finished or half-built, on the same
        // grounds NoBurningTheColony spares the whole player faction: a monument is an
        // hour of somebody's tokens, and a blast that takes it is the board deleting work
        // rather than the plague being dangerous.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            spared.AddRange(map.listerBuildings.allBuildingsColonist.Cast<Thing>());
            spared.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return spared;
        }

        // FireUtility declines quietly for anything that cannot burn or is already
        // alight. The haze goes up first, so the tell is the plague's rather than an
        // animal that happens to be on fire.
        static void Ignite(Pawn pawn, Dose dose)
        {
            PlagueFx.Act(pawn);
            pawn.TryAttachFire(dose.FireSize, null);
        }

        // No bleed-out without health ticks, so the damage is the death.
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

        // Checks whether it can land before it smokes: a puff over a pawn with no job
        // tracker would advertise an effect that never came.
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

            // Agents are immune by design, and the pets fall out of the same check and are
            // meant to. Nobody else on this map belongs to the player.
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

        // Every wild plant on a map arrives through this one method, so refusing the cell
        // is the whole of it. The sweep alone loses the race: the spawner refills behind
        // it, so the core would spend the colony's life growing grass and having it torn
        // out again.
        //
        // Gated on Band.Full rather than Reaches: the weak band has to keep growing the
        // plants it is only holding back, and a cell the dither spared is untouched
        // ground. Grit being stable is what makes a cell sterile forever or fertile
        // forever rather than flickering.
        [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
        public static class Patch_NoRegrowth
        {
            static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
            {
                var plague = ___map?.GetComponent<Plague>();
                if (plague == null || plague.BandAt(c) != Band.Full) return true;

                // Except where the cat is standing - the only thing that ever takes a cell back
                // off the core.
                if (Aura.Of(___map)?.Covers(c) == true) return true;

                __result = false;
                return false;
            }
        }

        // Without this the bands are a lie the moment anything ignites: a rainforest
        // carries a fire to the map edge in minutes. TrySpread picks its own cell
        // internally, so this can only allow or refuse the whole attempt.
        //
        // Containment is for a map with a future: NextPlanet is six seconds of this one
        // burning on the way out.
        [HarmonyPatch(typeof(Fire), "TrySpread")]
        public static class Patch_ContainFire
        {
            static bool Prefix(Fire __instance)
            {
                if (NextPlanet.Leaving) return true;

                var plague = __instance.Map?.GetComponent<Plague>();
                if (plague == null || !plague.Active) return true;
                // A fire under the cat is going out at the next sweep anyway; this stops it
                // taking the aura's plants with it.
                if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
                return plague.Reaches(__instance.Position);
            }
        }
    }
}
