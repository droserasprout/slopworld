using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Keeps the colony in step with the daemon: one colonist per session, named
    /// after it. GameComponents are constructed for every subclass automatically,
    /// so this needs no def.
    /// </summary>
    public class AgentColony : GameComponent
    {
        Dictionary<string, Pawn> _pawns = new Dictionary<string, Pawn>();

        // Reconciling is cheap but pointless every tick; once a second is plenty.
        const int Interval = 60;

        public AgentColony(Game game) { }

        public static AgentColony Current => Verse.Current.Game?.GetComponent<AgentColony>();

        public string SessionOf(Pawn p)
        {
            foreach (var kv in _pawns)
                if (kv.Value == p) return kv.Key;
            return null;
        }

        public Pawn PawnOf(string session) =>
            _pawns.TryGetValue(session, out var p) ? p : null;

        public bool IsAgentPawn(Pawn p) => p != null && _pawns.ContainsValue(p);

        public IEnumerable<KeyValuePair<string, Pawn>> All => _pawns;

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;
            if (!Settings.SpawnPawns) return;

            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var sessions = SessionHub.Instance.Sessions;
            if (sessions.Count == 0 && !SessionHub.Instance.Online) return;

            var live = new HashSet<string>(sessions.Select(s => s.Name));

            // Session gone, or its colonist was destroyed some other way.
            foreach (var name in _pawns.Keys.ToList())
            {
                var p = _pawns[name];
                if (!live.Contains(name))
                {
                    Retire(p);
                    _pawns.Remove(name);
                }
                else if (p == null || p.Destroyed)
                {
                    _pawns.Remove(name);
                }
            }

            foreach (var s in sessions)
            {
                if (_pawns.ContainsKey(s.Name)) continue;
                // Adopt a colonist already on the map with this name before spawning
                // a new one. The session->pawn map is saved with reference values,
                // which RimWorld resolves in a later load phase and silently drops
                // when they don't round-trip; without this the reconcile would spawn
                // a duplicate next to the loaded pawn. Matching by name (each agent
                // is a NameSingle of its session) rebuilds the map instead.
                var pawn = FindExisting(s.Name) ?? Spawn(s.Name, map);
                if (pawn != null) _pawns[s.Name] = pawn;
            }
        }

        Pawn FindExisting(string name)
        {
            foreach (var p in PawnsFinder.AllMaps_FreeColonists)
            {
                if (p == null || p.Destroyed) continue;
                if (IsAgentPawn(p)) continue; // already tracked under some session
                if (p.Name is NameSingle ns && ns.Name == name) return p;
            }
            return null;
        }

        static void Retire(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            if (p.Spawned) p.DeSpawn();
            p.Destroy();
        }

        static Pawn Spawn(string name, Map map)
        {
            var req = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowAddictions: false,
                colonistRelationChanceFactor: 0f,
                allowGay: true);

            var pawn = PawnGenerator.GeneratePawn(req);
            pawn.Name = new NameSingle(name);

            var cell = CellFinder.RandomSpawnCellForPawnNear(map.Center, map);
            GenSpawn.Spawn(pawn, cell, map);

            Log.Message($"[SlopWorld] colonist '{name}' joined the colony");
            return pawn;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _pawns, "agentPawns",
                LookMode.Value, LookMode.Reference);
            if (_pawns == null) _pawns = new Dictionary<string, Pawn>();
        }
    }
}
