using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The opening scene of a fresh colony, once, as a cutscene: nothing on the map is
    // clickable and no UI is drawn until it is over.
    //
    // A hillside populated with living animals and people, the cat among them - all
    // placed, because a hundred pods would be a different scene - and then the persona
    // core falls into the middle of it. It vents alone for a couple of seconds, so what
    // follows reads as having come out of it: the plague is armed, and only then do the
    // clankers come down into it.
    //
    // Nobody is standing on this map at tick zero. The scenario hands over no people
    // (SlopScenario), so the only things that fall are the ones this scene calls for,
    // in the order it wants them. The living are placed alive and left to the plague,
    // which is cheaper than killing them on camera and is the whole point.
    //
    // The phase is persisted so a reload never replays the intro; the work lists are
    // not, so a reload mid-intro skips ahead.
    public class IntroDirector : GameComponent
    {
        // How much life the scene puts on the map before killing it.
        const int AnimalsMin = 50;
        const int AnimalsMax = 70;
        const int HumansMin = 20;
        const int HumansMax = 30;

        // Generating a pawn is expensive; a few per tick keeps the frame smooth.
        const int SpawnsPerTick = 6;

        const float FumeSeconds = 3f;

        // The plague spreading with nothing on the map to answer it. Short, because the
        // hold is only there so the clankers are seen landing *into* something.
        const float SeedSeconds = 3f;

        // Longer than the beats around it, because it is waiting on three things in a
        // row that are not this component's: the reconcile's own second, the pods' fall,
        // and the delay they take to open.
        const float BloomSeconds = 6f;

        // A fallback against a skyfaller that never landed, not a timer anything is
        // supposed to hit.
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

        // Runtime only - a save loaded mid-scene comes back with the UI on rather than
        // stuck hidden.
        public static bool UiHidden { get; private set; }

        // The reconcile holds off, so the agents come down on their cue instead of being
        // on the board before the thing that made them is.
        public static bool AgentsHeld { get; private set; }

        // Both flags are static, so a colony discarded during its own intro must not hand
        // the next one a hidden UI.
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

            // A load, not a fresh landing. Finish rather than marking it done, so a colony
            // abandoned mid-scene cannot leave the UI hidden or the agents held.
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

        // Everything spawns alive and factionless: they wander, they never join the
        // colonist bar, and they are here to die of the plague. The cat goes down with
        // the first of them - she is part of the hillside, not an arrival - and the
        // camera takes the middle now and keeps it for the rest of the scene, which is
        // also what makes the fumes exist: flecks are not spawned off screen.
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
                // The rest of the scene needs a core standing, so it gets put there without the
                // theatre rather than not at all.
                Log.Warning("[SlopWorld] persona core never landed; placing it");
                Ground(map);
            }

            Go(Phase.Fume, FumeSeconds);
        }

        // ShipChunkIncoming is vanilla's own carrier for wreckage: with no graphicData of
        // its own the skyfaller draws its payload, so what falls is the core. It is also
        // the harmless one - the variant that blows a hole in the ground is a separate
        // def, which matters because the scene's own hillside is standing underneath.
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

        // Nothing is marked yet and the plague is not armed - this is the core alone, so
        // that what comes next is read as having come out of it.
        void Vent()
        {
            if (Find.TickManager.TicksGame % PuffInterval != 0) return;
            PlagueFx.Fume(TheCore(TheMap));
        }

        void WaitOnFumes()
        {
            if (!Held) Go(Phase.Seed);
        }

        // The plague goes first and the clankers come down into it, in that order and
        // with a beat between: a pod that lands on a hillside and a pod that lands on a
        // map already turning are two different arrivals, and this is the one where the
        // core made them.
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

        // The agents are dropped by AgentColony's own reconcile on its own second, so
        // this is a beat: long enough for the pods to be worth looking at.
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
