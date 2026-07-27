using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

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

        // Last state each session was reconciled at, so a collapse can tell an
        // agent that has only just gone down from one that was already there.
        // Deliberately not saved: a load starts with nothing here, and a colony
        // full of stopped agents should not greet the player with a wall of sirens.
        readonly Dictionary<string, AgentState> _seen = new Dictionary<string, AgentState>();

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

        /// <summary>Shorthand for the patches that ask this on the way past, which run
        /// whether or not there is a game to ask.</summary>
        public static bool IsAgent(Pawn p) => p != null && Current != null && Current.IsAgentPawn(p);

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
            if (_seen.TryGetValue(oldName, out var state))
            {
                _seen.Remove(oldName);
                _seen[newName] = state; // same agent, same history, new handle
            }
            if (pawn == null || pawn.Destroyed) return;

            pawn.Name = new NameSingle(newName);
            _pawns[newName] = pawn;
            Log.Message($"[SlopWorld] colonist '{oldName}' is now '{newName}'");
        }

        public IEnumerable<KeyValuePair<string, Pawn>> All => _pawns;

        /// <summary>The sessions in colonist-bar order, which is the order the strip
        /// above an open pane draws them in - so the numbered switch keys count the
        /// portraits the player is looking at rather than a dictionary's whim. The
        /// bar's own list is a shared scratch buffer, hence the copy.</summary>
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
            if (Find.TickManager.TicksGame % Interval != 0) return;

            // The opening scene wants the board empty until it says otherwise: an
            // agent standing in the crowd is one the purge has to step around, and
            // the clankers walking out of the plague is the last beat of the scene
            // rather than something that happened before it started.
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
                    _pawns.Remove(name);
                    _seen.Remove(name);
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
                // a duplicate next to the loaded pawn.
                var pawn = FindExisting(s.Name) ?? Spawn(s.Name, map);
                if (pawn == null) continue;
                _pawns[s.Name] = pawn;

                // Only now is this pawn an agent, and the faceplate hangs off that
                // answer: SlopFaceRenderNodes asks IsAgent while the render tree is
                // being built, and a loaded colony builds every tree before this
                // reconcile has run. Without this the colony comes back with human
                // faces and stays that way, since nothing else dirties a pawn that
                // has not changed. It is the portrait cache too, which is what the
                // colonist bar and the terminal strip draw from.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // Posture each colonist to its agent: a stopped process collapses it
            // (Downed), and anything else is left to get on with it.
            foreach (var kv in _pawns)
            {
                RobotFace.Apply(kv.Value);

                var state = SessionHub.Instance.Get(kv.Key)?.State ?? AgentState.Down;
                // What it was doing when we last looked, if we have looked at all.
                // The first sight of a session is not a move: a colony loading with
                // half its agents already stopped must not greet the player with a
                // wall of sirens, nor a colony of quiet ones with a peal of bells.
                AgentState? was = _seen.TryGetValue(kv.Key, out var seen)
                    ? seen : (AgentState?)null;
                _seen[kv.Key] = state;

                Reflect(kv.Value, state, was);
            }
        }

        // Stopped process -> the colonist goes down but stays a live, clickable pawn
        // its process can get back up. Killing it instead would mean a corpse and a
        // fresh stranger on every restart. Anything else -> whatever a colonist does
        // with itself, which is the game's answer and not ours; the daemon's word is
        // carried by the state icon and the inspect pane instead.
        //
        // `was` is the state this agent was last reconciled at, or nothing at all if
        // this is the first look. A state it *moved* into is the only kind worth
        // making a noise about.
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

            // An agent going quiet is the one thing a player who has looked away
            // wants to be told, and it is a small thing rather than an alarm: this
            // is vanilla's own new-alert chime, which nothing here plays any more
            // now the alerts are stripped out.
            if (moved) SoundDefOf.TinyBell.PlayOneShotOnCamera(pawn.Map);
        }

        // Collapse the colonist by capping its consciousness, unless already down.
        // `loud` means the process stopped just now, rather than having been gone
        // all along, and the board should hear about it.
        static void Down(Pawn pawn, bool loud)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            if (health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline) != null) return;

            health.AddHediff(SlopDefOf.SlopOffline);

            if (loud) SlopDefOf.LetterArrive_BadUrgent.PlayOneShotOnCamera(pawn.Map);
        }

        // Clear the collapse so the pawn gets back on its feet.
        static void Revive(Pawn pawn)
        {
            var health = pawn.health;
            if (health?.hediffSet == null) return;
            var h = health.hediffSet.GetFirstHediffOfDef(SlopDefOf.SlopOffline);
            if (h != null) health.RemoveHediff(h);

            // Still flat with the collapse gone means something hurt it, and a running
            // agent lying in a heap is the board telling a lie. Patch_AgentsInvulnerable
            // makes that unreachable from here on; this is for the colonists that were
            // already chewed on when it arrived, and for whatever finds a way past it.
            if (pawn.Downed) Mend(pawn);
        }

        // Undo the injury. Blood loss is not an injury and outlives the wounds that
        // caused it, so it goes separately or the pawn faints straight back down.
        static void Mend(Pawn pawn)
        {
            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(pawn);

            var bleeding = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bleeding != null) pawn.health.RemoveHediff(bleeding);

            // Only once it worked. Something this cannot mend - an anaesthetic, a
            // missing organ - would otherwise say so once a second forever, because
            // the reconcile comes back and tries again for as long as the pawn is down.
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

        // Not static: the anchor it spawns against comes from the live pawn table.
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

            GenSpawn.Spawn(pawn, SpawnSpot.Find(map, Anchor(map)), map);

            // Every agent arrives in the plague's own haze, not just the ones the
            // opening scene lands: a clanker is what this map makes of a person, and
            // the one that turns up an hour later is no different.
            PlagueFx.Arrive(pawn);

            Log.Message($"[SlopWorld] colonist '{name}' joined the colony");
            return pawn;
        }

        /// <summary>Where a new agent should be looking to land. Beside one that is
        /// already standing, first: agents scattered across the map are hard to read
        /// on the bar, and whichever spot the last one found was open. Failing that
        /// the persona core, which is where the intro put everything and what the
        /// plague spreads from - so it is on open ground by construction.</summary>
        IntVec3 Anchor(Map map)
        {
            foreach (var p in _pawns.Values)
                if (p != null && p.Spawned && p.Map == map) return p.Position;

            var core = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            if (core.Count > 0) return core[0].Position;

            return map.Center;
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
