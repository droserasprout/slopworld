using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Assign construction errands while agents work. Completed errands expand the plague through Plague.Bloom.
    // Roam uses plague growth to expand the construction area.
    // The base game job driver performs construction. Fill supplies frame materials without hauling.
    public partial class Worksite : MapComponent
    {
        // Run every 15 game ticks. Offset the schedule from AgentColony reconciliation.
        // Frequent assignment reduces delays between paving jobs.
        const int Interval = 15;
        const int Phase = 7;

        // Try random positions within the construction area.
        const int Tries = 30;

        // Game ticks to wait after all floor placement attempts fail.
        const int BlockedFor = 300;

        // Limit unfinished frames. A complete paving square needs 49 frames.
        const int MaxOpen = 96;

        // Estimate construction work per tick to convert configured seconds into work requirements.
        // The pawn construction speed determines actual progress.
        const float WorkPerTick = 1.4f;

        // Target durations in real seconds of agent work.
        // Paving requires little construction time compared with movement between cells.
        const float PavingSeconds = 0.1f;
        const float SmallSeconds = 4f;
        const float MediumSeconds = 8f;
        const float LargeSeconds = 15f;
        const float MonumentSeconds = 30f;

        // Place floor frames in squares to keep paving jobs close together.
        const int PavingSide = 7;

        // Place graves in rows with a shared orientation and one cell between graves.
        // Other errands can use the same Run structure.
        const int GraveRowLeast = 5;
        const int GraveRowMost = 10;
        const int GraveAisle = 1;

        // Select sites within this distance range from the pawn.
        // The minimum distance helps prevent the pawn from blocking a new frame. Fits also checks clearance.
        const float SiteNear = 4f;
        const float SiteRadius = 12f;

        // Bias site directions toward the core while permitting sites farther away.
        // Remove this bias within one cell of the center.
        const float SiteLean = 0.8f;

        // Extend the construction radius beyond the plague area.
        // Plague.Girth measures the affected area, so completed construction permits sites farther from the center.
        const float RoamMargin = 16f;

        // Set a minimum construction radius so agents have space before the plague expands.
        const float MinCircle = 18f;

        // Use relative selection weights for errands that the current pawn can complete.
        // The weights control selection frequency, independent of work duration.
        const float PavingOdds = 10f;

        const float ColumnOdds = 2f;
        const float GraveOdds = 5f;
        const float SarcophagusOdds = 1f;
        const float SteleLargeOdds = 1f;
        const float SteleGrandOdds = 1f;

        // Select lamps more often than lampposts.
        const float LampOdds = 5f;
        const float LamppostOdds = 1f;
        const float RackOdds = 2f;
        const float ScreensOdds = 2f;
        const float LockersOdds = 2f;
        const float GeneratorOdds = 3f;
        const float MachineOdds = 1f;

        // Plague growth radius in cells for each completed item.
        // Tune growth with work duration to limit differences in plague expansion between errand types.
        const float PavingBloom = 3f;
        const float SmallBloom = 4f;
        const float MediumBloom = 6f;
        const float LargeBloom = 8f;
        const float MonumentBloom = 11f;

        readonly WorksiteRuntime _runtime = new WorksiteRuntime();
        int _laid;

        // Access temporary state through the runtime cache.
        ThingDef _blocks { get => _runtime.Blocks; set => _runtime.Blocks = value; }
        ThingDef _rock { get => _runtime.Rock; set => _runtime.Rock = value; }
        bool _quarried { get => _runtime.Quarried; set => _runtime.Quarried = value; }
        Plague _plague { get => _runtime.Plague; set => _runtime.Plague = value; }
        int _blocked { get => _runtime.Blocked; set => _runtime.Blocked = value; }
        int _swept { get => _runtime.Swept; set => _runtime.Swept = value; }
        HashSet<Thing> _mine => _runtime.Mine;
        List<Frame> _frames => _runtime.Frames;
        List<Thing> _sweepDoomed => _runtime.SweepDoomed;

        // Worksite passes between frame sweeps.
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

            EnsureFrameIndex();
            if (++_swept >= SweepEvery)
            {
                _swept = 0;
                if (!_runtime.SweepPending) BeginSweep();
            }
            if (_runtime.SweepPending) SweepSlice();

            var colony = AgentColony.Current;
            if (colony == null) return;

            var hub = SessionHub.Instance;

            foreach (var kv in colony.All)
            {
                var pawn = kv.Value;
                if (pawn == null || !pawn.Spawned || pawn.Map != map) continue;
                if (pawn.Dead || pawn.Downed) continue;

                var state = hub.Get(kv.Key)?.State ?? AgentState.Down;
                if (state != AgentState.Working) { Stop(pawn); continue; }

                // Replace missing materials during construction.
                // A failed construction attempt can consume materials, and this map has no haulers to replace them.
                if (pawn.CurJobDef == JobDefOf.FinishFrame)
                {
                    Fill(pawn.CurJob?.targetA.Thing as Frame);
                    continue;
                }

                Send(pawn);
            }
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

            // Delay placement after all floor placement attempts fail.
            // Failed building runs do not delay other attempts.
            if (errand.What is TerrainDef) _blocked = now + BlockedFor;
            return null;
        }

        int Standing()
        {
            EnsureFrameIndex();
            int count = 0;
            foreach (var frame in _frames)
                if (frame != null && frame.Spawned) count++;
            return count;
        }

        void EnsureFrameIndex()
        {
            var things = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            if ((!_runtime.FrameIndexDirty && things.Count == _runtime.FrameSourceCount) ||
                _runtime.SweepPending) return;

            _frames.Clear();
            _runtime.FrameSourceCount = things.Count;
            foreach (var thing in things)
            {
                var frame = thing as Frame;
                if (frame != null && frame.Spawned &&
                    WorkFor(frame.def?.entityDefToBuild) > 0f)
                    _frames.Add(frame);
            }
            _runtime.FrameIndexDirty = false;
        }

        internal void MarkFramesDirty() => _runtime.FrameIndexDirty = true;

        void RegisterFrame(Frame frame)
        {
            if (frame == null) return;
            if (_runtime.FrameIndexDirty) return;
            if (!_frames.Contains(frame))
            {
                _frames.Add(frame);
                _runtime.FrameSourceCount++;
            }
        }

        public static int LaidOn(Map map) => map?.GetComponent<Worksite>()?._laid ?? 0;

        // Count completed footprint cells, independent of construction time.
        void Count(int cells) => _laid += cells;

        // Choose a site near the pawn if it is within the permitted area.
        // Otherwise, choose a site near the plague center.
        // Round coordinates to avoid shifting sites by half a cell.
        IntVec3 Site(Pawn pawn)
        {
            bool home = Near(pawn.Position);
            var from = home ? pawn.Position : Heart();

            // Choose sites in a ring around the pawn to avoid its occupied cell.
            var dir = Rand.InsideUnitCircleVec3.normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            dir = (dir + Pull(pawn.Position)).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            var v = home
                ? dir * Rand.Range(SiteNear, SiteRadius)
                : Rand.InsideUnitCircleVec3 * Roam();

            return from + new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));
        }

        // Bias the direction toward the center with a constant magnitude.
        // Remove the bias within one cell of the center.
        Vector3 Pull(IntVec3 from)
        {
            var to = (Heart() - from).ToVector3();
            return to.magnitude < 1f ? Vector3.zero : to.normalized * SiteLean;
        }

        // Approximate the plague area as a circle with an outer margin.
        // A radius check avoids repeated lookups across the irregular plague boundary.
        float Roam() => _plague == null ? 0f : Mathf.Max(_plague.Girth + RoamMargin, MinCircle);

        IntVec3 Heart() => _plague == null ? map.Center : _plague.Heart;

        bool Near(IntVec3 cell) =>
            _plague != null && _plague.Active && cell.DistanceTo(_plague.Heart) <= Roam();

        // Place one rotated run. Skip items that do not fit without stopping the run.
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

            // Center the run on the selected site to preserve the bias toward the core.
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

        // Return the footprint size along the specified direction.
        // The footprint already includes rotation.
        static int Reach(IntVec2 span, IntVec3 dir) => dir.x != 0 ? span.x : span.z;

        bool Fits(BuildableDef what, IntVec3 at, Rot4 rot, Pawn pawn)
        {
            if (!at.InBounds(map) || at.Fogged(map)) return false;

            // Permit sites outside the plague boundary but within the construction radius.
            // Construction must start before it can expand the plague.
            if (!Near(at)) return false;

            // Reserve space around buildings to keep paths open.
            // Floors need no margin and can reach a building edge.
            int pad = what is TerrainDef ? 0 : 1;
            bool floor = what is TerrainDef;
            var footprint = GenAdj.OccupiedRect(at, rot, what.Size);

            // Read clearing flags from the blueprint when available.
            // Terrain blueprints permit paving over grass and slag. Rooted checks larger plants separately.
            var print = what.blueprintDef;
            bool clear = print != null ? print.clearBuildingArea : what.clearBuildingArea;
            bool tidy = clear || (print != null
                ? print.forceMoveItemsBeforeConstruction
                : what.forceMoveItemsBeforeConstruction);

            foreach (var c in footprint.ExpandedBy(pad))
            {
                if (!c.InBounds(map)) return false;
                // Preserve roofed areas as scenery.
                if (map.roofGrid.Roofed(c)) return false;

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];

                    // Ignore frames from this run when checking margins.
                    // Otherwise, a placed frame can prevent placement of the next item in the run.
                    if (_mine.Contains(thing)) continue;

                    if (thing is Building || thing is Blueprint) return false;
                    if (!footprint.Contains(c)) continue;

                    if (clear && thing.def.category == ThingCategory.Plant) return false;
                    if (tidy && thing.def.category == ThingCategory.Item) return false;

                    // Reject plants that require removal before paving.
                    if (floor && Rooted(thing)) return false;

                    // Reject occupied building sites to avoid repeated movement and construction assignments.
                    // Pawns do not block floor placement.
                    if (!floor && thing is Pawn) return false;
                }
            }

            // Reject cells that already have the requested terrain.
            if (what is TerrainDef terrain && map.terrainGrid.TerrainAt(at) == terrain) return false;

            if (!GenConstruct.CanPlaceBlueprintAt(what, at, rot, map, false, null, null, StuffFor(what))
                    .Accepted) return false;

            return pawn.CanReach(at, PathEndMode.Touch, Danger.Deadly);
        }

        // Use the dandelion harvest work value as the plant clearance threshold.
        // This matches the terrain frame check in GenConstruct.BlocksConstruction.
        static bool Rooted(Thing thing)
        {
            var plant = thing.def.category == ThingCategory.Plant ? thing.def.plant : null;
            return plant != null &&
                   plant.harvestWork > ThingDefOf.Plant_Dandelion.plant.harvestWork;
        }

        // Create and fill a frame directly so construction does not require hauling.
        Frame Pitch(BuildableDef what, IntVec3 at, Rot4 rot)
        {
            if (what.frameDef == null) return null;

            var frame = ThingMaker.MakeThing(what.frameDef, StuffFor(what)) as Frame;
            if (frame == null) return null;

            frame.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(frame, at, map, rot);
            Fill(frame);
            RegisterFrame(frame);
            return frame;
        }

        // Remove blueprints, frames, and completed errand buildings before discarding the map.
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

                    // Stop if the container rejects a stack to prevent an infinite loop.
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

        // Use the first natural rock type for the map tile.
        // Derive the choice again after loading instead of saving it.
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
