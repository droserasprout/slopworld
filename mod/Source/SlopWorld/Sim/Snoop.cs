using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public class Snoop : GameComponent
    {
        const int Hour1 = 4;
        const int Minute1 = 20;

        const int Hour2 = 16;
        const int Minute2 = 20;

        const int Interval = 60;
        const int SmokeInterval = 600;
        const int Stash = 40;

        const int StayMin = 18000;  // 5 min in game ticks
        const int StayMax = 36000;  // 10 min in game ticks

        const string HairName = "Afro";
        const string SkinName = "Skin_Melanin9";
        const string JointName = "SmokeleafJoint";

        const string Handle = "Snoop";

        static readonly Color Hair = new Color(0.13f, 0.12f, 0.11f);

        long _lastDay4;
        long _lastDay16;
        int _leaveTick;
        Pawn _pawn;

        public Snoop(Game game) { }

        public override void GameComponentTick()
        {
            // Grandma mode: no easter eggs.
            if (Settings.GrandmaMode) return;

            int tick = Find.TickManager.TicksGame;

            if (tick % SmokeInterval == 0) Smoke();
            if (tick % Interval != 0) return;
            if (Cutscene.AgentsHeld) return;

            // Leave after 5-10 minutes
            if (_leaveTick > 0 && tick >= _leaveTick)
            {
                Leave();
                return;
            }

            var now = DateTime.Now;
            long today = now.Ticks / TimeSpan.TicksPerDay;
            double mins = now.TimeOfDay.TotalMinutes;

            bool due4 = mins >= Hour1 * 60 + Minute1 && mins < Hour2 * 60 + Minute2;
            bool due16 = mins >= Hour2 * 60 + Minute2;

            if (_lastDay4 == 0 && _lastDay16 == 0)
            {
                _lastDay4 = due4 ? today : today - 1;
                _lastDay16 = due16 ? today : today - 1;
            }

            if (due4 && _lastDay4 < today)
            {
                _lastDay4 = today;
                Arrive(tick);
                return;
            }

            if (due16 && _lastDay16 < today)
            {
                _lastDay16 = today;
                Arrive(tick);
            }
        }

        bool Around => _pawn != null && !_pawn.Destroyed && !_pawn.Dead && _pawn.Spawned;

        void Leave()
        {
            if (!Around) return;
            _pawn.DeSpawn();
            _pawn.Destroy();
            _pawn = null;
            _leaveTick = 0;
            Log.Message($"[SlopWorld] {Handle} left at {DateTime.Now:HH:mm}");
        }

        void Arrive(int tick)
        {
            if (Around) return;

            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var plague = map.GetComponent<Plague>();
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var cell, map,
                    CellFinder.EdgeRoadChance_Neutral, false,
                    c => plague == null || !plague.Reaches(c)))
                return;

            try
            {
                var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, null,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    colonistRelationChanceFactor: 0f,
                    allowAddictions: false,
                    fixedGender: Gender.Male);

                var pawn = PawnGenerator.GeneratePawn(req);
                Dress(pawn);
                GenSpawn.Spawn(pawn, cell, map);
                Joint(pawn);

                _pawn = pawn;
                _leaveTick = tick + Rand.Range(StayMin, StayMax);
                Log.Message($"[SlopWorld] {Handle} walked in at {DateTime.Now:HH:mm}, staying {_leaveTick - tick} ticks");
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] snoop: {e.Message}");
            }
        }

        static void Dress(Pawn pawn)
        {
            if (pawn?.story == null) return;

            var last = (pawn.Name as NameTriple)?.Last ?? "";
            pawn.Name = new NameTriple(Handle, Handle, last);

            var hair = DefDatabase<HairDef>.GetNamedSilentFail(HairName);
            if (hair != null) pawn.story.hairDef = hair;
            pawn.story.HairColor = Hair;

            var skin = DefDatabase<GeneDef>.GetNamedSilentFail(SkinName);
            if (skin != null && pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading.ToList())
                    if (gene.def != null && gene.def.endogeneCategory == EndogeneCategory.Melanin)
                        pawn.genes.RemoveGene(gene);

                pawn.genes.AddGene(skin, false);
                if (skin.skinColorBase.HasValue)
                    pawn.story.SkinColorBase = skin.skinColorBase.Value;
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        void Smoke()
        {
            if (!Around) return;

            var pawn = _pawn;
            if (pawn.Downed || pawn.jobs == null) return;

            if (pawn.CurJobDef == JobDefOf.Ingest)
            {
                Puff(pawn);
                return;
            }

            var joint = Joint(pawn);
            if (joint == null) return;

            var job = JobMaker.MakeJob(JobDefOf.Ingest, joint);
            job.count = 1;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        static Thing Joint(Pawn pawn)
        {
            if (pawn?.inventory == null) return null;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(JointName);
            if (def == null) return null;

            var held = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == def);
            if (held != null) return held;

            var made = ThingMaker.MakeThing(def);
            made.stackCount = Stash;
            return pawn.inventory.innerContainer.TryAdd(made) ? made : null;
        }

        static void Puff(Pawn pawn)
        {
            if (TerminalWindow.Covering) return;

            var map = pawn.Map;
            if (map == null) return;

            var at = pawn.DrawPos + new Vector3(Rand.Range(-0.15f, 0.15f), 0f, 0.25f);
            if (!GenView.ShouldSpawnMotesAt(at, map)) return;

            FleckMaker.ThrowSmoke(at, map, Rand.Range(0.5f, 0.9f));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _lastDay4, "snoopDay4", 0L);
            Scribe_Values.Look(ref _lastDay16, "snoopDay16", 0L);
            Scribe_Values.Look(ref _leaveTick, "snoopLeave", 0);
            Scribe_References.Look(ref _pawn, "snoopPawn");
        }
    }
}