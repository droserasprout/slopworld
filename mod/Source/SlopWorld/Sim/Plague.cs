using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    /// <summary>
    /// What the machine persona does to a living world. A circle centred on the core
    /// widens until it covers the map; anything alive inside it is marked, and marked
    /// things come apart - bleeding, detonating, retching, seizing up. Plants do not
    /// get a slow death: a tree the circle reaches goes bare, everything smaller is
    /// gone. Whatever wanders in later, or grows back, is caught by a later sweep,
    /// because the circle never shrinks.
    ///
    /// Agents are immune, wholly: never marked, never harmed, and never standing near
    /// a detonation we chose to start. An agent's colonist dying would leave its
    /// session pointing at a corpse.
    ///
    /// Effects are applied directly rather than left to the sim, because there is no
    /// sim: Patch_Health skips health ticks, so a hediff never worsens and nothing
    /// ever bleeds out on its own. The mark is a tag; the killing is done here.
    ///
    /// MapComponents are instantiated for every subclass, so this needs no def.
    /// </summary>
    public class Plague : MapComponent
    {
        // How the circle grows: a step every interval, forever.
        const int SpreadInterval = 60;
        const float SpreadStep = 4f;
        const float StartRadius = 4f;

        // How often a marked thing rolls for an effect, and the odds of each. What
        // is left over is a quiet tick.
        const int EffectInterval = 300;
        const float PExplode = 0.08f;
        const float PBleed = 0.25f;
        const float PVomit = 0.25f;
        const float PStuck = 0.20f;

        // How hard each effect lands.
        const float BlastRadius = 1.9f;
        const int BlastDamage = 60;
        const float BlastSafeRadius = 4.5f; // no closer to an agent than this
        const float BleedMin = 8f;
        const float BleedMax = 18f;
        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;
        const int SeizeTicksMin = 120;
        const int SeizeTicksMax = 420;

        // Withering is cheap, but a grown map has thousands of plants.
        const int PlantsPerTick = 1;

        // Persisted.
        IntVec3 _origin = IntVec3.Invalid;
        float _radius;
        bool _active;

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
            _active = true;
            Log.Message($"[SlopWorld] plague seeded at {origin}");
        }

        public override void MapComponentTick()
        {
            if (!_active) return;

            int t = Find.TickManager.TicksGame;
            if (t % SpreadInterval == 0) Spread();
            if (t % EffectInterval == 0) Effects();
            StepPlants();
        }

        // Widen the circle and mark every living thing that now falls inside it.
        void Spread()
        {
            _radius += SpreadStep;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Infectable(pawn)) continue;
                if (!InCircle(pawn.Position)) continue;
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
                if (p.def.plant == null || !InCircle(p.Position)) continue;

                if (p.def.plant.IsTree)
                {
                    // Already bare is already dealt with: the list is rebuilt every
                    // pass, so a tree comes back round forever and would smoke on
                    // each one.
                    if (p.LeaflessNow) continue;
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
        void Effects()
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                float roll = Rand.Value;
                if ((roll -= PExplode) < 0f) Detonate(pawn);
                else if ((roll -= PBleed) < 0f) Bleed(pawn);
                else if ((roll -= PVomit) < 0f) Vomit(pawn);
                else if (roll - PStuck < 0f) Seize(pawn);
            }
        }

        void Detonate(Pawn pawn)
        {
            // Never light one off next to an agent: the blast does not care who it
            // is, and immunity has to mean immunity.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage);
        }

        // No bleed-out without health ticks, so the damage is the death: a few of
        // these in a row and whatever it is falls over.
        void Bleed(Pawn pawn)
        {
            var pos = pawn.Position;
            PlagueFx.Act(pawn); // before the damage, which may be the one that drops it
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, Rand.Range(BleedMin, BleedMax)));

            int cells = GenRadial.NumCellsInRadius(BloodRadius);
            for (int i = 0; i < BloodPerBleed; i++)
            {
                var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (c.InBounds(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, pawn.LabelShort, 1);
            }
        }

        // The two harmless effects check whether they can land before they smoke:
        // a pawn with no job tracker, or one already retching, is a roll that did
        // nothing, and a puff over it would advertise an effect that never came.
        static void Vomit(Pawn pawn)
        {
            if (pawn.jobs == null) return;
            if (pawn.CurJobDef == JobDefOf.Vomit) return;
            PlagueFx.Act(pawn);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced);
        }

        static void Seize(Pawn pawn)
        {
            var stunner = pawn.stances?.stunner;
            if (stunner == null) return;
            PlagueFx.Act(pawn);
            stunner.StunFor(Rand.Range(SeizeTicksMin, SeizeTicksMax), null);
        }

        bool InCircle(IntVec3 cell) =>
            _origin.IsValid && cell.DistanceTo(_origin) <= _radius;

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
            // the colonist bar - behind.
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
        }
    }
}
