using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Errands for agents whose process is busy. Everything finished emits plague
    // (Plague.Bloom), and the ring of living ground the site works in is opened by its own
    // work (Roam). The one system here that leaves its effects to the vanilla job driver;
    // the map has no economy, so a frame arrives with its stone already in it (Fill).
    public class Worksite : MapComponent
    {
        // Four times a second, off AgentColony's own second so the reconcile and the
        // errands never land on the same tick. A plate takes less than a tick to lay and
        // the errand is the whole of what the pawn does next, so once a second is a
        // clanker standing over a finished floor waiting for the next cell.
        const int Interval = 15;
        const int Phase = 7;

        // Darts at the circle rather than a search: most of the ground is fine.
        const int Tries = 30;

        // How long the site sits down after a round of darts found nowhere to build.
        const int BlockedFor = 300;

        // Frames outlive the burst that opened them. Generous, a paving square being
        // fifty frames on its own.
        const int MaxOpen = 96;

        // What a tick of construction is worth to a middling clanker (the driver pays
        // ConstructionSpeed * 1.7, stone knocks it back). Only here to turn the seconds
        // below into a work figure; the pawn's own stat is what spends it.
        const float WorkPerTick = 1.4f;

        // Real seconds of an agent's *working* time. Paving is instant in the hand - the
        // walk to the next cell is the whole cost.
        const float PavingSeconds = 0.1f;
        const float SmallSeconds = 4f;
        const float MediumSeconds = 8f;
        const float LargeSeconds = 15f;
        const float MonumentSeconds = 30f;

        // Floors are laid a square at a time, or every errand is one cell and the agent
        // spends the burst walking between them.
        const int PavingSide = 7;

        // Graves come as a row: all facing the same way, a cell of grass between each. Any
        // errand can ask for the same shape - see Run.
        const int GraveRowLeast = 5;
        const int GraveRowMost = 10;
        const int GraveAisle = 1;

        // How far from the pawn an errand may be opened. The near edge is not decoration:
        // a pawn standing inside a frame *blocks* it, vanilla answers with a job to go and
        // stand elsewhere, and this loop forces the errand back over the top a quarter
        // second later - a clanker that re-paths forever. The pad in Fits is the other half.
        const float SiteNear = 4f;
        const float SiteRadius = 12f;

        // How hard a dart is pulled toward the core, so ground by the core fills first. A
        // weight rather than a rule: when the inside is full the darts still have to reach
        // the outside. Fades out on its own as the pawn nears the heart.
        const float SiteLean = 0.8f;

        // The ring of living ground outside the plague, and where everything gets built.
        // Cannot stall or run away: building blooms, blooming grows the plague, and the
        // plague's girth is what this is measured off. Girth is a bulk rather than a
        // furthest reach (see Plague.Girth), so the next ring costs ground that really died.
        const float RoamMargin = 16f;

        // Floor under Roam, so the plague's first seconds do not hem the agents into the core.
        const float MinCircle = 18f;

        // Share of the errands each thing is, out of a hundred - the whole of the tuning.
        // Nothing enforces the sum; Pick normalises whatever it is handed, and must,
        // because it weighs only what the pawn in front of it could finish. Shares by
        // *count*, not by time: a plate is a tenth of a second where a grand stele is
        // thirty, so at even odds the agents would spend nine tenths of a burst on shapes.
        const float PavingOdds = 10f;

        const float ColumnOdds = 2f;
        const float GraveOdds = 5f;
        const float SarcophagusOdds = 1f;
        const float SteleLargeOdds = 1f;
        const float SteleGrandOdds = 1f;

        // One lamp lights a good few cells and a field of them lights the same ground
        // over and over, hence the lamppost's share.
        const float LampOdds = 5f;
        const float LamppostOdds = 1f;
        const float RackOdds = 2f;
        const float ScreensOdds = 2f;
        const float LockersOdds = 2f;
        const float GeneratorOdds = 3f;
        const float MachineOdds = 1f;

        // How far the plague walks out of each thing once it stands, in cells - the other
        // half of the tuning. Kept roughly flat per second of working time, so the map
        // dies at the speed the sessions are busy rather than at the speed of whichever
        // errand the darts favoured.
        const float PavingBloom = 3f;
        const float SmallBloom = 4f;
        const float MediumBloom = 6f;
        const float LargeBloom = 8f;
        const float MonumentBloom = 11f;

        // How a thing stands with its own kind: a line of Least..Most of them, Lines such
        // lines side by side, Gap cells clear between neighbours along the line and
        // between the lines. Zeroes mean one thing on its own, which is what most errands
        // are, so an errand wanting nothing special says nothing (Add normalises).
        //
        // The whole sequence is pitched in the same pass, and a frame is a building as far
        // as Fits and the next round of darts are concerned, so the ground under the run
        // is claimed before the first of them is finished. One at a time, a row of graves
        // would grow a stele through the middle of it. The spacing is read off the thing's
        // own footprint rather than stated here, so the table never has to keep a figure
        // in step with a def.
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
        ThingDef _blocks;
        ThingDef _rock;
        bool _quarried;

        // Held for the length of a pass: asked once per candidate cell, fifty per square.
        Plague _plague;

        // The tick placement may be attempted again. Not saved.
        int _blocked;

        int _swept;

        // Cells the site has finished with. Scribed; printed by the plague's log line and
        // nothing else - what the plague has is in the plague's own field.
        int _laid;

        // The frame each agent was last sent to, and the frames being passed over. Being
        // asked for an errand at all means the last job did not survive a quarter second
        // (a pass that finds the pawn on FinishFrame leaves it alone), so whatever we were
        // told about that frame was wrong and it waits for the next sweep. Otherwise two
        // systems disagree about one frame for the life of the colony.
        readonly Dictionary<Pawn, Frame> _sent = new Dictionary<Pawn, Frame>();
        readonly HashSet<Frame> _shunned = new HashSet<Frame>();

        readonly List<Pawn> _hands = new List<Pawn>();
        readonly List<Pawn> _stale = new List<Pawn>();

        // The frames pitched so far in the sequence being laid. Fits lets these through
        // its pad, or the second grave of a row is refused by the first.
        readonly HashSet<Thing> _mine = new HashSet<Thing>();

        // Passes between sweeps - once a second.
        const int SweepEvery = 4;

        public Worksite(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _laid, "laid", 0);
        }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + Phase) % Interval != 0) return;

            if (Cutscene.AgentsHeld) return;

            _plague = map.GetComponent<Plague>();
            if (_plague == null || !_plague.Active) return;

            if (++_swept >= SweepEvery) { _swept = 0; Sweep(); }

            var colony = AgentColony.Current;
            if (colony == null) return;

            var hub = SessionHub.Instance;

            foreach (var kv in colony.All)
            {
                var pawn = kv.Value;
                if (pawn == null || !pawn.Spawned || pawn.Map != map) continue;
                if (pawn.Dead || pawn.Downed) continue;

                var state = hub.Get(kv.Key)?.State ?? AgentState.Down;
                if (state != AgentState.Working) { _sent.Remove(pawn); Stop(pawn); continue; }

                // Already at it. The top-up is not idle work: a failed construction empties
                // the frame, and vanilla answers that with a hauler this map has not got.
                if (pawn.CurJobDef == JobDefOf.FinishFrame)
                {
                    Fill(pawn.CurJob?.targetA.Thing as Frame);
                    continue;
                }

                Send(pawn);
            }
        }

        // An agent gone quiet puts the hammer down where it stands; the frame keeps what
        // it has been given.
        static void Stop(Pawn pawn)
        {
            if (pawn.jobs != null && pawn.CurJobDef == JobDefOf.FinishFrame)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);

            Allow(pawn, false);
        }

        // One kind of work, and only while the process is busy. On the vanilla work sheet
        // the work givers find their own jobs (blood to clean, steel to haul) and this
        // loop overrides them a quarter second later, so the pawn turns round every few
        // steps. Construction is on the same switch from the other side: left on while
        // idle, the work giver hands the agent the nearest frame and the site stops saying
        // which processes are busy.
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
            if (!Ready(pawn)) return;

            // Vanilla has sent the pawn to stand clear of a frame so it can be built.
            // Forcing the errand back over that job is how a clanker walks on the spot.
            if (pawn.CurJobDef == JobDefOf.Goto) return;

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

        // The driver's own fail condition, asked one tick early and asked *the same way*.
        // Asked with skills off, a sarcophagus (Construction 5) is handed to a clanker
        // with three, which walks the site, is refused on arrival - the fail condition
        // sits on the build toil, not the walk - and is handed the same nearest unreserved
        // frame a quarter second later, forever.
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

            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                var frame = thing as Frame;
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

        Frame Open(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            if (now < _blocked) return null;
            if (Standing() >= MaxOpen) return null;

            var pick = Pick(pawn);
            if (pick == null) return null;
            var errand = pick.Value;

            for (int i = 0; i < Tries; i++)
            {
                var frame = Lay(errand, Site(pawn), pawn);
                if (frame != null) return frame;
            }

            // Only the floor's failure sits the site down. A plate is one cell wanting no
            // clearance, so nowhere to lay one means a full circle - the state this is all
            // aimed at, and it must be cheap to be in (otherwise 500 CanPlaceBlueprintAt a
            // second against ground that will not change). Anything with a shape failing
            // says only that there is no room for *it* near this pawn.
            if (errand.What is TerrainDef) _blocked = now + BlockedFor;
            return null;
        }

        // A frame nobody can finish counts against MaxOpen and nothing else would ever take
        // it away, so a site left alone fills its own quota with rubbish. Blocked is
        // GenConstruct.FirstBlockingThing - vanilla's own question, whose answer is
        // something to cut down or carry off, which here is work no agent is allowed and no
        // hauler exists to do. Fits declines these on the way in, but only what it can see;
        // this is for the ground changing afterwards.
        void Sweep()
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

            List<Thing> doomed = null;

            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                var frame = thing as Frame;
                if (frame == null || !frame.Spawned) continue;
                var what = frame.def?.entityDefToBuild;
                if (WorkFor(what) <= 0f) continue;

                if (GenConstruct.FirstBlockingThing(frame, null) == null && Anyone(what)) continue;

                if (doomed == null) doomed = new List<Thing>();
                doomed.Add(frame);
            }

            // The ones that deserved shunning for good have just been destroyed.
            _shunned.Clear();
            Prune();

            if (doomed == null) return;

            for (int i = 0; i < doomed.Count; i++)
                if (!doomed[i].Destroyed) doomed[i].Destroy(DestroyMode.Vanish);

            // Room where there was none; the last round of darts is out of date.
            _blocked = 0;
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

        int Standing()
        {
            int n = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                if (WorkFor((thing as Frame)?.def?.entityDefToBuild) > 0f) n++;
            return n;
        }

        public static int LaidOn(Map map) => map?.GetComponent<Worksite>()?._laid ?? 0;

        // Footprint, so ground taken rather than work done: an hour on a grand stele is
        // one cell.
        void Count(int cells) => _laid += cells;

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

        // Next to whoever is asking, so the ground fills the way the colony moves over it
        // and the walk out is a few steps. A pawn past the leash is aimed back inside it,
        // or it spends every look asking for ground it is not allowed.
        // Round rather than truncate, or the sites lean half a cell in both axes.
        IntVec3 Site(Pawn pawn)
        {
            bool home = Near(pawn.Position);
            var from = home ? pawn.Position : Heart();

            // An annulus rather than a disc when aimed at the pawn: the middle of that
            // disc is the cell the pawn is standing in.
            var dir = Rand.InsideUnitCircleVec3.normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            dir = (dir + Pull(pawn.Position)).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            var v = home
                ? dir * Rand.Range(SiteNear, SiteRadius)
                : Rand.InsideUnitCircleVec3 * Roam();

            return from + new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));
        }

        // A bias on a direction, not a distance: a pawn far out pulls no harder than one
        // nearby, and zero within a step of the heart so the pull never needs switching off.
        Vector3 Pull(IntVec3 from)
        {
            var to = (Heart() - from).ToVector3();
            return to.magnitude < 1f ? Vector3.zero : to.normalized * SiteLean;
        }

        // The plague as a circle plus the living ring outside it. A distance rather than the
        // plague's own shape: the region is a union of thousands of stamps, and "within a
        // few cells of somewhere dead" is hundreds of lookups per candidate where this is one.
        float Roam() => _plague == null ? 0f : Mathf.Max(_plague.Girth + RoamMargin, MinCircle);

        IntVec3 Heart() => _plague == null ? map.Center : _plague.Heart;

        bool Near(IntVec3 cell) =>
            _plague != null && _plague.Active && cell.DistanceTo(_plague.Heart) <= Roam();

        // One sequence, centred on the dart. Every member faces the same way - the facing
        // is rolled once, for the run, not once per thing - and the line runs *across* that
        // facing, so a row of graves is a row of graves rather than a queue of them. A
        // single is the same code with a run of one, which is why there is only this.
        //
        // A member that does not fit is skipped rather than ending the run: the far end of
        // a row reaching a boulder should cost the row its far end, not the whole colony a
        // dart. The run is what is *offered*; what stands is what the ground allowed.
        Frame Lay(Errand errand, IntVec3 at, Pawn pawn)
        {
            var td = errand.What as ThingDef;
            var rot = td != null && td.rotatable ? Rot4.Random : Rot4.North;

            var run = errand.Run;
            int many = Rand.RangeInclusive(run.Least, run.Most);

            var span = GenAdj.OccupiedRect(IntVec3.Zero, rot, errand.What.Size).Size;
            var along = rot.Rotated(RotationDirection.Clockwise).FacingCell;
            var across = rot.FacingCell;
            int step = Reach(span, along) + run.Gap;
            int rank = Reach(span, across) + run.Gap;

            // Centred, or a seven-by-seven patch would sit off the dart by half itself and
            // the lean toward the core would read as a lean away from it.
            var head = at - along * ((many - 1) * step / 2)
                          - across * ((run.Lines - 1) * rank / 2);

            _mine.Clear();
            Frame first = null;

            for (int line = 0; line < run.Lines; line++)
            {
                var start = head + across * (line * rank);
                for (int i = 0; i < many; i++)
                {
                    var c = start + along * (i * step);
                    if (!Fits(errand.What, c, rot, pawn)) continue;

                    var frame = Pitch(errand.What, c, rot);
                    if (frame == null) continue;
                    _mine.Add(frame);
                    if (first == null) first = frame;
                }
            }

            return first;
        }

        // How far a footprint stretches along one of the four directions. The rect is
        // already rotated, so the direction only says which of its two sides to read.
        static int Reach(IntVec2 span, IntVec3 dir) => dir.x != 0 ? span.x : span.z;

        bool Fits(BuildableDef what, IntVec3 at, Rot4 rot, Pawn pawn)
        {
            if (!at.InBounds(map) || at.Fogged(map)) return false;

            // Near the middle, not *in* the plague: what the agents build is what kills the
            // ground, so a site allowed only on ash could never lay its first plate.
            if (!Near(at)) return false;

            // A pad around anything with a shape, so a site never closes off a path or
            // grows into one lump. Floors want none - paving up to a monument is the point.
            int pad = what is TerrainDef ? 0 : 1;
            bool floor = what is TerrainDef;
            var footprint = GenAdj.OccupiedRect(at, rot, what.Size);

            // What vanilla wants cleared off the ground first, which here is work no agent
            // is allowed and a hauler that does not exist - so such a cell is not a site.
            //
            // Read off the *blueprint*, not off what is being built: for a floor the two
            // disagree and only the blueprint's is the answer the game will give.
            // NewBlueprintDef_Terrain sets both flags false, so a plate goes over grass and
            // slag and only a plant worth harvesting blocks one. The TerrainDef has
            // clearBuildingArea true, as every BuildableDef does by default, and reading it
            // there refused every cell with a blade of grass in it - which on the rim and
            // anywhere a detonation has been is most of them, so the darts found nowhere to
            // pave and BlockedFor sat the site down on that.
            var print = what.blueprintDef;
            bool clear = print != null ? print.clearBuildingArea : what.clearBuildingArea;
            bool tidy = clear || (print != null
                ? print.forceMoveItemsBeforeConstruction
                : what.forceMoveItemsBeforeConstruction);

            foreach (var c in footprint.ExpandedBy(pad))
            {
                if (!c.InBounds(map)) return false;
                // The hillside is scenery, not a building site.
                if (map.roofGrid.Roofed(c)) return false;

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];

                    // The rest of this sequence, already pitched. The pad is there to keep
                    // the site from growing into one lump, and a run is a shape somebody
                    // asked for - read as a stranger, a row of graves refuses its own
                    // second grave and every row on the map is one grave long.
                    if (_mine.Contains(thing)) continue;

                    if (thing is Building || thing is Blueprint) return false;
                    if (!footprint.Contains(c)) continue;

                    if (clear && thing.def.category == ThingCategory.Plant) return false;
                    if (tidy && thing.def.category == ThingCategory.Item) return false;

                    // The one thing a floor does have to have off the ground first.
                    if (floor && Rooted(thing)) return false;

                    // A pawn standing there blocks the build: it is told to move, we tell it
                    // to build, and neither wins. Nothing moves out of the way of a floor.
                    if (!floor && thing is Pawn) return false;
                }
            }

            // Paving a paved cell builds nothing and the sweep returns to it forever.
            if (what is TerrainDef terrain && map.terrainGrid.TerrainAt(at) == terrain) return false;

            if (!GenConstruct.CanPlaceBlueprintAt(what, at, rot, map, false, null, null, StuffFor(what))
                    .Accepted) return false;

            return pawn.CanReach(at, PathEndMode.Touch, Danger.Deadly);
        }

        // Where GenConstruct.BlocksConstruction draws the line for a terrain frame: a
        // dandelion's harvest work. Grass is paved over; a tree is not.
        static bool Rooted(Thing thing)
        {
            var plant = thing.def.category == ThingCategory.Plant ? thing.def.plant : null;
            return plant != null &&
                   plant.harvestWork > ThingDefOf.Plant_Dandelion.plant.harvestWork;
        }

        // Straight to the frame: a blueprint is a request for a hauler, and the only thing
        // here that could answer it is the agent that would rather be building.
        Frame Pitch(BuildableDef what, IntVec3 at, Rot4 rot)
        {
            if (what.frameDef == null) return null;

            var frame = ThingMaker.MakeThing(what.frameDef, StuffFor(what)) as Frame;
            if (frame == null) return null;

            frame.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(frame, at, map, rot);
            Fill(frame);
            return frame;
        }

        // A new map should carry nothing of this one, but the site is the only thing here
        // leaving permanent marks on the board, so it is stated rather than assumed.
        public static void Wipe(Map map)
        {
            if (map == null) return;

            var doomed = new List<Thing>();
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing == null || thing.Destroyed) continue;
                if (thing is Blueprint || thing is Frame ||
                    (thing is Building && WorkFor(thing.def) > 0f)) doomed.Add(thing);
            }

            for (int i = 0; i < doomed.Count; i++)
                if (!doomed[i].Destroyed) doomed[i].Destroy(DestroyMode.Vanish);
        }

        static void Fill(Frame frame)
        {
            if (frame == null || !frame.Spawned || frame.resourceContainer == null) return;

            var cost = frame.TotalMaterialCost();
            for (int i = 0; i < cost.Count; i++)
            {
                var need = cost[i];
                int missing = need.count - frame.resourceContainer.TotalStackCountOfDef(need.thingDef);

                while (missing > 0)
                {
                    int take = Mathf.Min(missing, need.thingDef.stackLimit);
                    var stack = ThingMaker.MakeThing(need.thingDef);
                    stack.stackCount = take;

                    // Never twice: a container that will not take the stone would otherwise
                    // be conjured into forever.
                    if (!frame.resourceContainer.TryAdd(stack, false)) return;
                    missing -= take;
                }
            }
        }

        ThingDef StuffFor(BuildableDef what)
        {
            var td = what as ThingDef;
            if (td == null || !td.MadeFromStuff) return null;

            var blocks = Blocks();
            if (blocks != null && GenStuff.AllowedStuffsFor(td).Contains(blocks)) return blocks;
            return GenStuff.DefaultStuffFor(td);
        }

        // One rock for the colony, the tile's own - stable across a reload without being
        // written down.
        ThingDef Blocks()
        {
            if (_quarried) return _blocks;
            _quarried = true;

            var world = Find.World;
            if (world != null)
                foreach (var def in world.NaturalRockTypesIn(map.Tile)) { _rock = def; break; }

            _blocks = _rock != null ? Named("Blocks" + _rock.defName) : null;
            return _blocks;
        }

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

        // Vanilla's figures are an economy's; the errand table states seconds of an agent's
        // working time and this is where that lands. Anything off the table keeps vanilla's
        // number, WorkFor answering zero for it.
        [HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
        public static class Patch_ErrandWork
        {
            static void Postfix(Frame __instance, ref float __result)
            {
                float work = WorkFor(__instance?.def?.entityDefToBuild);
                if (work > 0f) __result = work;
            }
        }

        // Where a finished thing becomes plague. A prefix, because after CompleteConstruction
        // the frame is despawned and has neither a map nor the footprint - and the footprint
        // is the point, a five-by-three machine being a source that wide rather than a point.
        [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
        public static class Patch_ErrandDone
        {
            static void Prefix(Frame __instance)
            {
                if (__instance == null || !__instance.Spawned) return;

                var what = __instance.def?.entityDefToBuild;
                if (WorkFor(what) <= 0f) return;

                var map = __instance.Map;
                var rect = __instance.OccupiedRect();
                map?.GetComponent<Worksite>()?.Count(rect.Area);

                float bloom = BloomFor(what);
                if (bloom <= 0f) return;

                var plague = map?.GetComponent<Plague>();
                if (plague == null) return;
                foreach (var c in rect) plague.Bloom(c, bloom);
            }
        }

        // A terrain frame draws four white corner brackets, and paving is queued a square at
        // a time, so ahead of the agents that is a grid over most of the board saying
        // nothing anybody can act on. Anything with a shape keeps its frame.
        // By name: the override is not public, so nameof would not compile.
        [HarmonyPatch(typeof(Frame), "DrawAt", new[] { typeof(Vector3), typeof(bool) })]
        public static class Patch_HideFloorFrames
        {
            static bool Prefix(Frame __instance)
            {
                var what = __instance?.def?.entityDefToBuild;
                return !(what is TerrainDef) || WorkFor(what) <= 0f;
            }
        }
    }
}
