using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The vanilla scenario lands three starting colonists that have nothing to do
    /// with any agent. A couple of seconds after they touch down we blow them up
    /// in a shower of blood and wipe them from the colonist bar, leaving the map
    /// to the agents. Runs once per game; the "done" flag is persisted so a reload
    /// never re-triggers it. GameComponents are built for every subclass
    /// automatically, so this needs no def.
    /// </summary>
    public class StarterPurge : GameComponent
    {
        // Persisted: set once we have purged, or decided this is not a fresh game.
        bool _done;

        // Runtime only.
        bool _armed;
        float _fireAt;

        const float FuseSeconds = 10f;
        // A brand-new game is only a few ticks in; anything past this is a load of
        // an existing colony, which we must never touch.
        const int FreshGameTicks = 2000;

        public StarterPurge(Game game) { }

        // Runs every frame during play, paused or not, so the real-time fuse burns.
        public override void GameComponentUpdate()
        {
            if (_done) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (Find.TickManager.TicksGame > FreshGameTicks)
            {
                _done = true; // not a fresh landing; leave everyone alone
                return;
            }

            // Let the massacre dressing finish first. It does a heavy one-tick
            // burst that freezes the frame; if our real-time fuse were already
            // armed it would burn through the freeze and blow the starters early.
            var dead = DeadScene.Current;
            if (dead != null && !dead.Finished)
            {
                Unpause(); // keep time running so DeadScene ticks and finishes
                return;
            }

            var starters = Starters(map);
            if (starters.Count == 0)
            {
                // Pods may still be sealed; let time run so they open.
                Unpause();
                return;
            }

            if (!_armed)
            {
                _armed = true;
                _fireAt = Time.realtimeSinceStartup + FuseSeconds;
                Unpause(); // so the fuse burns and the blast actually animates
                return;
            }

            if (Time.realtimeSinceStartup < _fireAt) return;

            foreach (var p in starters) Explode(p, map);
            _done = true;
        }

        // Player colonists that are not one of our agent pawns.
        static List<Pawn> Starters(Map map)
        {
            var colony = AgentColony.Current;
            return map.mapPawns.FreeColonists
                .Where(p => colony == null || !colony.IsAgentPawn(p))
                .ToList();
        }

        static void Unpause()
        {
            if (Find.TickManager.Paused)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        static void Explode(Pawn pawn, Map map)
        {
            var pos = pawn.Position;

            // Lots of blood, spread well past the blast.
            int cells = GenRadial.NumCellsInRadius(3.5f);
            for (int i = 0; i < 40; i++)
            {
                var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (c.InBounds(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, pawn.LabelShort, 1);
            }

            GenExplosion.DoExplosion(pos, map, 2.9f, DamageDefOf.Bomb, pawn, damAmount: 80);

            // Make sure they leave the colonist bar regardless of what the blast
            // left behind: kill, bin the corpse, then unspawn.
            if (!pawn.Dead)
                pawn.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 999f, -1f, pawn));
            pawn.Corpse?.Destroy();
            if (pawn.Spawned) pawn.DeSpawn();
            if (!pawn.Destroyed) pawn.Destroy();

            Log.Message($"[SlopWorld] purged starting colonist '{pawn.LabelShort}'");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _done, "starterPurgeDone", false);
        }
    }
}
