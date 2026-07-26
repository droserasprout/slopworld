using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Runs the opening scene of a fresh colony, once, in order: a welcome dialog
    /// over a bare map, a hillside populated with living animals and people, the
    /// machine persona core dropped in the middle, and - after a fuse long enough
    /// for the player to believe they are about to play RimWorld - the scenario's
    /// three starting colonists going up in a red mist. Then the UI comes back and
    /// the map is handed to the plague, which kills everything the scene just put
    /// there.
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

        // Real-time seconds between the starters touching down and detonating.
        const float FuseSeconds = 10f;

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

        enum Phase { Waiting, Welcome, Populate, Console, Purge, Done }

        // Persisted: how far through the scene we are.
        Phase _phase = Phase.Waiting;

        // Runtime only.
        Map _map;
        List<PawnKindDef> _animalKinds;
        int _animalsLeft, _humansLeft;
        bool _armed;
        float _fireAt;

        /// <summary>True while the scene is playing: the bottom bar and the colonist
        /// bar stay off screen so the map reads as a cutscene. Runtime only - a save
        /// loaded mid-scene comes back with the UI on rather than stuck hidden.</summary>
        public static bool UiHidden { get; private set; }

        public IntroDirector(Game game) { }

        public static IntroDirector Current => Verse.Current.Game?.GetComponent<IntroDirector>();

        Map TheMap => _map ?? (_map = Find.CurrentMap);

        // Phases that must advance while the game is paused: the dialog holds time
        // still, and the fuse burns in real time so the blast animates.
        public override void GameComponentUpdate()
        {
            switch (_phase)
            {
                case Phase.Waiting: TryBegin(); break;
                case Phase.Welcome: WaitOnWelcome(); break;
                case Phase.Purge: BurnFuse(); break;
            }
        }

        // Phases that spawn things. Pawns and buildings only stick once the map is
        // live and ticking; anything placed pre-tick silently vanishes.
        public override void GameComponentTick()
        {
            switch (_phase)
            {
                case Phase.Populate: StepPopulate(); break;
                case Phase.Console: PlaceCore(); break;
            }
        }

        void TryBegin()
        {
            var map = TheMap;
            if (map == null) return;

            if (Find.TickManager.TicksGame > FreshGameTicks)
            {
                _phase = Phase.Done; // a load, not a fresh landing
                return;
            }

            UiHidden = true;
            _phase = Phase.Welcome;
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
            if (map == null) { _phase = Phase.Done; return; }

            _animalKinds = AnimalKinds(map);
            _animalsLeft = Rand.Range(AnimalsMin, AnimalsMax);
            _humansLeft = Rand.Range(HumansMin, HumansMax);

            _phase = Phase.Populate;
        }

        // Animals first, then people, a few per tick. Everything spawns alive and
        // factionless: they wander, they never join the colonist bar, and they are
        // here to die of the plague rather than to be found already dead.
        void StepPopulate()
        {
            var map = TheMap;
            if (map == null) { _phase = Phase.Done; return; }

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

            if (_animalsLeft <= 0 && _humansLeft <= 0) _phase = Phase.Console;
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

        // Drop the core in the middle and hand its cell to the plague. Reuses a core
        // already on the map, so a reload mid-scene never leaves two.
        void PlaceCore()
        {
            var map = TheMap;
            if (map == null) { _phase = Phase.Done; return; }

            var existing = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            var core = existing.Count > 0 ? existing[0] : null;

            if (core == null)
            {
                try
                {
                    core = ThingMaker.MakeThing(SlopDefOf.Ship_ComputerCore);
                    GenSpawn.Spawn(core, map.Center, map);
                }
                catch (System.Exception e)
                {
                    Log.Warning($"[SlopWorld] persona core: {e.Message}");
                    core = null;
                }
            }

            map.GetComponent<Plague>()?.Arm(core?.Position ?? map.Center);

            _phase = Phase.Purge;
        }

        void BurnFuse()
        {
            var map = TheMap;
            if (map == null) { Finish(); return; }

            // The pods are still in the air, or still sealed. TimeKeeper is what
            // keeps the clock running through this; we only wait.
            var starters = Starters(map);
            if (starters.Count == 0) return;

            if (!_armed)
            {
                _armed = true;
                _fireAt = Time.realtimeSinceStartup + FuseSeconds;
                // The pets land here, next to the people they belong to, with a fuse
                // to spare - long enough for the player to read them as part of the
                // team before the team stops existing.
                Pets.Place(map, starters[0].Position);
                return;
            }

            if (Time.realtimeSinceStartup < _fireAt) return;

            // Everything else in the blast is scenery. The pets are the point.
            var spared = Pets.On(map).Cast<Thing>().ToList();
            foreach (var p in starters) Explode(p, map, spared);
            Finish();
        }

        void Finish()
        {
            _phase = Phase.Done;
            _map = null;
            _animalKinds = null;
            UiHidden = false;
        }

        // Player colonists that are not one of our agent pawns - the dead ones too.
        // A colonist's corpse holds their slot in the colonist bar until it
        // dessicates (ColonistBar.CheckRecacheEntries walks the map's corpses and
        // adds every colonist it finds inside one), so a starter that died before
        // the fuse - caught by a stray blast, or by something the scene threw at it
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

        // Animals that actually belong to this biome, falling back to any animal so
        // an odd biome with no fauna still gets a population.
        static List<PawnKindDef> AnimalKinds(Map map)
        {
            var local = new List<PawnKindDef>();
            var any = new List<PawnKindDef>();
            foreach (var k in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (k.RaceProps == null || !k.RaceProps.Animal) continue;
                any.Add(k);
                if (map.Biome.CommonalityOfAnimal(k) > 0f) local.Add(k);
            }
            return local.Count > 0 ? local : any;
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
