using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // One colonist per session, named after it.
    public class AgentColony : GameComponent
    {
        // Reconciling is cheap but pointless every tick.
        const int Interval = 60;

        // Shorter than vanilla's 110: the pod is theatre, and the agent is wanted on the
        // colonist bar.
        const int PodOpenDelay = 60;

        Dictionary<string, Pawn> _pawns = new Dictionary<string, Pawn>();

        // An index, not a convenience: IsAgent is asked on the way past by most of
        // Patches/ - a validator inside BestAttackTarget, the IsIdle the colonist bar
        // reads per colonist per frame - and the answer used to be ContainsValue.
        readonly Dictionary<Pawn, string> _names = new Dictionary<Pawn, string>();

        // Scribe hands back a whole new _pawns, so an index built before a load indexes
        // nothing. Rebuilt on the next question, not in ExposeData: references are not
        // resolved until a later phase and the pawns would all still be null.
        bool _reindex = true;

        // Deliberately not saved: a colony loading with its agents already stopped should
        // not greet the player with a wall of sirens.
        readonly Dictionary<string, AgentState> _seen = new Dictionary<string, AgentState>();

        // Agents still in the air. A pawn inside a pod is not spawned, so the haze it
        // arrives in cannot go up until the pod opens - and that is watched every tick
        // rather than on the reconcile's own second, because a puff a beat after the dust
        // has settled reads as a second event. Not saved either: a game put down while a
        // pod is falling loses one puff, which is cheaper than scribing a list to say so.
        readonly List<Pawn> _landing = new List<Pawn>();

        readonly Game _game;

        public AgentColony(Game game) { _game = game; }

        // Held rather than looked up: Game.GetComponent walks the components with a type
        // check each, and this is read several times a frame per pawn. Checked against the
        // live game, so a discarded colony cannot answer for the one that replaced it.
        static AgentColony _current;

        public static AgentColony Current
        {
            get
            {
                var game = Verse.Current.Game;
                if (game == null) return _current = null;
                if (_current != null && _current._game == game) return _current;
                return _current = game.GetComponent<AgentColony>();
            }
        }

        Dictionary<Pawn, string> Names
        {
            get
            {
                if (!_reindex) return _names;
                _reindex = false;
                _names.Clear();
                foreach (var kv in _pawns)
                    if (kv.Value != null) _names[kv.Value] = kv.Key;
                return _names;
            }
        }

        // The two tables move together or not at all.
        void Bind(string name, Pawn pawn)
        {
            Unbind(name);
            _pawns[name] = pawn;
            if (pawn != null) Names[pawn] = name;
        }

        void Unbind(string name)
        {
            if (_pawns.TryGetValue(name, out var had) && had != null) Names.Remove(had);
            _pawns.Remove(name);
        }

        public string SessionOf(Pawn p) =>
            p != null && Names.TryGetValue(p, out var s) ? s : null;

        public Pawn PawnOf(string session) =>
            _pawns.TryGetValue(session, out var p) ? p : null;

        public bool IsAgentPawn(Pawn p) => p != null && Names.ContainsKey(p);

        public static bool IsAgent(Pawn p)
        {
            if (p == null) return false;
            var colony = Current;
            return colony != null && colony.IsAgentPawn(p);
        }

        // The reconcile knows sessions only by name, so without this a rename reads as
        // one session gone and another arrived.
        public void Rename(string oldName, string newName)
        {
            if (!_pawns.TryGetValue(oldName, out var pawn)) return;
            Unbind(oldName);
            if (_seen.TryGetValue(oldName, out var state))
            {
                _seen.Remove(oldName);
                _seen[newName] = state; // same agent, same history, new handle
            }
            if (pawn == null || pawn.Destroyed) return;

            pawn.Name = new NameSingle(newName);
            Bind(newName, pawn);
            Log.Message($"[SlopWorld] colonist '{oldName}' is now '{newName}'");
        }

        public IEnumerable<KeyValuePair<string, Pawn>> All => _pawns;

        // The order the strip above an open pane draws them in, so the numbered switch
        // keys count the portraits the player is looking at. The bar's own list is a
        // shared scratch buffer, hence the copy.
        public static List<string> InBarOrder()
        {
            var order = new List<string>();
            var colony = Current;
            var bar = Find.ColonistBar;
            if (colony == null || bar == null) return order;

            foreach (var pawn in bar.GetColonistsInOrder())
            {
                var session = colony.SessionOf(pawn);
                if (session != null) order.Add(session);
            }
            return order;
        }

        public override void GameComponentTick()
        {
            if (_landing.Count > 0) Landed();

            if (Find.TickManager.TicksGame % Interval != 0) return;

            // The clankers coming down into the plague is the opening scene's last beat,
            // and there is nothing worth dropping onto a map that is on fire.
            if (Cutscene.AgentsHeld) return;

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
                    Unbind(name);
                    _seen.Remove(name);
                }
                else if (p == null || p.Destroyed)
                {
                    Unbind(name);
                }
                else if (p.Dead)
                {
                    // Its corpse would hold the session's slot in the colonist bar and no new body
                    // would ever be spawned.
                    Retire(p);
                    Unbind(name);
                }
            }

            // Every session gets a colonist, running or not. A stopped agent's is downed
            // rather than removed, staying a live pawn its process can wake later.
            foreach (var s in sessions)
            {
                if (_pawns.ContainsKey(s.Name)) continue;
                // The session->pawn map is saved with reference values, which RimWorld resolves
                // in a later load phase and drops when they don't round-trip - so without this
                // the reconcile spawns a duplicate next to the loaded pawn.
                var pawn = FindExisting(s.Name) ?? Spawn(s.Name, map);
                if (pawn == null) continue;
                Bind(s.Name, pawn);

                // Only now is this pawn an agent, and the faceplate hangs off that answer:
                // SlopFaceRenderNodes asks IsAgent while the render tree is built, and a loaded
                // colony builds every tree before this has run. It is the portrait cache too,
                // which the colonist bar and the terminal strip draw from.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // A stopped process collapses it; anything else is left to get on with it.
            foreach (var kv in _pawns)
            {
                RobotFace.Apply(kv.Value);

                var state = SessionHub.Instance.Get(kv.Key)?.State ?? AgentState.Down;
                // The first sight of a session is not a move: a colony loading with half its
                // agents stopped must not greet the player with a wall of sirens.
                AgentState? was = _seen.TryGetValue(kv.Key, out var seen)
                    ? seen : (AgentState?)null;
                _seen[kv.Key] = state;

                Reflect(kv.Value, state, was);
            }

            Reorder();
        }

        void Reorder()
        {
            var names = _pawns.Keys.ToList();
            names.Sort(Alphanum);
            for (int i = 0; i < names.Count; i++)
            {
                var settings = _pawns[names[i]]?.playerSettings;
                if (settings != null) settings.displayOrder = i;
            }
        }

        static int Alphanum(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;

                    var na = a.Substring(si, i - si).TrimStart('0');
                    var nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int num = string.CompareOrdinal(na, nb);
                    if (num != 0) return num;
                    continue;
                }

                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }

            if (i < a.Length) return 1;
            if (j < b.Length) return -1;
            return string.CompareOrdinal(a, b);
        }

        // The colonist goes down but stays a live pawn its process can get back up;
        // killing it would mean a corpse and a fresh stranger on every restart. The
        // daemon's word is carried by the state icon and the inspect pane instead. `was`
        // is nothing at all on the first look, and a state it *moved* into is the only
        // kind worth making a noise about.
        static void Reflect(Pawn pawn, AgentState state, AgentState? was)
        {
            if (pawn == null || !pawn.Spawned) return;

            bool moved = was.HasValue && was.Value != state;

            if (state == AgentState.Down)
            {
                Down(pawn, moved);
                return;
            }

            Revive(pawn); // process is back: clear the collapse

            if (state != AgentState.Idle) return;

            // Vanilla's own new-alert chime, which nothing here plays any more now the alerts
            // are stripped.
            if (moved) SoundDefOf.TinyBell.PlayOneShotOnCamera(pawn.Map);
        }

        // `loud` means the process stopped just now rather than having been gone all
        // along.
        static void Down(Pawn pawn, bool loud)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            if (health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline) != null) return;

            health.AddHediff(SlopDefOf.SlopOffline);

            if (loud) SlopDefOf.LetterArrive_BadUrgent.PlayOneShotOnCamera(pawn.Map);
        }

        static void Revive(Pawn pawn)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            var h = health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline);
            if (h != null) health.RemoveHediff(h);

            // A running agent lying in a heap is the board telling a lie. This is for the
            // colonists that were already chewed on when NoHarmAgents arrived.
            if (pawn.Downed) Mend(pawn);
        }

        // Blood loss is not an injury and outlives the wounds that caused it, so it goes
        // separately or the pawn faints straight back down.
        static void Mend(Pawn pawn)
        {
            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(pawn);

            var bleeding = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bleeding != null) pawn.health.RemoveHediff(bleeding);

            // Only once it worked. Something this cannot mend would otherwise say so once a
            // second forever, the reconcile coming back for as long as the pawn is down.
            if (!pawn.Downed)
                Log.Message($"[SlopWorld] colonist '{pawn.LabelShort}' patched up; agents take no damage");
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

        // The pod opened. Walked backwards so the list can be edited as it goes; a pawn
        // that was retired or destroyed mid-flight just drops out.
        void Landed()
        {
            for (int i = _landing.Count - 1; i >= 0; i--)
            {
                var p = _landing[i];
                if (p != null && !p.Destroyed && !p.Spawned) continue;

                if (p != null && p.Spawned) PlagueFx.Arrive(p);
                _landing.RemoveAt(i);
            }
        }

        // Not static: the anchor it drops against comes from the live pawn table.
        Pawn Spawn(string name, Map map)
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
            RobotFace.FitHair(pawn);

            // In a pod, always. An agent added on a Tuesday afternoon comes down the same
            // way the opening scene's did, because a colonist that was simply *there* the
            // next time you looked is the one arrival this board cannot narrate. Not
            // forbidden and not slagged: the pod is the arrival, not wreckage to clear.
            // SpawnSpot picks the ground; DropCellFinder does the last few cells itself.
            DropPodUtility.DropThingsNear(SpawnSpot.Find(map, Anchor(map)), map,
                new List<Thing> { pawn }, openDelay: PodOpenDelay,
                canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true,
                forbid: false, allowFogged: true, faction: Faction.OfPlayer);

            // Every agent arrives in the plague's haze, not just the ones the opening scene
            // lands: a clanker is what this map makes of a person. It waits on the pod.
            _landing.Add(pawn);

            Log.Message($"[SlopWorld] colonist '{name}' is on its way down");
            return pawn;
        }

        // Beside one already standing, first - agents scattered across the map are hard
        // to read on the bar. Failing that the core, which is on open ground by
        // construction.
        IntVec3 Anchor(Map map)
        {
            foreach (var p in _pawns.Values)
                if (p != null && p.Spawned && p.Map == map) return p.Position;

            var core = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            if (core.Count > 0) return core[0].Position;

            return map.Center;
        }

        // Scribe_Collections holds a dictionary's keys and values in two lists between
        // the phase that reads the XML and the phase that resolves references, and for a
        // Reference on one side it will not supply them itself - the short overload logs
        // "you need to provide working lists" and hands back an empty dictionary. These
        // two are that scratch space and nothing here reads them.
        List<string> _pawnKeys;
        List<Pawn> _pawnBodies;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _pawns, "agentPawns",
                LookMode.Value, LookMode.Reference, ref _pawnKeys, ref _pawnBodies);
            if (_pawns == null) _pawns = new Dictionary<string, Pawn>();
            _reindex = true; // whatever the table is now, it is not what the index holds
        }
    }
}
