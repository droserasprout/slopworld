using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Easter egg: Snoop visits at 04:20 and 16:20 local time to smoke smokeleaf.
    public class SnoopVisitor : GameComponent
    {
        const int MorningVisitMinute = 4 * 60 + 20;
        const int AfternoonVisitMinute = 16 * 60 + 20;

        const int VisitCheckIntervalTicks = 60;
        const int SmokeIntervalTicks = 600;
        const int JointStackCount = 40;

        const int MinStayTicks = 18000;
        const int MaxStayTicks = 36000;

        const string HairName = "Afro";
        const string SkinName = "Skin_Melanin9";
        const string JointName = "SmokeleafJoint";

        const string VisitorName = "Snoop";

        static readonly Color Hair = new Color(0.13f, 0.12f, 0.11f);

        long _morningVisitDay;
        long _afternoonVisitDay;
        int _leaveTick;
        Pawn _pawn;

        public SnoopVisitor(Game game) { }

        public override void GameComponentTick()
        {
            if (Settings.GrandmaMode) return;

            int tick = Find.TickManager.TicksGame;

            if (tick % SmokeIntervalTicks == 0) Smoke();
            if (tick % VisitCheckIntervalTicks != 0) return;
            if (Cutscene.AgentsHeld) return;

            if (_leaveTick > 0 && tick >= _leaveTick)
            {
                Leave();
                return;
            }

            var now = DateTime.Now;
            long today = now.Ticks / TimeSpan.TicksPerDay;
            double mins = now.TimeOfDay.TotalMinutes;

            bool morningDue = mins >= MorningVisitMinute && mins < AfternoonVisitMinute;
            bool afternoonDue = mins >= AfternoonVisitMinute;

            if (_morningVisitDay == 0 && _afternoonVisitDay == 0)
            {
                _morningVisitDay = morningDue ? today : today - 1;
                _afternoonVisitDay = afternoonDue ? today : today - 1;
            }

            if (morningDue && _morningVisitDay < today)
            {
                _morningVisitDay = today;
                Arrive(tick);
                return;
            }

            if (afternoonDue && _afternoonVisitDay < today)
            {
                _afternoonVisitDay = today;
                Arrive(tick);
            }
        }

        bool VisitorIsPresent => _pawn != null && !_pawn.Destroyed && !_pawn.Dead && _pawn.Spawned;

        void Leave()
        {
            var pawn = _pawn;
            bool around = VisitorIsPresent;
            _pawn = null;
            _leaveTick = 0;
            if (!around) return;
            pawn.DeSpawn();
            pawn.Destroy();
        }

        void Arrive(int tick)
        {
            if (VisitorIsPresent) return;

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
                ApplySnoopAppearance(pawn);
                GenSpawn.Spawn(pawn, cell, map);
                GetOrCreateJointStack(pawn);

                _pawn = pawn;
                _leaveTick = tick + Rand.Range(MinStayTicks, MaxStayTicks);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] Snoop visitor: {e.Message}");
            }
        }

        static void ApplySnoopAppearance(Pawn pawn)
        {
            if (pawn?.story == null) return;

            var last = (pawn.Name as NameTriple)?.Last ?? "";
            pawn.Name = new NameTriple(VisitorName, VisitorName, last);

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
            if (!VisitorIsPresent) return;

            var pawn = _pawn;
            if (pawn.Downed || pawn.jobs == null) return;

            if (pawn.CurJobDef == JobDefOf.Ingest)
            {
                if (pawn.CurJob?.targetA.Thing?.def?.defName == JointName) Puff(pawn);
                return;
            }

            var joint = GetOrCreateJointStack(pawn);
            if (joint == null) return;

            var job = JobMaker.MakeJob(JobDefOf.Ingest, joint);
            job.count = 1;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        static Thing GetOrCreateJointStack(Pawn pawn)
        {
            if (pawn?.inventory == null) return null;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(JointName);
            if (def == null) return null;

            var held = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == def);
            if (held != null) return held;

            var made = ThingMaker.MakeThing(def);
            made.stackCount = JointStackCount;
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
            Scribe_Values.Look(ref _morningVisitDay, "snoopMorningVisitDay", 0L);
            Scribe_Values.Look(ref _afternoonVisitDay, "snoopAfternoonVisitDay", 0L);
            Scribe_Values.Look(ref _leaveTick, "snoopLeaveTick", 0);
            Scribe_References.Look(ref _pawn, "snoopVisitorPawn");
        }
    }
}
