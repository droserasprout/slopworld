using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Runs the opening scene of a fresh colony, once, as a cutscene: nothing on the
    /// map is clickable and no UI is drawn until it is over, so the player watches
    /// rather than plays.
    ///
    /// The beats, in order. A welcome dialog over a bare map. A hillside populated
    /// with living animals and people - placed, because they are scenery and a
    /// hundred pods would be a different scene. The scenario's three starting
    /// colonists come down in their pods, the cat comes down after them, and they
    /// are given a few seconds on the ground to walk about and read as a landing
    /// party. Then the machine persona core falls on the middle of it. It sits there
    /// venting pink for a couple of seconds - long enough to be the thing you are
    /// looking at when it happens - and then the colonists go up in a red mist and
    /// the plague starts spreading from the core. The agents walk out of that haze,
    /// one thick cloud each, and once they are standing the UI comes back.
    ///
    /// Successor to the massacre dressing: bodies are no longer placed dead, they
    /// are placed alive and killed on camera, which is both cheaper (no corpse, no
    /// blood pass) and the whole point of the thing.
    ///
    /// The phase is persisted, so a reload never replays the intro; the work lists
    /// are not, so a reload mid-intro simply skips ahead. GameComponents are built
    /// for every subclass automatically, so this needs no def.
    /// </summary>
    public class IntroDirector : GameComponent
    {
        // How much life the scene puts on the map before killing it.
        const int AnimalsMin = 50;
        const int AnimalsMax = 70;
        const int HumansMin = 20;
        const int HumansMax = 30;

        // Generating a pawn is expensive; a few per tick keeps the frame smooth,
        // same budget the corpse pass used.
        const int SpawnsPerTick = 6;

        // Real-time seconds each held beat lasts. Walk is measured from the first
        // starter standing, so it has to cover the cat's own pod falling and opening
        // - up to four seconds of it - as well as the walking it is named for.
        const float WalkSeconds = 7f;
        const float FumeSeconds = 3f;
        const float BloomSeconds = 3f;

        // How long the core gets to reach the ground before the scene stops waiting
        // on it. Generous: this is a fallback against a skyfaller that never landed,
        // not a timer anything is supposed to hit.
        const float FallSeconds = 12f;

        // Ticks between breaths of the core's vent.
        const int PuffInterval = 10;

        // How the starters go: a blast, in a pool of blood wider than the blast.
        const float PurgeBlastRadius = 2.9f;
        const int PurgeBlastDamage = 80;
        const float PurgeBloodRadius = 3.5f;
        const int PurgeBloodCount = 40;

        // Tries at a random standable cell before a spawn gives up on the middle.
        const int PlacementTries = 30;

        // A brand-new game is only a few ticks in; anything past this is a load of
        // an existing colony, which we must never touch.
        const int FreshGameTicks = 2000;

        const string WelcomeText =
            "You wake to sirens and the smell of burnt insulation.\n\n" +
            "The ship is gone. What is left of it came down across a hillside on an " +
            "unnamed rimworld, and the persona core came down with it - still " +
            "powered, still talking, still very sure of itself.\n\n" +
            "Whatever it has started here, it started before you opened your eyes.";

        enum Phase { Waiting, Welcome, Populate, Land, Core, Fume, Purge, Bloom, Done }

        // Persisted: how far through the scene we are.
        Phase _phase = Phase.Waiting;

        // Runtime only.
        Map _map;
        List<PawnKindDef> _animalKinds;
        int _animalsLeft, _humansLeft;

        // The current phase's one-off has been done, and the real time it stops
        // waiting. Both are cleared by every transition, so a phase reads them
        // without caring what the last one left behind.
        bool _armed;
        float _at;

        /// <summary>True while the scene is playing: no UI is drawn and nothing on
        /// the map can be selected, so it reads as a cutscene rather than as a colony
        /// with its buttons missing. Runtime only - a save loaded mid-scene comes back
        /// with the UI on rather than stuck hidden.</summary>
        public static bool UiHidden { get; private set; }

        /// <summary>True while the scene still has killing to do. The reconcile holds
        /// off on it, so the agents arrive on their cue instead of standing in the
        /// crowd waiting to watch themselves not die.</summary>
        public static bool AgentsHeld { get; private set; }

        // A new Game - a load, or a fresh colony - and the two flags below are
        // static, so whatever the last one was in the middle of when it was thrown
        // away would otherwise still be in force. A colony discarded during its own
        // intro must not hand the next one a hidden UI.
        public IntroDirector(Game game)
        {
            UiHidden = false;
            AgentsHeld = false;
        }

        public static IntroDirector Current => Verse.Current.Game?.GetComponent<IntroDirector>();

        Map TheMap => _map ?? (_map = Find.CurrentMap);

        // Phases that must advance while the game is paused: the dialog holds time
        // still, and the held beats burn in real time so the scene keeps its rhythm
        // whatever the clock is doing.
        public override void GameComponentUpdate()
        {
            switch (_phase)
            {
                case Phase.Waiting: TryBegin(); break;
                case Phase.Welcome: WaitOnWelcome(); break;
                case Phase.Land: WaitOnLanding(); break;
                case Phase.Fume: WaitOnFumes(); break;
                case Phase.Purge: BurnThem(); break;
                case Phase.Bloom: WaitOnBloom(); break;
            }
        }

        // Phases that spawn things. Pawns and buildings only stick once the map is
        // live and ticking; anything placed pre-tick silently vanishes.
        public override void GameComponentTick()
        {
            switch (_phase)
            {
                case Phase.Populate: StepPopulate(); break;
                case Phase.Core: DropCore(); break;
                case Phase.Fume: Vent(); break;
            }
        }

        // Every phase change goes through here, so no phase inherits the last one's
        // timer or its one-off flag.
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

            // A load, not a fresh landing. Finish rather than just marking it done,
            // so a colony abandoned mid-scene cannot leave the UI hidden or the
            // agents held for the game that replaces it.
            if (Find.TickManager.TicksGame > FreshGameTicks) { Finish(); return; }

            UiHidden = true;
            AgentsHeld = true;
            Go(Phase.Welcome);
        }

        // The scenario put its dialog up during FinalizeInit, before the first frame,
        // so by now it is on the stack and holding the game paused. Gone means read -
        // by the OK button or by Escape, either way the scene can start.
        void WaitOnWelcome()
        {
            if (Find.WindowStack.WindowOfType<Dialog_NodeTree>() == null) BeginScene();
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

        // Animals first, then people, a few per tick. Everything spawns alive and
        // factionless: they wander, they never join the colonist bar, and they are
        // here to die of the plague rather than to be found already dead.
        void StepPopulate()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

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

            if (_animalsLeft <= 0 && _humansLeft <= 0) Go(Phase.Land);
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

        // The pods are still in the air, or still sealed; TimeKeeper is what keeps
        // the clock running through that and we only wait. Once somebody is standing
        // the cat comes down after them, and then the whole party gets a few seconds
        // to walk about before anything happens to it.
        void WaitOnLanding()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            if (!_armed)
            {
                var starters = Starters(map);
                if (starters.Count == 0) return;

                _armed = true;
                _at = Time.realtimeSinceStartup + WalkSeconds;
                Pets.Place(map, starters[0].Position);
                return;
            }

            if (!Held) Go(Phase.Core);
        }

        // Drop the core on the middle of the party and wait for it to arrive. Reuses
        // a core already on the map, so a reload mid-scene never leaves two.
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
                // It never arrived. The rest of the scene needs a core standing -
                // the plague spreads from it and the agents land beside it - so it
                // gets put there without the theatre rather than not at all.
                Log.Warning("[SlopWorld] persona core never landed; placing it");
                Ground(map);
            }

            Go(Phase.Fume, FumeSeconds);
        }

        /// <summary>
        /// The core comes down the way everything else on this map arrived: out of
        /// the sky. ShipChunkIncoming is vanilla's own carrier for wreckage falling
        /// on a colony - it holds whatever it is handed and puts it down on impact,
        /// and with no graphicData of its own the skyfaller draws its payload, so
        /// what falls is the core rather than a chunk. It is also the harmless one:
        /// the variant that blows a hole in the ground is a separate def
        /// (ShipChunkIncoming_SmallExplosion), which matters here because the cat is
        /// standing directly underneath.
        ///
        /// The camera goes with it. The scene has been following the pods, the core
        /// lands on the same spot they did, and the jump is what guarantees the
        /// player is looking at the thing when it hits - which also matters to the
        /// fumes, since flecks are not spawned off screen at all.
        /// </summary>
        void Fall(Map map)
        {
            var cell = map.Center;
            Find.CameraDriver?.JumpToCurrentMapLoc(cell);

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

        // The tell, before there is anything to tell. Nothing is marked yet and the
        // plague is not armed - this is the core alone, venting, so that what comes
        // next is read as having come out of it.
        void Vent()
        {
            if (Find.TickManager.TicksGame % PuffInterval != 0) return;
            PlagueFx.Fume(TheCore(TheMap));
        }

        void WaitOnFumes()
        {
            if (!Held) Go(Phase.Purge);
        }

        void BurnThem()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            // Everything else in the blast is scenery. The pets are the point.
            var spared = Pets.On(map).Cast<Thing>().ToList();
            foreach (var p in Starters(map)) Explode(p, map, spared);

            map.GetComponent<Plague>()?.Arm(TheCore(map)?.Position ?? map.Center);

            AgentsHeld = false; // the reconcile may put the agents on the board now
            Go(Phase.Bloom, BloomSeconds);
        }

        // The agents are spawned by AgentColony's own reconcile, on its own second,
        // so this is a beat rather than a step: long enough for them to arrive and
        // for their haze to be worth looking at before the UI covers it.
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

        // Player colonists that are not one of our agent pawns - the dead ones too.
        // A colonist's corpse holds their slot in the colonist bar until it
        // dessicates (ColonistBar.CheckRecacheEntries walks the map's corpses and
        // adds every colonist it finds inside one), so a starter that died before
        // the purge - caught by a stray blast, or by something the scene threw at it
        // - would sit up there forever if the purge only looked at the living.
        static List<Pawn> Starters(Map map)
        {
            var colony = AgentColony.Current;
            var found = map.mapPawns.FreeColonists
                .Where(p => colony == null || !colony.IsAgentPawn(p))
                .ToList();

            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                var inner = (thing as Corpse)?.InnerPawn;
                if (inner == null || !inner.IsColonist) continue;
                if (colony != null && colony.IsAgentPawn(inner)) continue;
                found.Add(inner);
            }

            return found;
        }

        static void Explode(Pawn pawn, Map map, List<Thing> spared)
        {
            // PositionHeld, not Position: one of these may already be lying inside a
            // corpse, and it should go up in the same red mist as the rest.
            var pos = pawn.PositionHeld;
            if (pos.IsValid && pos.InBounds(map))
            {
                // Lots of blood, spread well past the blast.
                int cells = GenRadial.NumCellsInRadius(PurgeBloodRadius);
                for (int i = 0; i < PurgeBloodCount; i++)
                {
                    var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                    if (c.InBounds(map))
                        FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, pawn.LabelShort, 1);
                }

                GenExplosion.DoExplosion(pos, map, PurgeBlastRadius, DamageDefOf.Bomb, pawn,
                    damAmount: PurgeBlastDamage, ignoredThings: spared);
            }

            // Make sure they leave the colonist bar regardless of what the blast
            // left behind: kill, bin the corpse, then unspawn.
            if (!pawn.Dead)
                pawn.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 999f, -1f, pawn));
            pawn.Corpse?.Destroy();
            if (pawn.Spawned) pawn.DeSpawn();
            if (!pawn.Destroyed) pawn.Destroy();

            Log.Message($"[SlopWorld] purged starting colonist '{pawn.LabelShort}'");
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

        /// <summary>
        /// The scenario's own opening dialog, with our words in it. Vanilla owns the
        /// choreography around it - the game starts paused, the dialog holds the
        /// pause, and closing it starts the clock and lets the music back in - so we
        /// swap the text rather than putting up a second dialog of our own. A
        /// ScenPart_GameStartDialog prefers its literal text over its textKey, and
        /// Crashlanded ships only the key, so setting one is enough. The field is
        /// private, hence the ref.
        /// </summary>
        [HarmonyPatch(typeof(ScenPart_GameStartDialog), nameof(ScenPart_GameStartDialog.PostGameStart))]
        public static class Patch_WelcomeText
        {
            static readonly AccessTools.FieldRef<ScenPart_GameStartDialog, string> TextOf =
                AccessTools.FieldRefAccess<ScenPart_GameStartDialog, string>("text");

            static void Prefix(ScenPart_GameStartDialog __instance) =>
                TextOf(__instance) = WelcomeText;
        }
    }
}
