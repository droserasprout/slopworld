using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The symptoms half of the plague: the doses each band carries and the passes that act on
    // the pawns standing in them. The growth field, the clock and the save live in Plague.cs;
    // everything here reads the band and turns it into damage, fire, bleeding or a mark.
    public partial class Plague
    {
        // Probabilities are per roll and share one pass, so they add rather than compose.
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            // The difference the eye reads: stripping both bands alike left nothing to tell
            // them apart but pawn effects nobody watches.
            public bool Strips;

            // What the weak band knocks a plant's growth back to.
            public float StuntTo;

            // The gap is load-bearing. Nothing stops a plant ticking here (the strip takes
            // needs, health, age and the storyteller, not Plant.TickLong), so a test against
            // StuntTo comes true again within a pass: every weak plant puffed, re-stunted by
            // a fraction of a percent and re-rolled for ignition forever.
            public float StuntFrom;
        }

        static readonly Dose Full = new Dose
        {
            PExplode = 0.015f,
            PIgnite = 0.020f,
            PBleed = 0.300f,
            PVomit = 0.250f,
            BleedMin = 8f,
            BleedMax = 18f,
            FireSize = 1.0f,
            // ~15k plants inside the circle on a default map, each rolled once: a handful of
            // ignitions over the whole first sweep.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // Nothing detonates out here - there is no half of a blast - and plants are held
        // back rather than taken, so the band reads as thin instead of as more dead ground.
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

        void Catch()
        {
            Progress();
            int now = Find.TickManager.TicksGame;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Infectable(pawn)) continue;
                if (BandAt(pawn.Position, now) == Band.None) continue;
                if (Marked(pawn)) continue;
                if (Spared(pawn)) continue; // the cat has it, for now
                pawn.health.AddHediff(SlopDefOf.SlopPlague);
                PlagueFx.Mark(pawn);
            }
        }

        // The dose comes from where the pawn is standing now, so the same mark means less at
        // the edge and nothing past it.
        void Effects()
        {
            int now = Find.TickManager.TicksGame;
            _runtime.Rolling.Clear();
            _runtime.Rolling.AddRange(map.mapPawns.AllPawnsSpawned);

            foreach (var pawn in _runtime.Rolling)
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position, now);
                if (band == Band.None) continue;
                // A marked animal that walked into the aura is unmarked at the aura's next
                // sweep and not before; a detonation in that half second is the cat failing.
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

        // The core stands in the middle of the band that detonates hardest, so at this radius
        // it would eventually blow a hole in its own origin. Then everything the agents built,
        // on the same grounds NoBurningTheColony spares the player faction: a monument is an
        // hour of somebody's tokens.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            spared.AddRange(map.listerBuildings.allBuildingsColonist.Cast<Thing>());
            spared.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return spared;
        }

        // The haze goes up first, so the tell is the plague's rather than an animal that
        // happens to be on fire.
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

        // Checks it can land before it smokes: a puff over a pawn with no job tracker
        // advertises an effect that never comes.
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

            // Agents are immune by design and the pets fall out of the same check, meaning to.
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
    }
}
