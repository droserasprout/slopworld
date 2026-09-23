using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Apply pawn effects according to the current plague band.
    // Plague.cs owns the field, clock, and saved state.
    public partial class Plague
    {
        // Use one random value per pawn to select at most one effect. Add the probabilities.
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            // Strip plants only in the full band to distinguish it from the weak band.
            public bool Strips;

            // Reduce plant growth to this value in the weak band.
            public float StuntTo;

            // Apply stunting only above this threshold. Plants continue to grow through Plant.TickLong.
            // The gap above StuntTo prevents small growth increments from causing repeated smoke, stunting, and ignition attempts.
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
            // Use a low ignition probability because the sweep can affect thousands of plants.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // The weak band excludes explosions and limits plant growth instead of removing plants.
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
                pawn.health.AddHediff(ModDefOf.SlopPlague);
                PlagueFx.Mark(pawn);
            }
        }

        // Use the pawn current position to select the dose.
        // The weak band reduces effects, and positions outside the field receive none.
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
                // Check aura protection even if the pawn still has the plague condition.
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
            // Use cut damage instead of an explosion when an agent is nearby.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn, dose); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage, ignoredThings: Untouchable());
        }

        // Exclude the core, pets, player buildings, and construction frames from explosion damage.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            spared.AddRange(map.listerBuildings.allBuildingsColonist.Cast<Thing>());
            spared.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return spared;
        }

        // Show plague gas before attaching fire to identify the cause.
        static void Ignite(Pawn pawn, Dose dose)
        {
            PlagueFx.Act(pawn);
            pawn.TryAttachFire(dose.FireSize, null);
        }

        // The game disables health ticks, so direct damage causes death instead of continued blood loss.
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

        // Check the job tracker before showing the effect. Skip pawns that already have a vomiting job.
        static void Vomit(Pawn pawn)
        {
            if (pawn.jobs == null) return;
            if (pawn.CurJobDef == JobDefOf.Vomit) return;
            PlagueFx.Act(pawn);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced);
        }

        static bool Marked(Pawn pawn) =>
            pawn.health?.hediffSet?.GetFirstHediffOfDef(ModDefOf.SlopPlague) != null;

        static bool Infectable(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;
            if (pawn.health?.hediffSet == null) return false;
            if (pawn.RaceProps == null) return false;
            if (!pawn.RaceProps.Animal && !pawn.RaceProps.Humanlike) return false;

            // Exclude the player faction, including agents and pets.
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
