using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Dresses a fresh colony as a massacre site: leafless "dead" trees, cleared
    /// undergrowth, the map's own wildlife slaughtered where it stood, scattered
    /// animal and human corpses, and a pool of blood around every body. Runs once
    /// per game, right after the landing, gated on a persisted flag so a reload
    /// never re-does it. Sibling of StarterPurge; GameComponents are built for every
    /// subclass automatically, so this needs no def.
    ///
    /// The work is spread across many ticks rather than done in a single burst:
    /// generating a fresh pawn for each corpse is expensive, and a few hundred in
    /// one tick froze the frame for seconds. Each tick chews through a budget of
    /// plants, kills, and spawns; the whole massacre lands within a second or two
    /// of real time with no visible hitch.
    ///
    /// Dressing happens in GameComponentTick, not GameComponentUpdate: spawning
    /// things (corpses, filth) only sticks once the map is live and ticking, the
    /// same window AgentColony spawns its colonists in. Bodies and blood placed
    /// pre-tick silently vanish.
    /// </summary>
    public class DeadScene : GameComponent
    {
        // Persisted: set once we have decided what to do for this game (dressed or
        // skipped). A reload sees this and never re-triggers.
        bool _done;

        // Runtime only: true while we are still working through the queues below.
        bool _dressing;
        Map _map;

        List<Plant> _plants;
        int _plantIdx;

        List<Pawn> _liveAnimals;
        int _animalIdx;

        List<PawnKindDef> _animalKinds;
        int _animalCorpsesLeft;
        int _humanCorpsesLeft;

        // Running tallies for the summary log.
        int _trees, _cleared, _slain, _animalCorpses, _humanCorpses;

        // Per-tick work budgets. Editing a plant or killing a live pawn is cheap;
        // generating a brand-new pawn for a corpse is not, so only a handful spawn
        // per tick to keep the frame from freezing.
        const int PlantsPerTick = 400;
        const int KillsPerTick = 200;
        const int SpawnsPerTick = 6;

        // A brand-new game is only a few ticks in; anything past this is a load of
        // an existing colony, which we must never touch.
        const int FreshGameTicks = 2000;

        public DeadScene(Game game) { }

        public static DeadScene Current => Verse.Current.Game?.GetComponent<DeadScene>();

        /// True once we have decided what to do (dressed or skipped). StarterPurge
        /// waits on this before arming its fuse.
        public bool Finished => _done;

        public override void GameComponentTick()
        {
            if (_dressing) { Step(); return; }
            if (_done) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (Find.TickManager.TicksGame > FreshGameTicks)
            {
                _done = true; // a load, not a fresh landing; leave the map alone
                return;
            }

            _done = true; // decided; a reload will never re-trigger from here
            if (!Settings.DeadColony) return;

            Begin(map);
            _dressing = true; // heavy work starts next tick
        }

        // Snapshot everything we are about to churn through. Cheap: allocates the
        // work lists but touches nothing, so the deciding tick stays light.
        void Begin(Map map)
        {
            _map = map;
            _plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)
                .OfType<Plant>().ToList();
            _liveAnimals = map.mapPawns.AllPawnsSpawned
                .Where(p => p.RaceProps != null && p.RaceProps.Animal && !p.Dead)
                .ToList();
            _animalKinds = AnimalKinds(map);
            _animalCorpsesLeft = Rand.Range(100, 200);
            _humanCorpsesLeft = Rand.Range(40, 80);
        }

        // One tick of work: plants, then the map's own wildlife, then fresh corpses.
        // Each phase runs to completion across ticks before the next begins.
        void Step()
        {
            var map = _map;

            // 1) Trees go bare; every other plant (bushes, grass, flowers) is
            //    removed, so nothing green survives.
            int budget = PlantsPerTick;
            while (_plantIdx < _plants.Count && budget-- > 0)
            {
                var t = _plants[_plantIdx++];
                if (t == null || t.Destroyed || !(t is Plant p) || p.def.plant == null) continue;
                if (p.def.plant.IsTree) { p.MakeLeafless(Plant.LeaflessCause.Cold, false); _trees++; }
                else { p.Destroy(DestroyMode.Vanish); _cleared++; }
            }
            if (_plantIdx < _plants.Count) return;

            // 2) Slaughter the wildlife the map spawned with, each in a blood pool.
            budget = KillsPerTick;
            while (_animalIdx < _liveAnimals.Count && budget-- > 0)
            {
                var a = _liveAnimals[_animalIdx++];
                if (a == null || a.Dead || !a.Spawned) continue;
                var cell = a.Position;
                try
                {
                    a.Kill(null);
                    BleedAround(cell, map, a.LabelShort);
                    _slain++;
                }
                catch (System.Exception e)
                {
                    Log.Warning($"[SlopWorld] slay animal: {e.Message}");
                }
            }
            if (_animalIdx < _liveAnimals.Count) return;

            // 3) Fresh corpses, animals first then humans, a few per tick.
            budget = SpawnsPerTick;
            while (budget-- > 0 && (_animalCorpsesLeft > 0 || _humanCorpsesLeft > 0))
            {
                if (_animalCorpsesLeft > 0)
                {
                    if (SpawnCorpse(map, _animalKinds.RandomElement())) _animalCorpses++;
                    _animalCorpsesLeft--;
                }
                else
                {
                    if (SpawnCorpse(map, PawnKindDefOf.Colonist)) _humanCorpses++;
                    _humanCorpsesLeft--;
                }
            }
            if (_animalCorpsesLeft > 0 || _humanCorpsesLeft > 0) return;

            Finish();
        }

        // Generate one pawn, drop it on a standable cell, kill it, and bleed around
        // it. Returns false (and logs) if generation or placement failed.
        bool SpawnCorpse(Map map, PawnKindDef kind)
        {
            if (kind == null) return false;
            try
            {
                if (!TryRandomStandable(map, out var cell)) return false;

                var req = new PawnGenerationRequest(kind, null,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);
                var pawn = PawnGenerator.GeneratePawn(req);
                GenSpawn.Spawn(pawn, cell, map);
                pawn.Kill(null);
                BleedAround(cell, map, pawn.LabelShort);
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] corpse gen: {e.Message}");
                return false;
            }
        }

        void Finish()
        {
            _dressing = false;
            _plants = null;
            _liveAnimals = null;
            _animalKinds = null;
            _map = null;
            Log.Message($"[SlopWorld] massacre dressing: {_trees} dead trees, " +
                        $"{_cleared} bushes cleared, {_slain} wildlife slain, " +
                        $"{_animalCorpses} animal corpses, {_humanCorpses} human corpses");
        }

        // Animals that actually belong to this biome, falling back to any animal so
        // an odd biome with no fauna still gets bodies.
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

        static void BleedAround(IntVec3 pos, Map map, string source)
        {
            int cells = GenRadial.NumCellsInRadius(2.5f);
            for (int i = 0; i < 12; i++)
            {
                var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (c.InBounds(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, source, 1);
            }
        }

        static bool TryRandomStandable(Map map, out IntVec3 cell)
        {
            var size = map.Size;
            for (int t = 0; t < 30; t++)
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
            Scribe_Values.Look(ref _done, "deadSceneDone", false);
        }
    }
}
