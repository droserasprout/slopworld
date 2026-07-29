using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // What a clanker does with its hands while its process is burning tokens.
    //
    // A working agent walks to the nearest frame nobody has claimed and hammers at it;
    // if there is no such frame it opens one, in the half of the plague circle nearest
    // the core - ground the core has already taken, so the site is built on ash rather
    // than on anything living. Leaving Working ends the job on the spot. Frame.workDone
    // is on the frame rather than the pawn, so a thing standing here is the sum of every
    // burst the agent had while it was going up.
    //
    // Fast, all of it: three seconds a paving stone and three minutes for the largest
    // thing on the table. These go up at the speed the things they stand for are
    // written, and an agent that worked for a minute has to be able to point at
    // something for it.
    //
    // This is the one system here that does *not* apply its effects by hand. The job
    // driver is worth having whole: it walks the pawn, faces it at the work, throws the
    // construction effecter, draws the progress bar and rolls quality off the builder.
    // Its one roll we do take off it is the failure (SteadyHands).
    //
    // What it needs and the map does not have is materials. There are no stockpiles, no
    // haulers and no economy, so a frame arrives with its stone already inside it
    // (Fill).
    public class Worksite : MapComponent
    {
        // Off AgentColony's own second by half, so the reconcile and the errands are not
        // paid for on the same tick.
        const int Interval = 60;
        const int Phase = 30;

        // Sites are picked by throwing darts at the circle rather than by searching it:
        // most of the ground is fine, and a search would want the whole map when it is
        // not.
        const int Tries = 30;

        // Frames outlive the burst that opened them, so without a ceiling a colony of
        // busy agents leaves the map a field of half-built graves. Generous, because a
        // square of paving is two dozen frames on its own.
        const int MaxOpen = 48;

        // What one tick of construction is worth to a middling clanker: the driver pays
        // ConstructionSpeed * 1.7 a tick and stone knocks that back a little. It is only
        // here to turn the seconds below into a work figure - the pawn's own stat is
        // what actually spends it, so a good builder beats the estimate and a bad one
        // falls behind.
        const float WorkPerTick = 1.4f;

        // Real seconds of an agent's *working* time, not of the colony's day, and few of
        // them: these things go up at the speed the things they stand for are written.
        // A burst that lasted a minute has a column in it and half a plaza; nothing here
        // is a project, because nothing the agents do is either.
        const float PavingSeconds = 3f;
        const float SmallSeconds = 20f;
        const float MediumSeconds = 45f;
        const float LargeSeconds = 90f;
        const float MonumentSeconds = 180f;

        // Floors are laid a square at a time, or every errand would be one cell and the
        // agent would spend the burst walking between them. Wide, because at three
        // seconds a cell a small square is over before the walk out there was worth it.
        const int PavingSide = 5;

        // Where paving goes when there is already something to pave around.
        const float PavingBesideChance = 0.6f;
        const int PavingBesideSpread = 3;

        struct Errand
        {
            public BuildableDef What;
            public float Seconds;
            public float Weight;
            public int Patch; // side of the square laid at once; 1 for anything with a shape
        }

        // Keyed on what is being built rather than on the frame, because the frame is
        // gone by the time anything asks a second time. Static: the table is the same
        // table on every map, and Patch_ErrandWork has no map to ask.
        static readonly Dictionary<BuildableDef, float> Work = new Dictionary<BuildableDef, float>();

        public static float WorkFor(BuildableDef def) =>
            def != null && Work.TryGetValue(def, out float w) ? w : 0f;

        // Not saved: both are questions about the tile and the def database, and a load
        // can ask them again.
        List<Errand> _errands;
        ThingDef _blocks;
        ThingDef _rock;
        bool _quarried;

        public Worksite(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + Phase) % Interval != 0) return;

            // Neither scene wants a clanker wandering off to lay a floor in the middle of
            // it.
            if (Cutscene.AgentsHeld) return;

            var plague = map.GetComponent<Plague>();
            if (plague == null || !plague.Active) return;

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

                // Already at it. The top-up is not idle work: a failed construction empties
                // the frame, and vanilla's own answer to that is a hauler this map has not
                // got.
                if (pawn.CurJobDef == JobDefOf.FinishFrame)
                {
                    Fill(pawn.CurJob?.targetA.Thing as Frame);
                    continue;
                }

                Send(pawn, plague);
            }
        }

        // The daemon's word is the whole of it: an agent that has gone quiet puts the
        // hammer down where it stands, and the frame keeps what it has been given.
        //
        // Taking the work type back off it is the other half, and not optional. The
        // errand is ours to hand out, but Construction being *on* is an open invitation
        // to vanilla's own work giver, which hands any free colonist the nearest frame -
        // so an idle agent would walk over and build, and the one thing the site is
        // supposed to say is which processes are busy.
        static void Stop(Pawn pawn)
        {
            if (pawn.jobs != null && pawn.CurJobDef == JobDefOf.FinishFrame)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);

            var work = pawn.workSettings;
            if (work == null || !work.Initialized) return;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return;
            if (work.GetPriority(WorkTypeDefOf.Construction) != 0)
                work.SetPriority(WorkTypeDefOf.Construction, 0);
        }

        void Send(Pawn pawn, Plague plague)
        {
            if (!Ready(pawn)) return;

            var frame = Free(pawn) ?? Open(pawn, plague);
            if (frame == null) return;

            Fill(frame);
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.FinishFrame, frame), JobTag.Misc);
        }

        // Construction has to be *on* for as long as the hammer is swinging:
        // GenConstruct.CanConstruct reads the work settings and the job driver fails on
        // that every tick it runs. Patch_AgentsCanBuild is what makes sure the answer may
        // be yes whatever backstory the pawn was handed; this is what says it, and Stop
        // is what takes it back the moment the process goes quiet.
        static bool Ready(Pawn pawn)
        {
            if (pawn.jobs == null) return false;

            var work = pawn.workSettings;
            if (work == null) return false;
            work.EnableAndInitializeIfNotAlreadyInitialized();

            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return false;
            if (work.GetPriority(WorkTypeDefOf.Construction) == 0)
                work.SetPriority(WorkTypeDefOf.Construction, 3);

            return true;
        }

        // Free means nobody has reserved it - vanilla's own reservation is what the job
        // driver takes on the way in, so asking it here is asking the same question the
        // job will. Nearest first, so two agents on the same site do not cross the map
        // past each other.
        Frame Free(Pawn pawn)
        {
            Frame best = null;
            float nearest = float.MaxValue;

            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                var frame = thing as Frame;
                if (frame == null || !frame.Spawned) continue;
                if (WorkFor(frame.def?.entityDefToBuild) <= 0f) continue;
                if (!pawn.CanReserve(frame)) continue;
                if (!pawn.CanReach(frame, PathEndMode.Touch, Danger.Deadly)) continue;

                float d = frame.Position.DistanceToSquared(pawn.Position);
                if (d >= nearest) continue;
                nearest = d;
                best = frame;
            }

            return best;
        }

        Frame Open(Pawn pawn, Plague plague)
        {
            if (Standing() >= MaxOpen) return null;

            var pick = Pick();
            if (pick == null) return null;
            var errand = pick.Value;

            // Half the circle, and never so small that the first minute of the plague has
            // the agents building inside the core itself.
            float reach = Mathf.Max(plague.Reach * 0.5f, 6f);

            for (int i = 0; i < Tries; i++)
            {
                var frame = errand.Patch > 1
                    ? Pave(errand, pawn, plague, reach)
                    : Raise(errand, Site(plague, reach), pawn);
                if (frame != null) return frame;
            }

            return null;
        }

        int Standing()
        {
            int n = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                if (WorkFor((thing as Frame)?.def?.entityDefToBuild) > 0f) n++;
            return n;
        }

        Errand? Pick()
        {
            var list = Errands;
            if (list.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < list.Count; i++) total += list[i].Weight;

            float roll = Rand.Range(0f, total);
            for (int i = 0; i < list.Count; i++)
            {
                roll -= list[i].Weight;
                if (roll <= 0f) return list[i];
            }
            return list[list.Count - 1];
        }

        // A dart at the circle. Round rather than truncate, or the sites lean towards the
        // core by half a cell in both axes.
        IntVec3 Site(Plague plague, float reach)
        {
            var v = Rand.InsideUnitCircleVec3 * reach;
            return plague.Heart + new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));
        }

        Frame Raise(Errand errand, IntVec3 at, Pawn pawn)
        {
            var td = errand.What as ThingDef;
            var rot = td != null && td.rotatable ? Rot4.Random : Rot4.North;
            if (!Fits(errand.What, at, rot, pawn)) return null;
            return Pitch(errand.What, at, rot);
        }

        // Paving prefers ground beside something already standing: a floor spreading out
        // from under a stele reads as a plaza being kept, where squares dropped at random
        // read as a bug.
        Frame Pave(Errand errand, Pawn pawn, Plague plague, float reach)
        {
            IntVec3 centre = IntVec3.Invalid;
            if (Rand.Chance(PavingBesideChance)) centre = Beside();
            if (!centre.IsValid) centre = Site(plague, reach);

            Frame first = null;
            foreach (var c in CellRect.CenteredOn(centre, errand.Patch / 2))
            {
                if (!Fits(errand.What, c, Rot4.North, pawn)) continue;
                var frame = Pitch(errand.What, c, Rot4.North);
                if (frame != null && first == null) first = frame;
            }
            return first;
        }

        // Something this lot built, finished or not.
        IntVec3 Beside()
        {
            var built = map.listerBuildings.allBuildingsColonist
                .Where(b => WorkFor(b.def) > 0f || WorkFor(b.def?.entityDefToBuild) > 0f)
                .RandomElementWithFallback();
            if (built == null) return IntVec3.Invalid;

            return built.Position + new IntVec3(
                Rand.RangeInclusive(-PavingBesideSpread, PavingBesideSpread), 0,
                Rand.RangeInclusive(-PavingBesideSpread, PavingBesideSpread));
        }

        bool Fits(BuildableDef what, IntVec3 at, Rot4 rot, Pawn pawn)
        {
            if (!at.InBounds(map) || at.Fogged(map)) return false;

            // A pad around anything with a shape, so a site never closes a path off or
            // grows into one lump. Floors want none of it: paving right up to a monument is
            // the point of them.
            int pad = what is TerrainDef ? 0 : 1;

            foreach (var c in GenAdj.OccupiedRect(at, rot, what.Size).ExpandedBy(pad))
            {
                if (!c.InBounds(map)) return false;
                // The hillside is scenery, not a building site.
                if (map.roofGrid.Roofed(c)) return false;

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Building || things[i] is Blueprint) return false;
            }

            // Paving a cell that is already paved builds nothing and the sweep would come
            // back to it forever.
            if (what is TerrainDef terrain && map.terrainGrid.TerrainAt(at) == terrain) return false;

            if (!GenConstruct.CanPlaceBlueprintAt(what, at, rot, map, false, null, null, StuffFor(what))
                    .Accepted) return false;

            return pawn.CanReach(at, PathEndMode.Touch, Danger.Deadly);
        }

        // Straight to the frame, no blueprint: a blueprint is a request for a hauler, and
        // the only thing on this map that could answer it is the agent that would rather
        // be building.
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

                    // Never twice: a container that will not take the stone is one this would
                    // otherwise conjure into forever.
                    if (!frame.resourceContainer.TryAdd(stack, false)) return;
                    missing -= take;
                }
            }
        }

        // Stone, and the map's own: blocks nobody quarried, of the rock the colony is
        // standing on.
        ThingDef StuffFor(BuildableDef what)
        {
            var td = what as ThingDef;
            if (td == null || !td.MadeFromStuff) return null;

            var blocks = Blocks();
            if (blocks != null && GenStuff.AllowedStuffsFor(td).Contains(blocks)) return blocks;
            return GenStuff.DefaultStuffFor(td);
        }

        // One rock for the colony rather than a sampler, and the tile's own, which is
        // stable across a reload without being written down.
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

        List<Errand> Errands
        {
            get
            {
                if (_errands != null) return _errands;
                _errands = new List<Errand>();

                Blocks(); // which is what settles the rock the rest of this is quarried from
                var flagstone = _rock != null
                    ? DefDatabase<TerrainDef>.GetNamedSilentFail("Flagstone" + _rock.defName)
                    : null;

                // Most of what happens here, and deliberately: paving is the errand that
                // finishes. It is what a short burst has to show for itself, and a monument
                // that arrives now and then reads as an event where one a day would read as
                // the only thing the site does.
                Add(flagstone, PavingSeconds, 70f, PavingSide);

                // Monuments and graves. What a machine builds when it is told nothing about
                // what for: a marker, a place to put somebody, and a slab with writing on it
                // that nobody will read.
                Add(Named("Column"), SmallSeconds, 10f);
                Add(Named("Grave"), SmallSeconds, 7f);
                Add(Named("Sarcophagus"), MediumSeconds, 6f);
                Add(Named("SteleLarge"), LargeSeconds, 4f);
                Add(Named("SteleGrand"), MonumentSeconds, 3f);

                if (_errands.Count == 0)
                    Log.Warning("[SlopWorld] no errands this build knows how to build; agents will stand about");

                return _errands;
            }
        }

        void Add(BuildableDef what, float seconds, float weight, int patch = 1)
        {
            if (what == null || what.frameDef == null) return;

            _errands.Add(new Errand { What = what, Seconds = seconds, Weight = weight, Patch = patch });
            Work[what] = seconds * RealClock.TicksPerRealSecond * WorkPerTick;
        }

        static ThingDef Named(string name) => DefDatabase<ThingDef>.GetNamedSilentFail(name);

        // Vanilla's own figures are an economy's: a grand stele is two real minutes of
        // work because a colony has thirty other things to be doing. Here the figure is
        // the point - it is how long an agent has to stay busy for the thing to stand up
        // - so the errand table states it in minutes and this is where that lands. Every
        // frame on this map is one of ours, but the lookup answers zero for anything else
        // regardless, so a frame from a def we never listed keeps vanilla's number.
        [HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
        public static class Patch_ErrandWork
        {
            static void Postfix(Frame __instance, ref float __result)
            {
                float work = WorkFor(__instance?.def?.entityDefToBuild);
                if (work > 0f) __result = work;
            }
        }
    }
}
