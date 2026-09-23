using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public partial class Worksite
    {
        // A run contains Least to Most items per row, with Lines rows and Gap cells between items.
        // Place all frames in one pass to reserve the ground before construction.
        // Calculate spacing from rotated footprints.
        struct Run
        {
            public int Least, Most, Lines, Gap;
        }

        struct Errand
        {
            public BuildableDef What;
            public float Seconds;
            public float Weight;
            public float Bloom; // cells of plague the finished thing seeds
            public Run Run; // how many go down at once, and in what shape
        }

        // Key these tables by the buildable definition because completed frames no longer exist.
        // Use static tables because Patch_ErrandWork has no map reference.
        static readonly Dictionary<BuildableDef, float> Work = new Dictionary<BuildableDef, float>();
        static readonly Dictionary<BuildableDef, float> Blooms = new Dictionary<BuildableDef, float>();

        // Initialize the table before reading work requirements.
        // After a load, Patch_ErrandWork can request this value before the next errand starts.
        public static float WorkFor(BuildableDef def)
        {
            if (def == null) return 0f;
            Ensure();
            return Work.TryGetValue(def, out float w) ? w : 0f;
        }

        public static float BloomFor(BuildableDef def)
        {
            if (def == null) return 0f;
            Ensure();
            return Blooms.TryGetValue(def, out float r) ? r : 0f;
        }

        static void Ensure() { if (_errands == null) { var _ = Errands; } }

        // The errand list is independent of the map. Blocks() selects stone for each map.
        static List<Errand> _errands;
        static bool _grandmaModeCached;
        static TerrainDef _plate;

        struct AssignmentState
        {
            public Dictionary<Pawn, Frame> Sent;
            public HashSet<Frame> Shunned;
            public List<Pawn> Hands;
            public List<Pawn> Stale;
        }

        // Keep assignment state only in memory. The game restores jobs and pawns when it loads a map.
        // Store this state with errand selection and cleanup.
        AssignmentState _assignments = new AssignmentState
        {
            Sent = new Dictionary<Pawn, Frame>(),
            Shunned = new HashSet<Frame>(),
            Hands = new List<Pawn>(),
            Stale = new List<Pawn>(),
        };

        Dictionary<Pawn, Frame> _sent => _assignments.Sent;
        HashSet<Frame> _shunned => _assignments.Shunned;
        List<Pawn> _hands => _assignments.Hands;
        List<Pawn> _stale => _assignments.Stale;

        static List<Errand> Errands
        {
            get
            {
                bool grandma = Settings.GrandmaMode;
                if (_errands != null && _grandmaModeCached == grandma) return _errands;
                _grandmaModeCached = grandma;
                _errands = new List<Errand>();

                // Use metal tiles for the industrial appearance.
                _plate = DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");

                Add(_plate, PavingSeconds, PavingOdds, PavingBloom,
                    new Run { Least = PavingSide, Most = PavingSide, Lines = PavingSide });

                if (grandma)
                {
                    // Use furniture and flower pots when Grandma's visiting.
                    Add(Named("FlowerPot"), SmallSeconds, 5f, SmallBloom,
                        new Run { Least = 3, Most = 6, Gap = 1 });
                    Add(Named("Chair"), SmallSeconds, 3f, SmallBloom,
                        new Run { Least = 2, Most = 5, Gap = 1 });
                    Add(Named("Armchair"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("Table1x2c"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("EndTable"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("Bookshelf"), MediumSeconds, 2f, MediumBloom);
                    Add(Named("Dresser"), MediumSeconds, 2f, MediumBloom);
                    Add(Named("Lamp"), SmallSeconds, 3f, SmallBloom);
                    Add(Named("Bed"), MediumSeconds, 2f, MediumBloom);
                }
                else
                {
                    Add(Named("Column"), SmallSeconds, ColumnOdds, SmallBloom);
                    Add(Named("Grave"), SmallSeconds, GraveOdds, SmallBloom,
                        new Run { Least = GraveRowLeast, Most = GraveRowMost, Gap = GraveAisle });
                    Add(Named("Sarcophagus"), MediumSeconds, SarcophagusOdds, MediumBloom);
                    Add(Named("SteleLarge"), LargeSeconds, SteleLargeOdds, LargeBloom);
                    Add(Named("SteleGrand"), MonumentSeconds, SteleGrandOdds, MonumentBloom);
                }

                // Patches/AncientBuildings.xml adds frames for these ancient buildings.
                // They require no resources or skills. AncientLamp supplies light without power or fuel.
                Add(Named("AncientLamp"), SmallSeconds, LampOdds, SmallBloom);
                Add(Named("AncientLamppost"), SmallSeconds, LamppostOdds, SmallBloom);
                Add(Named("AncientSystemRack"), MediumSeconds, RackOdds, MediumBloom);
                Add(Named("AncientDisplayBank"), MediumSeconds, ScreensOdds, MediumBloom);
                Add(Named("AncientLockerBank"), MediumSeconds, LockersOdds, MediumBloom);
                Add(Named("AncientGenerator"), MediumSeconds, GeneratorOdds, MediumBloom);
                Add(Named("AncientMachine"), MonumentSeconds, MachineOdds, MonumentBloom);

                if (_errands.Count == 0)
                    Log.Warning("[SlopWorld] This build has no errands to assign. Agents will wait.");

                return _errands;
            }
        }

        static void Add(BuildableDef what, float seconds, float weight, float bloom,
                        Run run = default(Run))
        {
            if (what == null || what.frameDef == null) return;

            // Use one item if the caller omits the run.
            run.Least = Mathf.Max(1, run.Least);
            run.Most = Mathf.Max(run.Least, run.Most);
            run.Lines = Mathf.Max(1, run.Lines);
            run.Gap = Mathf.Max(0, run.Gap);

            _errands.Add(new Errand
            {
                What = what,
                Seconds = seconds,
                Weight = weight,
                Bloom = bloom,
                Run = run,
            });
            Work[what] = seconds * RealClock.TicksPerRealSecond * WorkPerTick;
            Blooms[what] = bloom;
        }

        static ThingDef Named(string name) => DefDatabase<ThingDef>.GetNamedSilentFail(name);

        // Stop construction. Retain progress in the frame.
        void Stop(Pawn pawn)
        {
            _sent.Remove(pawn);
            if (pawn.jobs != null && pawn.CurJobDef == JobDefOf.FinishFrame)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);

            Allow(pawn, false);
        }

        // Enable construction only while the agent works.
        // Otherwise, base game work givers can assign jobs that conflict with daemon assignments.
        static void Allow(Pawn pawn, bool building)
        {
            var work = pawn.workSettings;
            if (work == null) return;
            work.EnableAndInitializeIfNotAlreadyInitialized();
            if (!work.EverWork) return;

            var types = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                if (pawn.WorkTypeIsDisabled(type)) continue;

                int want = building && type == WorkTypeDefOf.Construction ? 3 : 0;
                // Change priority only when necessary. The setter invalidates the pawn work giver lists.
                if (work.GetPriority(type) != want) work.SetPriority(type, want);
            }
        }

        void Send(Pawn pawn)
        {
            // Allow the pawn to finish moving away from a frame.
            // Assigning construction again can repeatedly interrupt this movement.
            if (pawn.CurJobDef == JobDefOf.Goto) return;

            if (!Ready(pawn)) return;

            Frame stale;
            if (_sent.TryGetValue(pawn, out stale))
            {
                _sent.Remove(pawn);
                if (stale != null && stale.Spawned) _shunned.Add(stale);
            }

            var frame = Free(pawn) ?? Open(pawn);
            if (frame == null) return;

            // Check newly placed frames too. A pawn can block a frame immediately after placement.
            if (!Buildable(frame, pawn)) return;

            Fill(frame);
            if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.FinishFrame, frame), JobTag.Misc))
                _sent[pawn] = frame;
        }

        // Check the job driver construction conditions before assignment. Disable the skill check here.
        static bool Buildable(Frame frame, Pawn pawn) =>
            GenConstruct.CanConstruct(frame, pawn, true, false);

        // Keep Construction enabled during the job because CanConstruct checks work settings each tick.
        // Patch_AgentsCanBuild permits construction regardless of pawn backstory.
        static bool Ready(Pawn pawn)
        {
            if (pawn.jobs == null || pawn.workSettings == null) return false;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return false;

            Allow(pawn, true);
            return true;
        }

        // Select the nearest frame that the pawn can reserve and construct.
        Frame Free(Pawn pawn)
        {
            Frame best = null;
            float nearest = float.MaxValue;

            EnsureFrameIndex();
            foreach (var frame in _frames)
            {
                if (frame == null || !frame.Spawned) continue;
                if (WorkFor(frame.def?.entityDefToBuild) <= 0f) continue;
                if (_shunned.Contains(frame)) continue;
                // Reject more distant frames before checking reservations and construction eligibility.
                float d = frame.Position.DistanceToSquared(pawn.Position);
                if (d >= nearest) continue;
                // Check reservations before construction eligibility.
                if (!pawn.CanReserve(frame)) continue;
                if (!Buildable(frame, pawn)) continue;

                nearest = d;
                best = frame;
            }

            return best;
        }

        // Start a sweep with a fixed frame limit.
        // Remove blocked frames so they do not permanently consume the open frame limit.
        void BeginSweep()
        {
            // Collect agents to check whether any can meet each frame skill requirement.
            _hands.Clear();
            var colony = AgentColony.Current;
            if (colony != null)
                foreach (var kv in colony.All)
                {
                    var agent = kv.Value;
                    if (agent != null && agent.Spawned && agent.Map == map && !agent.Dead)
                        _hands.Add(agent);
                }

            _sweepDoomed.Clear();
            _runtime.SweepCursor = 0;
            _runtime.SweepLimit = _frames.Count;
            _runtime.SweepPending = true;
        }

        const int SweepBudget = 64;

        void SweepSlice()
        {
            int end = Mathf.Min(_runtime.SweepLimit,
                _runtime.SweepCursor + SweepBudget);

            for (int i = _runtime.SweepCursor; i < end; i++)
            {
                var frame = _frames[i];
                if (frame == null || !frame.Spawned) continue;
                var what = frame.def?.entityDefToBuild;
                if (WorkFor(what) <= 0f) continue;

                if (GenConstruct.FirstBlockingThing(frame, null) == null && Anyone(what)) continue;
                _sweepDoomed.Add(frame);
            }

            _runtime.SweepCursor = end;
            if (end < _runtime.SweepLimit) return;

            // Clear excluded frames before removing blocked frames and frames that no agent can construct.
            _shunned.Clear();
            Prune();

            for (int i = 0; i < _sweepDoomed.Count; i++)
                if (!_sweepDoomed[i].Destroyed) _sweepDoomed[i].Destroy(DestroyMode.Vanish);

            // Reset failed placement attempts because frame removal can make space available.
            _blocked = 0;
            _sweepDoomed.Clear();
            _runtime.SweepPending = false;
            _runtime.FrameIndexDirty = true;
        }

        // Retain frames when no agents exist. Otherwise, require at least one agent with sufficient skills.
        bool Anyone(BuildableDef what)
        {
            if (_hands.Count == 0) return true;
            for (int i = 0; i < _hands.Count; i++)
                if (Skilled(what, _hands[i])) return true;
            return false;
        }

        // Remove assignments for pawns or frames that no longer exist on the map.
        void Prune()
        {
            if (_sent.Count == 0) return;

            _stale.Clear();
            foreach (var kv in _sent)
                if (kv.Key == null || !kv.Key.Spawned || kv.Value == null || !kv.Value.Spawned)
                    _stale.Add(kv.Key);

            for (int i = 0; i < _stale.Count; i++) _sent.Remove(_stale[i]);
        }

        // Select an errand by weight from those that the pawn has sufficient skills to complete.
        Errand? Pick(Pawn pawn)
        {
            var list = Errands;

            float total = 0f;
            for (int i = 0; i < list.Count; i++)
                if (Skilled(list[i].What, pawn)) total += list[i].Weight;
            if (total <= 0f) return null;

            float roll = Rand.Range(0f, total);
            Errand? last = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (!Skilled(list[i].What, pawn)) continue;
                last = list[i];
                roll -= list[i].Weight;
                if (roll <= 0f) return list[i];
            }
            return last;
        }

        // Check pawn skill requirements before a frame exists.
        static bool Skilled(BuildableDef what, Pawn pawn)
        {
            var skills = pawn?.skills;
            if (what == null || skills == null) return true;

            return what.constructionSkillPrerequisite
                       <= skills.GetSkill(SkillDefOf.Construction).Level
                && what.artisticSkillPrerequisite
                       <= skills.GetSkill(SkillDefOf.Artistic).Level;
        }
    }
}
