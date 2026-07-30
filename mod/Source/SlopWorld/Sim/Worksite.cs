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
    // if there is no such frame it opens one where it is standing. Where it is standing
    // is where an agent with nothing to do had wandered to, which is as fair a spread as
    // this needs and costs nothing to work out. Leaving Working ends the job on the
    // spot. Frame.workDone
    // is on the frame rather than the pawn, so a thing standing here is the sum of every
    // burst the agent had while it was going up.
    //
    // What everything it finishes does is emit plague (Plague.Bloom). That is the whole
    // causal line this map is about and it runs one way: the agents build, the ground they
    // built on dies, and the dead ground is the visible sum of what the sessions have been
    // busy doing. Nothing is *earned* any more and nothing is spent - a monument is not a
    // payment for a ring of circle, it is a thing that kills the ground around itself.
    //
    // So the site is no longer held inside the dead ground; it could not be, since holding
    // it there would mean a site that can only be opened on ash that only the site makes.
    // It is held *near the middle* instead, which is Roam, and the ring of living ground it
    // works in is opened by its own work.
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
        // Four times a second, and off AgentColony's own second, so the reconcile and the
        // errands are never paid for on the same tick. Not once a second, because a
        // paving stone takes less than a tick to lay and the errand a pawn is handed here
        // is the whole of what it does next: at a second a look the site would be one
        // clanker standing over a finished floor, waiting to be told about the next cell.
        const int Interval = 15;
        const int Phase = 7;

        // Sites are picked by throwing darts at the circle rather than by searching it:
        // most of the ground is fine, and a search would want the whole map when it is
        // not.
        const int Tries = 30;

        // How long the whole site sits down for after a round of darts found nowhere to
        // build. Five seconds, which nobody can see and a full map does not spend.
        const int BlockedFor = 300;

        // Frames outlive the burst that opened them, so without a ceiling a colony of
        // busy agents leaves the map a field of half-built graves. Generous, because a
        // square of paving is fifty frames on its own.
        const int MaxOpen = 96;

        // What one tick of construction is worth to a middling clanker: the driver pays
        // ConstructionSpeed * 1.7 a tick and stone knocks that back a little. It is only
        // here to turn the seconds below into a work figure - the pawn's own stat is
        // what actually spends it, so a good builder beats the estimate and a bad one
        // falls behind.
        const float WorkPerTick = 1.4f;

        // Real seconds of an agent's *working* time, not of the colony's day, and barely
        // any of them. The target is the hour: a session's worth of a couple of agents
        // being busy has to leave the circle with nowhere left to put anything, because
        // running out of ground is the only thing on this map that ever asks the player
        // for a decision.
        //
        // Paving is instant in the hand - the walk to the next cell is the whole cost.
        const float PavingSeconds = 0.1f;
        const float SmallSeconds = 4f;
        const float MediumSeconds = 8f;
        const float LargeSeconds = 15f;
        const float MonumentSeconds = 30f;

        // Floors are laid a square at a time, or every errand would be one cell and the
        // agent would spend the burst walking between them. Wide, because a cell costs
        // nothing to lay and a small square is over before the walk out there was worth
        // it.
        const int PavingSide = 7;

        // How far from the pawn an errand may be opened. A few steps, so the walk out to
        // it is not what the burst is spent on, and wide enough that a clanker standing
        // still is not laying every square on the same cell.
        //
        // The near edge is not decoration. A pawn standing inside a frame *blocks* it,
        // vanilla answers that with a job to go and stand somewhere else, and this loop
        // forces the errand back over the top a quarter second later - a clanker that
        // never starts and re-paths forever. The pad in Fits is the other half of it.
        const float SiteNear = 4f;
        const float SiteRadius = 12f;

        // How hard a dart is pulled toward the core. The direction is a random one with
        // this much of the way to the heart added, so the errands lean inward without ever
        // only going that way: ground by the core fills first, which is what a colony
        // looks like, where paving that spread out from wherever an agent happened to
        // wander looks like litter. A weight rather than a rule, because when the ground
        // inside is full the darts still have to be able to find the ground outside - and
        // it fades out on its own as the pawn approaches the heart, where there is no
        // inward left to point.
        const float SiteLean = 0.8f;

        // How far past the plague's own girth an agent will carry an errand, and the whole
        // of what holds this site together. There is a ring of living ground this wide
        // outside the plague, and that ring is where everything gets built.
        //
        // It cannot stall and it cannot run away. Building anywhere blooms, blooming grows
        // the plague, and the plague's girth is what this is measured off - so the room to
        // work in is opened by the work. And girth is a bulk rather than a furthest reach
        // (see Plague.Girth), so opening the next ring costs ground that actually died
        // rather than one plate thrown at the edge.
        const float RoamMargin = 16f;

        // The floor under it, so the plague's first seconds do not have the agents hemmed
        // into the core itself.
        const float MinCircle = 18f;

        // What share of the errands each thing is, out of a hundred. This block is the
        // whole of the tuning: they are whole numbers and they sum to a hundred, so a
        // line here reads as the share of the finished site that will be that thing,
        // which is the only question anyone adjusting this is asking. Nothing enforces
        // the sum - Pick normalises whatever it is handed, and it has to, because it
        // weighs only what the pawn in front of it could finish.
        //
        // Paving is four errands in five, and it has to be far more than the half it was.
        // These are shares of the errands *handed out*, and an errand is not a unit of time:
        // a plate is a tenth of a second and a grand stele is thirty, so at even odds by
        // count the agents spend nine tenths of every burst standing over something with a
        // shape and the site reads as a machine that does not pave. Four in five by count is
        // about one working minute in three spent laying floor, which is the least that
        // reads as paving being what this thing does.
        const float PavingOdds = 10f;

        // Markers, and what a machine told nothing about what for puts up. Rare by count is
        // what makes them events; rare by count is also most of the clock, which is why
        // there is so little of them here.
        const float ColumnOdds = 2f;
        const float GraveOdds = 5f;
        const float SarcophagusOdds = 1f;
        const float SteleLargeOdds = 1f;
        const float SteleGrandOdds = 1f;

        // The world it came out of. The rack, the screens and the cabinets carry this
        // half, because they are the ones with a silhouette worth looking at twice; the
        // lamp and the lamppost are held down to nearly nothing, since one of them lights
        // a good few cells and a field of them lights the same ground over and over.
        const float LampOdds = 5f;
        const float LamppostOdds = 1f;
        const float RackOdds = 2f;
        const float ScreensOdds = 2f;
        const float LockersOdds = 2f;
        const float GeneratorOdds = 3f;
        const float MachineOdds = 1f;

        // How far the plague walks out of each thing once it stands, in cells. The other half
        // of the tuning, and the half that says how fast the map dies: a plate is a cell's
        // worth of creep and a monument is a bloom you can watch arrive.
        //
        // Roughly flat per second of an agent's working time, which is deliberate - the map
        // has to die at the speed the sessions are busy and not at the speed of whichever
        // errand the darts happen to favour. Paving is under the rest by that measure and
        // still does most of the killing, because it is forty-nine frames an errand.
        const float PavingBloom = 3f;
        const float SmallBloom = 4f;
        const float MediumBloom = 6f;
        const float LargeBloom = 8f;
        const float MonumentBloom = 11f;

        struct Errand
        {
            public BuildableDef What;
            public float Seconds;
            public float Weight;
            public float Bloom; // cells of plague the finished thing seeds
            public int Patch; // side of the square laid at once; 1 for anything with a shape
        }

        // Keyed on what is being built rather than on the frame, because the frame is
        // gone by the time anything asks a second time. Static: the table is the same
        // table on every map, and Patch_ErrandWork has no map to ask.
        static readonly Dictionary<BuildableDef, float> Work = new Dictionary<BuildableDef, float>();
        static readonly Dictionary<BuildableDef, float> Blooms = new Dictionary<BuildableDef, float>();

        public static float WorkFor(BuildableDef def) =>
            def != null && Work.TryGetValue(def, out float w) ? w : 0f;

        public static float BloomFor(BuildableDef def) =>
            def != null && Blooms.TryGetValue(def, out float r) ? r : 0f;

        // Not saved: both are questions about the tile and the def database, and a load
        // can ask them again.
        List<Errand> _errands;
        TerrainDef _plate;
        ThingDef _blocks;
        ThingDef _rock;
        bool _quarried;

        // Held for the length of a pass rather than passed down through five calls: the
        // circle is asked about once per candidate cell, and a paving square is fifty of
        // those.
        Plague _plague;

        // The tick placement may be attempted again. Not saved: a reload is welcome to
        // have one more go.
        int _blocked;

        // Passes since the last sweep.
        int _swept;

        // Cells the site has finished with, for the length of the colony. Scribed, being a
        // colony's whole history of having been busy - and it is only that now: what the
        // plague has is written down in the plague's own field.
        int _laid;

        // The frame each agent was last sent to, and the frames being passed over.
        //
        // Being asked for an errand at all means the last job did not survive a quarter
        // of a second: a pass that found the pawn on FinishFrame leaves it alone. So
        // whatever we were told about that frame was wrong, whoever was wrong about it,
        // and it is passed over until the next sweep rather than handed straight back.
        // Two systems can disagree about one frame forever otherwise, and from the
        // outside that is a clanker walking to the same spot for the life of the colony.
        readonly Dictionary<Pawn, Frame> _sent = new Dictionary<Pawn, Frame>();
        readonly HashSet<Frame> _shunned = new HashSet<Frame>();

        // Scratch, held rather than allocated: the sweep runs once a second forever.
        readonly List<Pawn> _hands = new List<Pawn>();
        readonly List<Pawn> _stale = new List<Pawn>();

        // How often the standing frames are looked over, in passes - once a second.
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

            // Neither scene wants a clanker wandering off to lay a floor in the middle of
            // it.
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
                // the frame, and vanilla's own answer to that is a hauler this map has not
                // got.
                if (pawn.CurJobDef == JobDefOf.FinishFrame)
                {
                    Fill(pawn.CurJob?.targetA.Thing as Frame);
                    continue;
                }

                Send(pawn);
            }
        }

        // The daemon's word is the whole of it: an agent that has gone quiet puts the
        // hammer down where it stands, and the frame keeps what it has been given.
        static void Stop(Pawn pawn)
        {
            if (pawn.jobs != null && pawn.CurJobDef == JobDefOf.FinishFrame)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);

            Allow(pawn, false);
        }

        // An agent is allowed exactly one kind of work, and only while the process it
        // stands for is busy. Everything else is off, always.
        //
        // This is not tidiness. A colonist with the vanilla work sheet is a colonist
        // vanilla's own work givers will find jobs for - blood to clean, steel to haul
        // out of a frame the plague blew up - and this loop comes back a quarter of a
        // second later and forces the errand over the top of whatever it had started.
        // Two systems taking turns at one pawn reads exactly as it is: a clanker that
        // turns round every few steps and never arrives anywhere.
        //
        // Construction itself is on the same switch, for the same reason from the other
        // side: left on, the work giver hands an *idle* agent the nearest frame, and the
        // one thing the site is supposed to say is which processes are busy.
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

            // Something is in the way of a frame and vanilla has sent the pawn to stand
            // elsewhere so it can be built. That job is the game agreeing with us; forcing
            // the errand back over it is how a clanker ends up walking on the spot.
            if (pawn.CurJobDef == JobDefOf.Goto) return;

            Frame stale;
            if (_sent.TryGetValue(pawn, out stale))
            {
                _sent.Remove(pawn);
                if (stale != null && stale.Spawned) _shunned.Add(stale);
            }

            var frame = Free(pawn) ?? Open(pawn);
            if (frame == null) return;

            // Asked of an opened frame as well as a found one, because a frame can be
            // blocked the moment it is placed - by whoever wandered past while it was being
            // placed, or by the pawn that asked for it.
            if (!Buildable(frame, pawn)) return;

            Fill(frame);
            if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.FinishFrame, frame), JobTag.Misc))
                _sent[pawn] = frame;
        }

        // The driver's own fail condition, asked one tick early and asked *the same way*.
        //
        // It used to be asked the easy way - skills off, forced on - and the gap between
        // the two is the whole of how a clanker gets stuck. A sarcophagus wants
        // Construction 5 (vanilla's own figure, in Buildings_Misc.xml); with skills off
        // the frame is handed to a clanker with three, which walks the length of the site,
        // is refused on arrival because the fail condition sits on the build toil and not
        // the walk, and is handed the same frame a quarter of a second later. It is the
        // nearest, it is unreserved, and nothing takes it away: that frame traps every
        // low-skilled agent that ever comes near it, for the life of the colony.
        static bool Buildable(Frame frame, Pawn pawn) =>
            GenConstruct.CanConstruct(frame, pawn, true, false);

        // Construction has to be *on* for as long as the hammer is swinging:
        // GenConstruct.CanConstruct reads the work settings and the job driver fails on
        // that every tick it runs. Patch_AgentsCanBuild is what makes sure the answer may
        // be yes whatever backstory the pawn was handed; Allow is what says it, and Stop
        // is what takes it back the moment the process goes quiet.
        static bool Ready(Pawn pawn)
        {
            if (pawn.jobs == null || pawn.workSettings == null) return false;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return false;

            Allow(pawn, true);
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
                if (_shunned.Contains(frame)) continue;
                // Cheap first: the reservation is a dictionary lookup and the rest is a
                // path.
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
                var at = Site(pawn);
                var frame = errand.Patch > 1 ? Pave(errand, pawn, at) : Raise(errand, at, pawn);
                if (frame != null) return frame;
            }

            // Thirty sites and nowhere to put it. For the floor that is what a full circle
            // looks like from in here - it is one cell, it wants no clearance, and it goes
            // down on anything - and a full circle is the state this is all aimed at, so it
            // must be cheap to be in. Every agent asking four times a second otherwise
            // means five hundred CanPlaceBlueprintAt calls a second against ground that is
            // not going to change.
            //
            // Anything with a shape asks for a footprint and a pad around it, so its
            // failing says only that there is no room for *it* within a few steps of this
            // pawn. Sitting the whole site down on that would be a five-by-three machine
            // stopping every agent who still had somewhere to lay a plate - and the floor
            // is more than half the table, so a circle that really is full arms this within
            // a look or two regardless.
            if (errand.Patch > 1) _blocked = now + BlockedFor;
            return null;
        }

        // A frame nobody on this map can ever finish is worse than no frame at all. It
        // counts against MaxOpen, it is picked up by nothing, and it stands there for the
        // life of the colony - so a site left alone fills its own quota with rubbish and
        // the agents run out of anywhere to build while the ground is still empty. That
        // is what a clanker with nothing to do looks like from the outside: walking,
        // stopping, walking again.
        //
        // Blocked means vanilla's own word for it. GenConstruct.FirstBlockingThing is the
        // question the job driver asks every tick it runs, and the answer is a thing
        // somebody would have to cut down or carry off first - which on this map is work
        // no agent is allowed and no hauler exists to do. A plant grew into it, a chunk
        // landed on it, the plague blew something over it. It goes.
        //
        // Placement declines these on the way in (Fits), but only what it can see: this
        // is for the ground changing afterwards, which on a map being eaten is most of
        // it.
        void Sweep()
        {
            // One look each is enough, and the list is short. A frame this colony has
            // nobody skilled enough for is rubbish in exactly the way a blocked one is:
            // nothing will ever take it, and it counts against MaxOpen while it stands.
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

            // A frame passed over for a quarter of a second gets another chance now: the
            // ones that deserved to be passed over for good have just been destroyed.
            _shunned.Clear();
            Prune();

            if (doomed == null) return;

            for (int i = 0; i < doomed.Count; i++)
                if (!doomed[i].Destroyed) doomed[i].Destroy(DestroyMode.Vanish);

            // Room where there was none. Whatever the last round of darts concluded about
            // this map is out of date now.
            _blocked = 0;
        }

        // With nobody on the map to ask, nothing is condemned: an empty colony is not the
        // same answer as a colony of clumsy hands.
        bool Anyone(BuildableDef what)
        {
            if (_hands.Count == 0) return true;
            for (int i = 0; i < _hands.Count; i++)
                if (Skilled(what, _hands[i])) return true;
            return false;
        }

        // Agents come and go, and a dictionary keyed on pawns would hold every one of
        // them that ever landed.
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

        // Ground this site has finished with, in cells. It no longer buys anything - the
        // plague grows off the blooms rather than off a tally - so this is what it always
        // read as: how busy the sessions have been, in ground. The plague's log line is the
        // one place it is printed, next to what the plague has made of it.
        public static int LaidOn(Map map) => map?.GetComponent<Worksite>()?._laid ?? 0;

        // The footprint, so a five-by-three machine is worth what fifteen plates are. This
        // is ground taken rather than work done: an hour on a grand stele covers one cell.
        void Count(int cells) => _laid += cells;

        // Weighted, and only over what the pawn asking could finish. A clanker that opens
        // a frame beyond its own hands has built the trap it then walks into.
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

        // Vanilla's two prerequisites, read off the def rather than guessed at. This is
        // the half of CanConstruct that is about the pawn rather than about the ground,
        // and the only half that can be asked before the frame exists.
        static bool Skilled(BuildableDef what, Pawn pawn)
        {
            var skills = pawn?.skills;
            if (what == null || skills == null) return true;

            return what.constructionSkillPrerequisite
                       <= skills.GetSkill(SkillDefOf.Construction).Level
                && what.artisticSkillPrerequisite
                       <= skills.GetSkill(SkillDefOf.Artistic).Level;
        }

        // Next to whoever is asking. An agent with nothing to do wanders, and where it
        // has wandered is as fair a spread as this wants: the ground fills the way the
        // colony moves over it, and the walk out to the errand is a few steps rather
        // than a crossing. A dart at the circle instead put every errand somewhere else
        // and filled the middle first.
        //
        // A pawn that has wandered out past the leash is aimed back inside it, or an agent
        // caught out there would spend every look asking for ground it is not allowed.
        //
        // Round rather than truncate, or the sites lean one way by half a cell in both
        // axes.
        IntVec3 Site(Pawn pawn)
        {
            bool home = Near(pawn.Position);
            var from = home ? pawn.Position : Heart();

            // An annulus rather than a disc when it is aimed at the pawn: the middle of
            // that disc is the cell the pawn is standing in.
            var dir = Rand.InsideUnitCircleVec3.normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            // And leaning toward the core, which is the one direction on this map that
            // means anything. Zero when the pawn is standing on the heart, so the pull
            // never has to be switched off.
            dir = (dir + Pull(pawn.Position)).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            var v = home
                ? dir * Rand.Range(SiteNear, SiteRadius)
                : Rand.InsideUnitCircleVec3 * Roam();

            return from + new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));
        }

        // The way to the core, at SiteLean's strength, and nothing at all within a step of
        // it. A pawn a long way out pulls no harder than one nearby: the lean is a bias on
        // a direction, not a distance, and a dart dragged the whole way to the heart from
        // the edge would be an errand nobody walks to.
        Vector3 Pull(IntVec3 from)
        {
            var to = (Heart() - from).ToVector3();
            return to.magnitude < 1f ? Vector3.zero : to.normalized * SiteLean;
        }

        // How far from the middle the colony may work: the plague as a circle, plus the ring
        // of living ground outside it. A distance rather than the plague's own shape, and it
        // has to be - the region is a union of a few thousand stamps now, and "within a few
        // cells of somewhere dead" asked of every candidate cell is a few hundred lookups
        // where this is one. The darts want a distance and nothing else.
        float Roam() => _plague == null ? 0f : Mathf.Max(_plague.Girth + RoamMargin, MinCircle);

        IntVec3 Heart() => _plague == null ? map.Center : _plague.Heart;

        bool Near(IntVec3 cell) =>
            _plague != null && _plague.Active && cell.DistanceTo(_plague.Heart) <= Roam();

        Frame Raise(Errand errand, IntVec3 at, Pawn pawn)
        {
            var td = errand.What as ThingDef;
            var rot = td != null && td.rotatable ? Rot4.Random : Rot4.North;
            if (!Fits(errand.What, at, rot, pawn)) return null;
            return Pitch(errand.What, at, rot);
        }

        // A square of it, around the cell the site landed on - which, the site being
        // where the pawn is, is the ground it has been standing on.
        Frame Pave(Errand errand, Pawn pawn, IntVec3 centre)
        {
            Frame first = null;
            foreach (var c in CellRect.CenteredOn(centre, errand.Patch / 2))
            {
                if (!Fits(errand.What, c, Rot4.North, pawn)) continue;
                var frame = Pitch(errand.What, c, Rot4.North);
                if (frame != null && first == null) first = frame;
            }
            return first;
        }

        bool Fits(BuildableDef what, IntVec3 at, Rot4 rot, Pawn pawn)
        {
            if (!at.InBounds(map) || at.Fogged(map)) return false;

            // Near the middle, wherever the pawn that asked has got to. Not *in* the plague:
            // what the agents build is what kills the ground, so a site allowed only on ash
            // could never lay its first plate. The leash is what keeps the colony reading as
            // a colony rather than as litter over a whole map.
            if (!Near(at)) return false;

            // A pad around anything with a shape, so a site never closes a path off or
            // grows into one lump. Floors want none of it: paving right up to a monument is
            // the point of them.
            int pad = what is TerrainDef ? 0 : 1;
            bool floor = what is TerrainDef;
            var footprint = GenAdj.OccupiedRect(at, rot, what.Size);

            // What vanilla would want cleared off the ground before this could go up. A
            // colony answers that with somebody to cut the tree and somebody to carry the
            // chunk away; this map has neither, and an agent is allowed no work but its
            // own. So a cell that would ask for either is not a site.
            //
            // Read off the *blueprint* rather than off what is being built, because for a
            // floor the two disagree and only one of them is the answer the game will give.
            // ThingDefGenerator_Buildings.NewBlueprintDef_Terrain sets both flags false:
            // nothing is carried off or cut down to lay a plate, and only a plant worth
            // harvesting blocks one at all (GenConstruct.BlocksConstruction). The TerrainDef
            // has clearBuildingArea true, as every BuildableDef does by default, and reading
            // it there was refusing every cell with a blade of grass or a chunk of slag in
            // it - which on the rim of the circle, where the weak band leaves plants
            // standing, and anywhere a detonation has been, is most of them. A round of
            // darts that lands there finds nowhere to pave, and BlockedFor sits the whole
            // site down on that answer, so the visible result was clankers that only ever
            // built things.
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
                    if (thing is Building || thing is Blueprint) return false;
                    if (!footprint.Contains(c)) continue;

                    if (clear && thing.def.category == ThingCategory.Plant) return false;
                    if (tidy && thing.def.category == ThingCategory.Item) return false;

                    // The one thing a floor does have to have off the ground first, and
                    // vanilla is the one saying so rather than us. Declined here rather than
                    // left to Sweep, which is where a frame nobody can finish goes.
                    if (floor && Rooted(thing)) return false;

                    // Anything standing where the thing is going blocks it being built, and
                    // being blocked is the state this must never open a frame into: the pawn
                    // is told to move, we tell it to build, and neither of us wins. Only for
                    // what has a shape, since nothing has to move out of the way of a floor.
                    if (!floor && thing is Pawn) return false;
                }
            }

            // Paving a cell that is already paved builds nothing and the sweep would come
            // back to it forever.
            if (what is TerrainDef terrain && map.terrainGrid.TerrainAt(at) == terrain) return false;

            if (!GenConstruct.CanPlaceBlueprintAt(what, at, rot, map, false, null, null, StuffFor(what))
                    .Accepted) return false;

            return pawn.CanReach(at, PathEndMode.Touch, Danger.Deadly);
        }

        // Where GenConstruct.BlocksConstruction draws the line for a terrain frame: a
        // dandelion's harvest work is the threshold between what a plate goes straight over
        // and what somebody would have to cut down first. Grass is paved over; a tree is
        // not.
        static bool Rooted(Thing thing)
        {
            var plant = thing.def.category == ThingCategory.Plant ? thing.def.plant : null;
            return plant != null &&
                   plant.harvestWork > ThingDefOf.Plant_Dandelion.plant.harvestWork;
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

        // Nothing this lot built follows the colony off the planet. Nothing should be
        // able to - a new planet is a new map, generated from nothing this one touched -
        // but the site is the one thing here that leaves permanent marks on the board,
        // and a monument turning up on the next world would be the kind of bug nobody
        // thinks to look for. So it is stated: the frames, the blueprints and everything
        // finished go before the map does.
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

                Blocks(); // which is what settles the rock the monuments are quarried from

                // Metal plate rather than the tile's own flagstone: a machine paving over
                // ash lays down what it is made of, and stone here read as a garden path
                // through a dead world. Steel, which the map has none of and never needed.
                _plate = DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");

                Add(_plate, PavingSeconds, PavingOdds, PavingBloom, PavingSide);

                // Monuments and graves. What a machine builds when it is told nothing about
                // what for: a marker, a place to put somebody, and a slab with writing on it
                // that nobody will read.
                Add(Named("Column"), SmallSeconds, ColumnOdds, SmallBloom);
                Add(Named("Grave"), SmallSeconds, GraveOdds, SmallBloom);
                Add(Named("Sarcophagus"), MediumSeconds, SarcophagusOdds, MediumBloom);
                Add(Named("SteleLarge"), LargeSeconds, SteleLargeOdds, LargeBloom);
                Add(Named("SteleGrand"), MonumentSeconds, SteleGrandOdds, MonumentBloom);

                // And the other half of what it was told nothing about: the world it came
                // out of. Racks, screens, cabinets, a generator, and one machine the size of
                // a house that does nothing. Vanilla ships these as scenery for its own
                // ruins and never lets a player build one - Patches/AncientBuildings.xml is
                // what hands them a frame. They cost nothing and want no skill, so every
                // agent that lands can raise one and the frame is ready the tick it is
                // placed.
                //
                // The lamp is the only thing on this list that is not decoration: a
                // CompGlower with neither a power comp nor a fuel one, which makes it the
                // one light in the game that simply burns - no grid, no hauler, nothing to
                // run out. A few of them is a night with somewhere to walk; the rest is
                // daylight, so it is one of the rarest things here rather than one of the
                // commonest.
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

        void Add(BuildableDef what, float seconds, float weight, float bloom, int patch = 1)
        {
            if (what == null || what.frameDef == null) return;

            _errands.Add(new Errand
            {
                What = what, Seconds = seconds, Weight = weight, Bloom = bloom, Patch = patch,
            });
            Work[what] = seconds * RealClock.TicksPerRealSecond * WorkPerTick;
            Blooms[what] = bloom;
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

        // Where a thing standing up becomes plague. A prefix, because by the time
        // CompleteConstruction returns the frame is despawned and has neither a map nor a
        // footprint to ask about - and the footprint is the whole point, a machine five by
        // three being a source five by three wide rather than a point.
        //
        // Only our own defs. Nothing else on this map is ever built, but both lookups answer
        // zero for anything off the errand table regardless, so a frame from somewhere else
        // kills nothing.
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

        // A terrain frame is drawn as four white corner brackets, and the site queues
        // paving a square at a time - so the ground ahead of the agents is a grid of
        // them, which is most of what is on the board. They say nothing anybody can act
        // on: there is no order to cancel, no material to deliver and nothing to decide.
        // The floor arrives without the scaffolding.
        //
        // Anything with a shape keeps its frame. A monument is half a minute of somebody
        // being busy and worth watching go up.
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
