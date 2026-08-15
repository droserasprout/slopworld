using System.Collections.Generic;
using System.Linq;
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
    public partial class Worksite : MapComponent
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

        // Relative task weights; Pick normalizes them over work the current pawn can finish.
        // Weight by item count, not work time, so large builds do not dominate every burst.
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

        struct MaterialState
        {
            public ThingDef Blocks;
            public ThingDef Rock;
            public bool Quarried;
        }

        struct PlacementState
        {
            public Plague Plague;
            public int Blocked;
            public int Swept;
            public int Laid;
            public HashSet<Thing> Mine;
        }

        struct AssignmentState
        {
            public Dictionary<Pawn, Frame> Sent;
            public HashSet<Frame> Shunned;
            public List<Pawn> Hands;
            public List<Pawn> Stale;
        }

        MaterialState _materials;
        PlacementState _placement = new PlacementState
        {
            Mine = new HashSet<Thing>(),
        };
        AssignmentState _assignments = new AssignmentState
        {
            Sent = new Dictionary<Pawn, Frame>(),
            Shunned = new HashSet<Frame>(),
            Hands = new List<Pawn>(),
            Stale = new List<Pawn>(),
        };

        // These aliases keep the domain logic readable while the state ownership stays in
        // value structs: placement, assignment, and material selection do not share a bag of
        // unrelated fields anymore.
        ThingDef _blocks { get => _materials.Blocks; set => _materials.Blocks = value; }
        ThingDef _rock { get => _materials.Rock; set => _materials.Rock = value; }
        bool _quarried { get => _materials.Quarried; set => _materials.Quarried = value; }
        Plague _plague { get => _placement.Plague; set => _placement.Plague = value; }
        int _blocked { get => _placement.Blocked; set => _placement.Blocked = value; }
        int _swept { get => _placement.Swept; set => _placement.Swept = value; }
        int _laid { get => _placement.Laid; set => _placement.Laid = value; }
        Dictionary<Pawn, Frame> _sent => _assignments.Sent;
        HashSet<Frame> _shunned => _assignments.Shunned;
        List<Pawn> _hands => _assignments.Hands;
        List<Pawn> _stale => _assignments.Stale;
        HashSet<Thing> _mine => _placement.Mine;

        // Passes between sweeps - once a second.
        const int SweepEvery = 4;

        public Worksite(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            int laid = _placement.Laid;
            Scribe_Values.Look(ref laid, "laid", 0);
            _placement.Laid = laid;
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

            // Only floor-placement failure blocks the site: a one-cell plate means no valid
            // position exists nearby. Shaped runs can fail locally without stopping the site.
            if (errand.What is TerrainDef) _blocked = now + BlockedFor;
            return null;
        }

        // Exclude frames blocked by vanilla's first blocking thing: they would consume MaxOpen
        // forever, and no agent or hauler can remove the blocker.
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

        // Place one oriented run; skip members that do not fit so one blocked cell does not
        // abort the rest of the run.
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

            // Read clearing flags from the blueprint: terrain blueprints disable both flags,
            // allowing paving over grass and slag while harvestable plants still block it.
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
    }
}
