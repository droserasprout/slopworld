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

        // Shorten the base game's 110-tick pod delay so the agent appears on the colonist bar sooner.
        const int PodOpenDelay = 60;

        Dictionary<string, Pawn> _pawns = new Dictionary<string, Pawn>();
        readonly ColonySessionIndex _membership = new ColonySessionIndex();
        readonly List<string> _retiring = new List<string>();
        readonly List<string> _ordered = new List<string>();
        bool _orderDirty = true;

        // Index pawns for frequent IsAgent calls from patches, including BestAttackTarget and colonist-bar IsIdle checks.
        // This avoids repeated ContainsValue scans.
        readonly Dictionary<Pawn, string> _names = new Dictionary<Pawn, string>();

        // Scribe replaces _pawns during loading.
        // Rebuild the index on the next lookup because pawn references remain null until a later load phase.
        bool _reindex = true;

        // Last state each session was reconciled at. Saved with the colony so a game restart
        // does not turn an unchanged daemon state into a fresh transition.
        Dictionary<string, AgentState> _seen = new Dictionary<string, AgentState>();

        // Track agents whose pods have not opened.
        // Check every tick so the haze appears when the pawn spawns, without waiting for the next reconciliation.
        readonly List<Pawn> _landing = new List<Pawn>();

        // Whether this colony's one jukebox has been put in a pod. See Spawn.
        bool _jukeboxSent;

        readonly Game _game;

        public AgentColony(Game game) { _game = game; }

        // Cache the component to avoid repeated Game.GetComponent scans for each pawn.
        // Check the current game so the cache cannot return a discarded colony.
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
            _orderDirty = true;
            if (pawn != null) Names[pawn] = name;
        }

        void Unbind(string name)
        {
            if (_pawns.TryGetValue(name, out var had) && had != null) Names.Remove(had);
            if (_pawns.Remove(name)) _orderDirty = true;
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

        // Update the binding during a rename.
        // Otherwise, reconciliation treats the old name as a removed session and the new name as an added session.
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

        // Use the column's display order for numbered selection shortcuts.
        // The column groups portraits by project.
        public static List<string> InBarOrder()
        {
            return AgentSidebar.Sessions();
        }

        // Match the tick reconcile's cadence even while Eco holds the simulation paused.
        const float SweepSecs = 1f;
        static float _swept;

        // Eco pauses game ticks, so run the full reconcile from wall time while resting. Pod
        // arrivals are spawned directly because their open delay would otherwise never tick.
        public override void GameComponentUpdate()
        {
            if (!Eco.Resting || Cutscene.AgentsHeld) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _swept < SweepSecs) return;
            _swept = now;

            Reconcile();
        }

        // Reconcile missing sessions and missing colonists.
        // Return false when the hub has no current data.
        // A disconnected socket must not cause removal of every agent.
        bool Sweep(List<SessionInfo> sessions)
        {
            if (sessions.Count == 0 && !SessionHub.Instance.Online) return false;

            if (_membership.Refresh(sessions, SessionHub.Instance.SessionsVersion))
                PerfTrace.Count("colony-membership-rebuilds");

            // Check for dead or destroyed pawns even when the daemon snapshot has not changed.
            // Collect removals first so callbacks can change bindings safely.
            _retiring.Clear();
            foreach (var pair in _pawns)
            {
                var pawn = pair.Value;
                // A rename briefly removes one name before the HTTP callback or its pushed
                // snapshot supplies the other. Retain the binding only when that counterpart
                // is in the current membership. An ordinary deletion still retires normally.
                bool member = _membership.Contains(pair.Key) || PendingRenameKeeps(pair.Key);
                if (!member || pawn == null || pawn.Destroyed || pawn.Dead)
                    _retiring.Add(pair.Key);
            }
            foreach (var name in _retiring)
            {
                if (!_pawns.TryGetValue(name, out var pawn)) continue;
                bool removed = !_membership.Contains(name) && !PendingRenameKeeps(name);
                if (removed || (pawn != null && pawn.Dead)) Retire(pawn);
                Unbind(name);
                if (removed) _seen.Remove(name);
            }
            _retiring.Clear();

            return true;
        }

        public override void GameComponentTick()
        {
            if (_landing.Count > 0) Landed();

            if (Find.TickManager.TicksGame % Interval != 0) return;

            if (Cutscene.AgentsHeld) return;

            Reconcile();
        }

        // Reconcile colony membership with daemon sessions.
        // Use the same method for game-tick updates and wall-time updates during Eco.
        void Reconcile()
        {
            long started = PerfTrace.Start();
            try { ReconcileCore(); }
            finally { PerfTrace.End("colony-reconcile", started, _pawns.Count); }
        }

        void ReconcileCore()
        {
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var sessions = SessionHub.Instance.Sessions;
            if (!Sweep(sessions)) return;

            // Keep colonists for eligible sessions even when their processes stop.
            // Down the pawn without removing it so a restarted process can restore it.
            foreach (var s in sessions)
            {
                // Workers have child rows in AgentSidebar. Do not create colony pawns for them.
                if (s.Ephemeral || s.Worker) continue;
                if (_pawns.ContainsKey(s.Name)) continue;
                // Do not create a second pawn while a pending rename has an existing binding under the alternate name.
                if (PendingRenameCovers(s.Name)) continue;
                // RimWorld resolves saved pawn references during a later load phase and can discard unresolved entries.
                // Find an existing loaded pawn before creating one to prevent duplicates.
                var pawn = FindExisting(s.Name) ?? Spawn(s.Name, map);
                if (pawn == null) continue;
                Bind(s.Name, pawn);

                // RobotFaceRenderNodes checks IsAgent when building the render tree.
                // Loaded pawns already have render trees before this binding exists.
                // Refresh the render tree and portrait cache after binding the pawn.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // Down pawns for stopped processes and preserve activity for other states.
            foreach (var kv in _pawns)
            {
                if (AgentLook.CapBodySize(kv.Value))
                    kv.Value.Drawer?.renderer?.SetAllGraphicsDirty();

                RobotFace.Apply(kv.Value);

                string sessionName = PendingRenameSession(kv.Key);
                var state = SessionHub.Instance.Get(sessionName)?.State ?? AgentState.Down;
                // Do not treat the first observed state as a transition without saved history.
                AgentState? was = _seen.TryGetValue(kv.Key, out var seen)
                    ? seen : (AgentState?)null;
                _seen[kv.Key] = state;

                Reflect(kv.Value, state, was);
            }

            Reorder();
        }

        bool PendingRenameKeeps(string name)
        {
            var hub = SessionHub.Instance;
            return AgentRenamePolicy.KeepsBinding(name, _membership.Contains,
                hub.PendingRenameDestination, hub.PendingRenameSource);
        }

        bool PendingRenameCovers(string sessionName)
        {
            var hub = SessionHub.Instance;
            return AgentRenamePolicy.HasBindingForSession(sessionName, _pawns.ContainsKey,
                hub.PendingRenameDestination, hub.PendingRenameSource);
        }

        string PendingRenameSession(string bindingName)
        {
            var hub = SessionHub.Instance;
            return AgentRenamePolicy.ResolveSessionName(bindingName, _membership.Contains,
                hub.PendingRenameDestination, hub.PendingRenameSource);
        }

        void Reorder()
        {
            if (!_orderDirty) return;
            _ordered.Clear();
            _ordered.AddRange(_pawns.Keys);
            _ordered.Sort(CompareNames);
            for (int i = 0; i < _ordered.Count; i++)
            {
                var settings = _pawns[_ordered[i]]?.playerSettings;
                if (settings != null && settings.displayOrder != i) settings.displayOrder = i;
            }
            _orderDirty = false;
            PerfTrace.Count("colony-order-rebuilds");
        }

        internal static int CompareNames(string a, string b)
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

        // Keep a stopped agent's pawn alive so a process restart can restore the same pawn.
        // was is absent for new sessions or older saves.
        // Play transition sounds only when the state changes.
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
            if (health.hediffSet.GetFirstHediffOfDef(ModDefOf.SlopOffline) != null) return;

            health.AddHediff(ModDefOf.SlopOffline);

            if (loud) ModDefOf.LetterArrive_BadUrgent.PlayOneShotOnCamera(pawn.Map);
        }

        static void Revive(Pawn pawn)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            var h = health.hediffSet.GetFirstHediffOfDef(ModDefOf.SlopOffline);
            if (h != null) health.RemoveHediff(h);

            // Repair injuries from saves created before NoHarmAgents protected these pawns.
            if (pawn.Downed) Mend(pawn);
        }

        // Remove blood loss separately from injuries.
        // Otherwise, it can persist after wound removal and down the pawn again.
        static void Mend(Pawn pawn)
        {
            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(pawn);

            var bleeding = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bleeding != null) pawn.health.RemoveHediff(bleeding);

            // Log only a successful recovery.
            // Reconciliation retries while the pawn remains downed, so logging failures here would repeat every second.
            if (!pawn.Downed)
                Log.Message($"[SlopWorld] Colonist '{pawn.LabelShort}' patched up. Agents take no damage.");
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

        // Traverse backward to permit removal during iteration.
        // Remove pawns retired before their pods open.
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
            var pawn = ColonyPawn.Generate();
            pawn.Name = new NameSingle(name);
            RobotFace.FitHair(pawn);
            RobotFace.RandomizeHairColor(pawn);
            RobotFace.Assign(pawn);
            AgentLook.Roll(pawn);

            // Prepare arrival cargo for a pod, except when Eco requires direct placement.
            // SpawnSpot selects the landing area. DropCellFinder selects the final pod position.
            var cargo = new List<Thing> { pawn };

            // Include one jukebox in the first arrival.
            // The saved flag prevents duplicates from concurrent arrivals or loading before pods open.
            if (!_jukeboxSent && !Jukebox.On(map))
            {
                cargo.Add(ThingMaker.MakeThing(ModDefOf.SlopJukebox));
                _jukeboxSent = true;
            }

            var cell = SpawnSpot.Find(map, Anchor(map));

            // During Eco, place the pawn and cargo directly at the landing cell.
            // Paused game ticks would otherwise prevent the pod from opening and leave a duplicate sidebar row.
            if (Eco.Resting)
            {
                GenSpawn.Spawn(pawn, cell, map);
                foreach (var thing in cargo)
                    if (thing != pawn)
                        GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);

                Log.Message($"[SlopWorld] Colonist '{name}' set down. The game remains paused.");
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

        // Prefer a position beside an existing pawn.
        // Otherwise, use the core, which map generation places on open ground.
        IntVec3 Anchor(Map map)
        {
            foreach (var p in _pawns.Values)
                if (p != null && p.Spawned && p.Map == map) return p.Position;

            var core = map.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore);
            if (core.Count > 0) return core[0].Position;

            return map.Center;
        }

        // Save eye variants by binding name, alongside the pawn references.
        Dictionary<string, RobotFace.EyeColor> _savedEyeColors;

        // Supply working lists for Scribe_Collections to retain dictionary keys and values between XML loading and reference resolution.
        // The short overload cannot supply these lists for Reference values and returns an empty dictionary with an error.
        // Only Scribe uses these temporary lists.
        List<string> _pawnKeys;
        List<Pawn> _pawnBodies;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _pawns, "agentPawns",
                LookMode.Value, LookMode.Reference, ref _pawnKeys, ref _pawnBodies);
            if (_pawns == null) _pawns = new Dictionary<string, Pawn>();
            Scribe_Collections.Look(ref _seen, "agentStates", LookMode.Value, LookMode.Value);
            if (_seen == null) _seen = new Dictionary<string, AgentState>();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _savedEyeColors = new Dictionary<string, RobotFace.EyeColor>();
                foreach (var binding in _pawns)
                    if (binding.Value != null)
                        _savedEyeColors[binding.Key] = RobotFace.ColorOf(binding.Value);
            }
            Scribe_Collections.Look(ref _savedEyeColors, "agentEyeColors", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (var binding in _pawns)
                    if (_savedEyeColors != null && _savedEyeColors.TryGetValue(binding.Key, out var color))
                        RobotFace.Restore(binding.Value, color);
                    else RobotFace.Assign(binding.Value);
                _savedEyeColors = null;
            }
            Scribe_Values.Look(ref _jukeboxSent, "jukeboxSent", false);
            _reindex = true; // whatever the table is now, it is not what the index holds
            _orderDirty = true;
        }
    }
}
