using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public partial class Worksite
    {
        // A run is Least..Most items in Lines rows with Gap cells between them. The whole
        // run is pitched in one pass so its frames reserve the ground before construction;
        // spacing comes from the rotated footprint rather than duplicated def dimensions.
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

        // Keyed on what is being built, not on the frame, which is gone by the time
        // anything asks twice. Static: Patch_ErrandWork has no map to ask.
        static readonly Dictionary<BuildableDef, float> Work = new Dictionary<BuildableDef, float>();
        static readonly Dictionary<BuildableDef, float> Blooms = new Dictionary<BuildableDef, float>();

        // Ensure() here rather than at the next errand: a load comes back with frames on
        // the board and pawns already swinging, and the first thing to ask about one is
        // Patch_ErrandWork. An empty table there is a plate costing vanilla's 1100 ticks.
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

        // Nothing on the list is the map's; the stone is, and Blocks() settles that.
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

        // Assignment state is transient: jobs and pawns are rebuilt by the game around a
        // map load. Keep it beside the errand selection and its cleanup, rather than with
        // the quarry's persistent material and placement state.
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

                // Metal plate rather than the tile's flagstone, which read as a garden path
                // through a dead world.
                _plate = DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");

                Add(_plate, PavingSeconds, PavingOdds, PavingBloom,
                    new Run { Least = PavingSide, Most = PavingSide, Lines = PavingSide });

                if (grandma)
                {
                    // Grandma mode: furniture and flower pots instead of graves and obelisks.
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

                // Ruin scenery vanilla lets no player build; Patches/AncientBuildings.xml is
                // what hands them a frame. They cost nothing and want no skill. The lamp is
                // the only one that is not decoration - a CompGlower with neither a power
                // comp nor a fuel one, the one light in the game that simply burns.
                Add(Named("AncientLamp"), SmallSeconds, LampOdds, SmallBloom);
                Add(Named("AncientLamppost"), SmallSeconds, LamppostOdds, SmallBloom);
                Add(Named("AncientSystemRack"), MediumSeconds, RackOdds, MediumBloom);
                Add(Named("AncientDisplayBank"), MediumSeconds, ScreensOdds, MediumBloom);
                Add(Named("AncientLockerBank"), MediumSeconds, LockersOdds, MediumBloom);
                Add(Named("AncientGenerator"), MediumSeconds, GeneratorOdds, MediumBloom);
                Add(Named("AncientMachine"), MonumentSeconds, MachineOdds, MonumentBloom);

                if (_errands.Count == 0)
                    Log.Warning("[SlopWorld] no errands this build knows how to build; agents will stand about");

                return _errands;
            }
        }

        static void Add(BuildableDef what, float seconds, float weight, float bloom,
                        Run run = default(Run))
        {
            if (what == null || what.frameDef == null) return;

            // So an omitted run is one thing on its own rather than none of it.
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

        // An agent gone quiet puts the hammer down where it stands; the frame keeps what
        // it has been given.
        void Stop(Pawn pawn)
        {
            _sent.Remove(pawn);
            if (pawn.jobs != null && pawn.CurJobDef == JobDefOf.FinishFrame)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);

            Allow(pawn, false);
        }

        // Override vanilla work only while the process is busy; otherwise its work givers
        // fight daemon assignments and make pawns turn between sweeps.
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
                // Only on a change: the setter dirties the pawn's work giver lists, and this
                // is asked of every agent four times a second.
                if (work.GetPriority(type) != want) work.SetPriority(type, want);
            }
        }

        void Send(Pawn pawn)
        {
            // Vanilla has sent the pawn to stand clear of a frame so it can be built.
            // Forcing the errand back over that job is how a clanker walks on the spot.
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

            // Asked of an opened frame too: one can be blocked the moment it is placed, by
            // whoever wandered past or by the pawn that asked for it.
            if (!Buildable(frame, pawn)) return;

            Fill(frame);
            if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.FinishFrame, frame), JobTag.Misc))
                _sent[pawn] = frame;
        }

        // Use the driver's fail condition one tick early and with skills disabled; otherwise
        // an under-skilled pawn receives an unreachable frame and retries it forever.
        static bool Buildable(Frame frame, Pawn pawn) =>
            GenConstruct.CanConstruct(frame, pawn, true, false);

        // Construction has to stay *on* while the hammer swings: CanConstruct reads the
        // work settings and the driver fails on that every tick. Patch_AgentsCanBuild is
        // what lets the answer be yes whatever backstory the pawn was handed.
        static bool Ready(Pawn pawn)
        {
            if (pawn.jobs == null || pawn.workSettings == null) return false;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return false;

            Allow(pawn, true);
            return true;
        }

        // Free = unreserved, which is the same question the driver asks on the way in.
        // Nearest first, so two agents do not cross the map past each other.
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
                // Cheap first: a dictionary lookup before a path.
                if (!pawn.CanReserve(frame)) continue;
                if (!Buildable(frame, pawn)) continue;

                float d = frame.Position.DistanceToSquared(pawn.Position);
                if (d >= nearest) continue;
                nearest = d;
                best = frame;
            }

            return best;
        }

        // Start a bounded sweep. Exclude frames blocked by vanilla's first blocking thing:
        // they would consume MaxOpen forever, and no agent or hauler can remove the blocker.
        void BeginSweep()
        {
            // A frame nobody on the map is skilled enough for is rubbish the same way.
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

            // The ones that deserved shunning for good have just been destroyed.
            _shunned.Clear();
            Prune();

            for (int i = 0; i < _sweepDoomed.Count; i++)
                if (!_sweepDoomed[i].Destroyed) _sweepDoomed[i].Destroy(DestroyMode.Vanish);

            // Room where there was none; the last round of darts is out of date.
            _blocked = 0;
            _sweepDoomed.Clear();
            _runtime.SweepPending = false;
            _runtime.FrameIndexDirty = true;
        }

        // An empty colony is not the same answer as a colony of clumsy hands.
        bool Anyone(BuildableDef what)
        {
            if (_hands.Count == 0) return true;
            for (int i = 0; i < _hands.Count; i++)
                if (Skilled(what, _hands[i])) return true;
            return false;
        }

        // A dictionary keyed on pawns would hold every agent that ever landed.
        void Prune()
        {
            if (_sent.Count == 0) return;

            _stale.Clear();
            foreach (var kv in _sent)
                if (kv.Key == null || !kv.Key.Spawned || kv.Value == null || !kv.Value.Spawned)
                    _stale.Add(kv.Key);

            for (int i = 0; i < _stale.Count; i++) _sent.Remove(_stale[i]);
        }

        // Weighted, and only over what the pawn asking could finish - a clanker that opens
        // a frame beyond its own hands has built the trap it walks into.
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

        // The half of CanConstruct that is about the pawn rather than the ground - the
        // only half that can be asked before the frame exists.
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
