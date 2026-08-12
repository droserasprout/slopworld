using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One persisted cutscene per fresh colony: block map/UI input while placing the hillside,
    // core, plague and clankers. SlopScenario supplies no pawns; this scene adds everything.
    // The phase persists but work lists do not, so a reload mid-intro skips ahead.
    public class IntroDirector : GameComponent
    {
        const int AnimalsMin = 50;
        const int AnimalsMax = 70;
        const int HumansMin = 20;
        const int HumansMax = 30;

        // Generating a pawn is expensive; a few per tick keeps the frame smooth.
        const int SpawnsPerTick = 6;

        const float FumeSeconds = 3f;

        // Long enough that the clankers are seen landing *into* something.
        const float SeedSeconds = 3f;

        // Waiting on three things that are not this component's: the reconcile's second, the
        // pods' fall, and the delay they take to open.
        const float BloomSeconds = 6f;

        // A fallback against a skyfaller that never landed, not a timer anything should hit.
        const float FallSeconds = 12f;

        // Ticks between breaths of the core's vent.
        const int PuffInterval = 10;

        // Tries at a random standable cell before a spawn gives up on the middle.
        const int PlacementTries = 30;

        // A brand-new game is only a few ticks in; anything past this is a load.
        const int FreshGameTicks = 2000;

        enum Phase { Waiting, Populate, Core, Fume, Seed, Bloom, Done }

        // Persisted: how far through the scene we are.
        Phase _phase = Phase.Waiting;

        Map _map;
        List<PawnKindDef> _animalKinds;
        int _animalsLeft, _humansLeft;

        // Both are cleared by every transition, so a phase reads them without caring what
        // the last one left behind.
        bool _armed;
        float _at;

        // Runtime only: a save loaded mid-scene comes back with the UI on rather than stuck
        // hidden.
        public static bool UiHidden { get; private set; }

        // The reconcile holds off, so the agents come down on their cue.
        public static bool AgentsHeld { get; private set; }

        // Both are static, so a colony discarded during its own intro must not hand the next
        // one a hidden UI.
        public IntroDirector(Game game)
        {
            UiHidden = false;
            AgentsHeld = false;
        }

        public static IntroDirector Current => Verse.Current.Game?.GetComponent<IntroDirector>();

        Map TheMap => _map ?? (_map = Find.CurrentMap);

        // Phases that must advance while the game is paused: a new colony starts on a
        // pause TimeKeeper has yet to lift, and the held beats burn in real time.
        public override void GameComponentUpdate()
        {
            switch (_phase)
            {
                case Phase.Waiting: TryBegin(); break;
                case Phase.Fume: WaitOnFumes(); break;
                case Phase.Seed: SeedPlague(); break;
                case Phase.Bloom: WaitOnBloom(); break;
            }
        }

        // Pawns and buildings only stick once the map is live and ticking.
        public override void GameComponentTick()
        {
            switch (_phase)
            {
                case Phase.Populate: StepPopulate(); break;
                case Phase.Core: DropCore(); break;
                case Phase.Fume: Vent(); break;
            }
        }

        // So no phase inherits the last one's timer or its one-off flag.
        void Go(Phase next, float hold = 0f)
        {
            _phase = next;
            _armed = false;
            _at = Time.realtimeSinceStartup + hold;
        }

        bool Held => Time.realtimeSinceStartup < _at;

        void TryBegin()
        {
            var map = TheMap;
            if (map == null) return;

            // A load. Finish rather than marking it done, so a colony abandoned mid-scene
            // cannot leave the UI hidden or the agents held.
            if (Find.TickManager.TicksGame > FreshGameTicks) { Finish(); return; }

            UiHidden = true;
            AgentsHeld = true;
            BeginScene();
        }

        void BeginScene()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            _animalKinds = Outskirts.Kinds(map);
            _animalsLeft = Rand.Range(AnimalsMin, AnimalsMax);
            _humansLeft = Rand.Range(HumansMin, HumansMax);

            Go(Phase.Populate);
        }

        // Alive and factionless: they wander, never join the colonist bar, and are here to die
        // of the plague. The camera takes the middle now and keeps it, which is also what
        // makes the fumes exist - flecks are not spawned off screen.
        void StepPopulate()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            if (!_armed)
            {
                _armed = true;
                Find.CameraDriver?.JumpToCurrentMapLoc(map.Center);
                Pets.Place(map);
            }

            int budget = SpawnsPerTick;
            while (budget-- > 0 && (_animalsLeft > 0 || _humansLeft > 0))
            {
                if (_animalsLeft > 0)
                {
                    Spawn(map, _animalKinds.RandomElement());
                    _animalsLeft--;
                }
                else
                {
                    Spawn(map, PawnKindDefOf.Colonist);
                    _humansLeft--;
                }
            }

            if (_animalsLeft <= 0 && _humansLeft <= 0) Go(Phase.Core);
        }

        static void Spawn(Map map, PawnKindDef kind)
        {
            if (kind == null) return;
            try
            {
                if (!TryRandomStandable(map, out var cell)) return;

                var req = new PawnGenerationRequest(kind, null,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(req), cell, map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] scene pawn: {e.Message}");
            }
        }

        // Reuses a core already on the map, so a reload mid-scene never leaves two.
        void DropCore()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            if (!_armed)
            {
                _armed = true;
                _at = Time.realtimeSinceStartup + FallSeconds;
                if (TheCore(map) == null) Fall(map);
                return;
            }

            if (TheCore(map) == null)
            {
                if (Held) return; // still on its way down
                // The rest of the scene needs a core standing.
                Log.Warning("[SlopWorld] persona core never landed; placing it");
                Ground(map);
            }

            Go(Phase.Fume, FumeSeconds);
        }

        // With no graphicData of its own a skyfaller draws its payload, so what falls is the
        // core. ShipChunkIncoming is also the harmless one - the variant that blows a hole in
        // the ground is a separate def, and the hillside is standing underneath.
        void Fall(Map map)
        {
            var cell = map.Center;

            try
            {
                var core = ThingMaker.MakeThing(SlopDefOf.Ship_ComputerCore);
                GenSpawn.Spawn(
                    SkyfallerMaker.MakeSkyfaller(ThingDefOf.ShipChunkIncoming, core),
                    cell, map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] persona core skyfaller: {e.Message}");
                Ground(map);
            }
        }

        static void Ground(Map map)
        {
            try
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(SlopDefOf.Ship_ComputerCore),
                    map.Center, map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] persona core: {e.Message}");
            }
        }

        // The core alone: nothing is marked yet and the plague is not armed.
        void Vent()
        {
            if (Find.TickManager.TicksGame % PuffInterval != 0) return;
            PlagueFx.Fume(TheCore(TheMap));
        }

        void WaitOnFumes()
        {
            if (!Held) Go(Phase.Seed);
        }

        // The plague goes first, with a beat before the clankers come down into it.
        void SeedPlague()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            if (!_armed)
            {
                _armed = true;
                _at = Time.realtimeSinceStartup + SeedSeconds;
                map.GetComponent<Plague>()?.Arm(TheCore(map)?.Position ?? map.Center);
                return;
            }

            if (Held) return;

            AgentsHeld = false; // the reconcile may drop the agents in now
            Go(Phase.Bloom, BloomSeconds);
        }

        // AgentColony drops the agents on its own second, so this is just a beat long enough
        // for the pods to be worth looking at.
        void WaitOnBloom()
        {
            if (!Held) Finish();
        }

        void Finish()
        {
            _phase = Phase.Done;
            _map = null;
            _animalKinds = null;
            UiHidden = false;
            AgentsHeld = false;
        }

        static Thing TheCore(Map map)
        {
            var found = map?.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            return found != null && found.Count > 0 ? found[0] : null;
        }

        static bool TryRandomStandable(Map map, out IntVec3 cell)
        {
            var size = map.Size;
            for (int t = 0; t < PlacementTries; t++)
            {
                cell = new IntVec3(Rand.Range(0, size.x), 0, Rand.Range(0, size.z));
                if (cell.Standable(map)) return true;
            }
            cell = map.Center;
            return cell.Standable(map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _phase, "introPhase", Phase.Waiting);
        }
    }
}
