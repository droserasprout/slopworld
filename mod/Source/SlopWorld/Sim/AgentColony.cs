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

        // An index rather than a convenience: IsAgent is asked on the way past by most of
        // Patches/ - a validator inside BestAttackTarget, the IsIdle the bar reads per
        // colonist per frame - and the answer used to be ContainsValue.
        readonly Dictionary<Pawn, string> _names = new Dictionary<Pawn, string>();

        // Scribe hands back a whole new _pawns. Rebuilt on the next question rather than in
        // ExposeData: references resolve in a later phase, so the pawns would still be null.
        bool _reindex = true;

        // Not saved: a colony loading with its agents already stopped must not greet the
        // player with a wall of sirens.
        readonly Dictionary<string, AgentState> _seen = new Dictionary<string, AgentState>();

        // Agents still in the air. A pawn inside a pod is not spawned, so the haze waits on
        // the pod opening - watched every tick rather than on the reconcile's second, a puff
        // after the dust has settled reading as a second event.
        readonly List<Pawn> _landing = new List<Pawn>();

        // Whether this colony's one jukebox has been put in a pod. See Spawn.
        bool _jukeboxSent;

        readonly Game _game;

        public AgentColony(Game game) { _game = game; }

        // Held rather than looked up: Game.GetComponent walks the components with a type check
        // each, and this is read several times a frame per pawn. Checked against the live game
        // so a discarded colony cannot answer for the one that replaced it.
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

        // The order the column draws them in, so the numbered switch keys count the portraits
        // the player is looking at. The column groups them by project and so draws them in an
        // order of its own.
        public static List<string> InBarOrder()
        {
            return AgentSidebar.Sessions();
        }

        // How often the sweep runs on a stopped clock, in seconds of wall time. The tick
        // reconcile's own second, near enough, and eco is drawing at 30 frames anyway.
        const float SweepSecs = 1f;
        static float _swept;

        // Eco pauses game ticks, so run the full reconcile from wall time while resting; pod
        // arrivals are spawned directly because their open delay would otherwise never tick.
        public override void GameComponentUpdate()
        {
            if (!Eco.Resting || Cutscene.AgentsHeld) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _swept < SweepSecs) return;
            _swept = now;

            Reconcile();
        }

        // Colonists whose session is gone, and colonists gone some other way. False means the
        // hub had nothing to say and the caller should stand down with it: a socket that has
        // dropped is not every agent in the colony leaving at once.
        bool Sweep(List<SessionInfo> sessions)
        {
            if (sessions.Count == 0 && !SessionHub.Instance.Online) return false;

            // Reconcile colony-owned sessions only; ephemeral viewer/editor/host sessions are
            // sidebar ghosts, not pawns, and are excluded from `live`.
            var live = new HashSet<string>(
                sessions.Where(s => !s.Ephemeral).Select(s => s.Name));

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
                    // Its corpse would hold the session's slot in the bar forever.
                    Retire(p);
                    Unbind(name);
                }
            }

            return true;
        }

        public override void GameComponentTick()
        {
            if (_landing.Count > 0) Landed();

            if (Find.TickManager.TicksGame % Interval != 0) return;

            if (Cutscene.AgentsHeld) return;

            Reconcile();
        }

        // The colony against the daemon's list of sessions, from the clock or - while eco
        // rests - from wall time. One body, because "which colonists are there" is the same
        // question whichever of the two asked it.
        void Reconcile()
        {
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var sessions = SessionHub.Instance.Sessions;
            if (!Sweep(sessions)) return;

            // Every session gets a colonist, running or not. A stopped agent's is downed
            // rather than removed, staying a live pawn its process can wake later.
            foreach (var s in sessions)
            {
                if (s.Ephemeral) continue;
                if (_pawns.ContainsKey(s.Name)) continue;
                // The session->pawn map is saved with reference values, which RimWorld resolves
                // in a later load phase and drops when they do not round-trip - without this
                // the reconcile spawns a duplicate beside the loaded pawn.
                var pawn = FindExisting(s.Name) ?? Spawn(s.Name, map);
                if (pawn == null) continue;
                Bind(s.Name, pawn);

                // Only now is this pawn an agent, and the faceplate hangs off that answer:
                // SlopFaceRenderNodes asks IsAgent while the render tree is built, and a loaded
                // colony builds every tree before this runs. Also the portrait cache.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // A stopped process collapses it; anything else is left to get on with it.
            foreach (var kv in _pawns)
            {
                RobotFace.Apply(kv.Value);

                var state = SessionHub.Instance.Get(kv.Key)?.State ?? AgentState.Down;
                // The first sight of a session is not a move.
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

        // The colonist goes down but stays a live pawn its process can get back up; killing it
        // would mean a corpse and a fresh stranger on every restart. `was` is nothing on the
        // first look, and only a state it *moved* into is worth a noise.
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

            // Vanilla's new-alert chime, which nothing plays now the alerts are stripped.
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

            // For colonists already chewed on when NoHarmAgents arrived.
            if (pawn.Downed) Mend(pawn);
        }

        // Blood loss is not an injury and outlives the wounds that caused it, so it goes
        // separately or the pawn faints straight back down.
        static void Mend(Pawn pawn)
        {
            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(pawn);

            var bleeding = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bleeding != null) pawn.health.RemoveHediff(bleeding);

            // Only once it worked: something this cannot mend would say so once a second
            // forever, the reconcile coming back for as long as the pawn is down.
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

        // Walked backwards so the list can be edited as it goes; a pawn retired mid-flight
        // drops out.
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
            RobotFace.Assign(pawn);
            AgentLook.Roll(pawn);

            // In a pod, always, and neither forbidden nor slagged: the pod is the arrival, not
            // wreckage to clear. SpawnSpot picks the ground; DropCellFinder does the last few
            // cells itself.
            var cargo = new List<Thing> { pawn };

            // Put one jukebox in the first arrival; the saved latch prevents multiple pods in
            // one reconcile or a reload with pods still in flight from adding duplicates.
            if (!_jukeboxSent && !Jukebox.On(map))
            {
                cargo.Add(ThingMaker.MakeThing(SlopDefOf.SlopJukebox));
                _jukeboxSent = true;
            }

            var cell = SpawnSpot.Find(map, Anchor(map));

            // While eco rests, bypass the pod so its ticked open delay cannot leave a duplicate
            // ghost in the sidebar; place the pawn and cargo at the landing cell directly.
            if (Eco.Resting)
            {
                GenSpawn.Spawn(pawn, cell, map);
                foreach (var thing in cargo)
                    if (thing != pawn)
                        GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);

                Log.Message($"[SlopWorld] colonist '{name}' set down; the clock is stopped");
                return pawn;
            }

            DropPodUtility.DropThingsNear(cell, map,
                cargo, openDelay: PodOpenDelay,
                canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true,
                forbid: false, allowFogged: true, faction: Faction.OfPlayer);

            _landing.Add(pawn);

            Log.Message($"[SlopWorld] colonist '{name}' is on its way down");
            return pawn;
        }

        // Beside one already standing; failing that the core, which is on open ground by
        // construction.
        IntVec3 Anchor(Map map)
        {
            foreach (var p in _pawns.Values)
                if (p != null && p.Spawned && p.Map == map) return p.Position;

            var core = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            if (core.Count > 0) return core[0].Position;

            return map.Center;
        }

        // Scribe_Collections holds a dictionary's keys and values in two lists between the
        // phase that reads the XML and the phase that resolves references, and will not supply
        // them itself for a Reference side: the short overload logs "you need to provide
        // working lists" and hands back an empty dictionary. Scratch space; nothing reads them.
        List<string> _pawnKeys;
        List<Pawn> _pawnBodies;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _pawns, "agentPawns",
                LookMode.Value, LookMode.Reference, ref _pawnKeys, ref _pawnBodies);
            if (_pawns == null) _pawns = new Dictionary<string, Pawn>();
            Scribe_Values.Look(ref _jukeboxSent, "jukeboxSent", false);
            _reindex = true; // whatever the table is now, it is not what the index holds
        }
    }
}
