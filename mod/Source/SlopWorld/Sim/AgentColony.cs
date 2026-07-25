using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    /// <summary>
    /// Keeps the colony in step with the daemon: one colonist per session, named
    /// after it. GameComponents are constructed for every subclass automatically,
    /// so this needs no def.
    /// </summary>
    public class AgentColony : GameComponent
    {
        // Reconciling is cheap but pointless every tick; once a second is plenty.
        const int Interval = 60;

        Dictionary<string, Pawn> _pawns = new Dictionary<string, Pawn>();

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

        /// <summary>
        /// Follows a rename the player just made, keeping the same colonist. The
        /// reconcile below knows sessions only by name, so without this it would
        /// read a rename as one session gone and another arrived - retiring a
        /// perfectly good pawn and spawning a stranger in its place.
        /// </summary>
        public void Rename(string oldName, string newName)
        {
            if (!_pawns.TryGetValue(oldName, out var pawn)) return;
            _pawns.Remove(oldName);
            if (pawn == null || pawn.Destroyed) return;

            pawn.Name = new NameSingle(newName);
            _pawns[newName] = pawn;
            Log.Message($"[SlopWorld] colonist '{oldName}' is now '{newName}'");
        }

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
                else if (p.Dead)
                {
                    // Something killed it - a blast it stood too close to, most
                    // likely. Its corpse would hold the session's slot in the
                    // colonist bar and no new body would ever be spawned, so bin it
                    // and let the reconcile below hand the session a fresh one.
                    Retire(p);
                    _pawns.Remove(name);
                }
            }

            // Every session gets a colonist, running or not, so it shows in the
            // colonist bar the moment it is created. A stopped agent's colonist is
            // downed rather than removed (see Reflect), staying a live, clickable
            // pawn that its process can wake later.
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

            // Posture each colonist to its agent: a dead process collapses it
            // (Downed), an idle agent sleeps, a working or waiting one stays awake.
            foreach (var kv in _pawns)
                Reflect(kv.Value, SessionHub.Instance.Get(kv.Key)?.State ?? AgentState.Dead);
        }

        // Dead process -> the colonist collapses (Downed) but stays a live, clickable
        // pawn its process can wake. Idle -> asleep on the spot. Working or waiting ->
        // awake and upright.
        static void Reflect(Pawn pawn, AgentState state)
        {
            if (pawn == null || !pawn.Spawned) return;

            if (state == AgentState.Dead)
            {
                Down(pawn);
                return;
            }

            Revive(pawn); // process is back: clear the collapse
            if (pawn.jobs == null) return;
            if (state == AgentState.Idle) Sleep(pawn);
            else Wake(pawn);
        }

        // Collapse the colonist by capping its consciousness, unless already down.
        static void Down(Pawn pawn)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            if (health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline) == null)
                health.AddHediff(SlopDefOf.SlopOffline);
        }

        // Clear the collapse so the pawn gets back on its feet.
        static void Revive(Pawn pawn)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            var h = health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline);
            if (h != null) health.RemoveHediff(h);
        }

        // Force the pawn to lie down and sleep on the spot, unless it already is.
        static void Sleep(Pawn pawn)
        {
            var cur = pawn.CurJob;
            if (cur != null && cur.def == JobDefOf.LayDown && cur.forceSleep) return;

            var job = JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            job.forceSleep = true;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        // End only the forced sleep we started, so a woken agent stands back up.
        static void Wake(Pawn pawn)
        {
            var cur = pawn.CurJob;
            if (cur != null && cur.def == JobDefOf.LayDown && cur.forceSleep)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
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
            p.Corpse?.Destroy(); // clear the body if the agent died before removal
            if (p.Spawned) p.DeSpawn();
            if (!p.Destroyed) p.Destroy();
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
