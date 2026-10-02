using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Run one opening scene for each new colony. Block map and UI input during the scene.
    // ModScenario supplies no starting pawns. This scene adds pawns, the core, and the plague.
    // Save the phase and remaining population; rebuild animal kinds after loading.
    public class IntroDirector : GameComponent
    {
        const int AnimalsMin = 50;
        const int AnimalsMax = 70;
        const int HumansMin = 20;
        const int HumansMax = 30;

        // Limit pawn generation per tick to reduce frame delays.
        const int SpawnsPerTick = 6;

        const float FumeSeconds = 3f;

        // Allow the plague to start before agents land.
        const float SeedSeconds = 3f;

        // Allow time for session reconciliation, pod descent, and pod opening.
        const float BloomSeconds = 6f;

        // Place the core directly if its skyfaller does not land before this timeout.
        const float FallSeconds = 12f;

        // Game ticks between core vent effects.
        const int PuffInterval = 10;

        // Random placement attempts before using the map center as a fallback.
        const int PlacementTries = 30;

        // Treat a game beyond this tick count as a loaded colony.
        const int FreshGameTicks = 2000;

        enum Phase { Waiting, Populate, Core, Fume, Seed, Bloom, Done }

        // Save the current scene phase.
        Phase _phase = Phase.Waiting;

        Map _map;
        List<PawnKindDef> _animalKinds;
        int _animalsLeft, _humansLeft;
        bool _populationArmed;

        // Each phase transition resets the flag and timer.
        bool _armed;
        float _at;

        // Keep this state only in memory so loading a save restores the UI.
        public static bool UiHidden { get; private set; }

        // Hold session reconciliation until the scene permits agent arrivals.
        public static bool AgentsHeld { get; private set; }

        // Reset static state so a new colony does not inherit a hidden UI or suspended arrivals.
        public IntroDirector(Game game)
        {
            UiHidden = false;
            AgentsHeld = false;
        }

        public static IntroDirector Current => Verse.Current.Game?.GetComponent<IntroDirector>();

        Map TheMap => _map ?? (_map = Find.CurrentMap);

        // Advance these phases even when the game is paused.
        // A new colony starts paused until TimeKeeper resumes it. Scene delays use real time.
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

        // Create pawns, buildings, and vent effects during game ticks.
        public override void GameComponentTick()
        {
            switch (_phase)
            {
                case Phase.Populate: StepPopulate(); break;
                case Phase.Core: DropCore(); break;
                case Phase.Fume: Vent(); break;
            }
        }

        // Reset the timer and flag for the new phase.
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

            // Finish the scene for an existing colony. Restore the UI and permit agent arrivals.
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

        // Generate pawns without a faction as plague targets. They do not join the colonist bar.
        // Center the camera so the core fumes remain on screen. Fleck generation excludes areas outside the screen.
        void StepPopulate()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            if (!_populationArmed)
            {
                _populationArmed = true;
                Find.CameraDriver?.JumpToCurrentMapLoc(map.Center);
                Pets.Place(map);
            }

            if (_animalKinds == null) _animalKinds = Outskirts.Kinds(map);
            if (_animalKinds.Count == 0) _animalsLeft = 0;

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

        // Use an existing core if one is already on the map.
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
                // Place the core directly so the scene can continue.
                Log.Warning("[SlopWorld] Persona core never landed. Placing it.");
                Ground(map);
            }

            Go(Phase.Fume, FumeSeconds);
        }

        // The skyfaller has no graphicData, so it draws the core that it carries.
        // Use ShipChunkIncoming to avoid the ground damage from the explosive variant.
        void Fall(Map map)
        {
            var cell = map.Center;

            try
            {
                var core = ThingMaker.MakeThing(ModDefOf.Ship_ComputerCore);
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
                GenSpawn.Spawn(ThingMaker.MakeThing(ModDefOf.Ship_ComputerCore),
                    map.Center, map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] persona core: {e.Message}");
            }
        }

        // Show core fumes before starting the plague.
        void Vent()
        {
            if (Find.TickManager.TicksGame % PuffInterval != 0) return;
            PlagueFx.Fume(TheCore(TheMap));
        }

        void WaitOnFumes()
        {
            if (!Held) Go(Phase.Seed);
        }

        // Start the plague before permitting agent arrivals.
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

        // Allow time to show arriving pods before restoring the UI.
        // AgentColony controls the arrival schedule.
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
            var found = map?.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore);
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
            Scribe_Values.Look(ref _animalsLeft, "introAnimalsLeft", 0);
            Scribe_Values.Look(ref _humansLeft, "introHumansLeft", 0);
            // Preserve population setup so loading does not replace the already placed pet.
            Scribe_Values.Look(ref _populationArmed, "introPopulationArmed", false);
        }
    }
}
