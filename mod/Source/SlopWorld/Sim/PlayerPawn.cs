using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public class PlayerPawn : GameComponent
    {
        const float FireballCooldown = 0.5f;

        Pawn _pawn;
        float _lastAction;

        readonly Game _game;

        public PlayerPawn(Game game) { _game = game; }

        static PlayerPawn _current;

        public static PlayerPawn Current
        {
            get
            {
                var game = Verse.Current.Game;
                if (game == null) return _current = null;
                if (_current != null && _current._game == game) return _current;
                return _current = game.GetComponent<PlayerPawn>();
            }
        }

        public static bool IsPlayer(Pawn p) =>
            p != null && Current?._pawn == p;

        public Pawn Pawn => _pawn;

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            var map = Find.CurrentMap;
            if (map == null) return;

            EnsurePawn(map);
        }

        public override void GameComponentOnGUI()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            if (_pawn == null || !_pawn.Spawned) return;
            if (Cutscene.Playing) return;
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;

            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.alt || e.control || e.shift || e.command) return;

            var action = ActionFor(e);
            if (action == PlayerAction.None) return;

            e.Use();
            switch (action)
            {
                case PlayerAction.Go: GoToCursor(); break;
                case PlayerAction.Fireball: CastFireball(); break;
                case PlayerAction.Rejuvenate: CastWaterBall(); break;
                case PlayerAction.Teleport: TeleportToCursor(); break;
                case PlayerAction.CatWhistle: CatWhistle(); break;
            }
        }

        void EnsurePawn(Map map)
        {
            if (_pawn != null && !_pawn.Destroyed && !_pawn.Dead) return;
            _pawn = Spawn(map);
        }

        Pawn Spawn(Map map)
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
            pawn.Name = new NameSingle("Player");

            GenSpawn.Spawn(pawn, map.Center, map);
            Log.Message("[SlopWorld] player pawn spawned");
            return pawn;
        }

        enum PlayerAction
        {
            None,
            Go,
            Fireball,
            Rejuvenate,
            Teleport,
            CatWhistle,
        }

        static PlayerAction ActionFor(Event e)
        {
            if (Bound(ModDefOf.SlopPlayerGo, e)) return PlayerAction.Go;
            if (Bound(ModDefOf.SlopPlayerFireball, e)) return PlayerAction.Fireball;
            if (Bound(ModDefOf.SlopPlayerRejuvenate, e)) return PlayerAction.Rejuvenate;
            if (Bound(ModDefOf.SlopPlayerTeleport, e)) return PlayerAction.Teleport;
            if (Bound(ModDefOf.SlopPlayerCatWhistle, e)) return PlayerAction.CatWhistle;
            return PlayerAction.None;
        }

        static bool Bound(KeyBindingDef def, Event e)
        {
            if (def == null || KeyPrefs.KeyPrefsData == null) return false;
            var data = KeyPrefs.KeyPrefsData;
            return data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.A) == e.keyCode
                || data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.B) == e.keyCode;
        }

        void GoToCursor()
        {
            var map = _pawn.Map;
            if (map == null || map != Find.CurrentMap) return;

            var target = UI.MouseMapPosition().ToIntVec3();
            if (!target.InBounds(map)) return;
            target = Walkable(map, target);
            if (!target.IsValid) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            _pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
        }

        static IntVec3 Walkable(Map map, IntVec3 near)
        {
            if (near.Standable(map)) return near;
            for (int i = 1; i < 50; i++)
            {
                var c = near + GenRadial.RadialPattern[i];
                if (c.InBounds(map) && c.Standable(map)) return c;
            }
            return IntVec3.Invalid;
        }

        void CastFireball()
        {
            Cast(ModDefOf.SlopFireball);
        }

        void CastWaterBall()
        {
            Cast(ModDefOf.SlopWaterBall);
        }

        void TeleportToCursor()
        {
            var map = _pawn.Map;
            if (map == null || map != Find.CurrentMap) return;

            var target = UI.MouseMapPosition().ToIntVec3();
            if (!target.InBounds(map)) return;

            target = Walkable(map, target);
            if (!target.IsValid) return;

            _pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            _pawn.Position = target;
            _pawn.Notify_Teleported();
        }

        void CatWhistle()
        {
            var map = _pawn.Map;
            if (map == null || map != Find.CurrentMap) return;

            var target = UI.MouseMapPosition().ToIntVec3();
            if (!target.InBounds(map)) return;

            target = Walkable(map, target);
            if (!target.IsValid) return;

            var cat = Pets.On(map).FirstOrDefault();
            if (cat == null) return;

            // Use the cat's own call rather than a generic UI sound: the species def owns the
            // sound and callers are what vanilla uses for a tame animal's non-angry call.
            cat.caller?.DoCall();

            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            cat.jobs?.StartJob(job, JobCondition.InterruptForced);
        }

        void Cast(ThingDef projectileDef)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastAction < FireballCooldown) return;
            _lastAction = now;

            var map = _pawn.Map;
            if (map == null || map != Find.CurrentMap) return;

            var mousePos = UI.MouseMapPosition();
            var targetCell = mousePos.ToIntVec3();
            if (!targetCell.InBounds(map)) return;

            var projectile = (Projectile)GenSpawn.Spawn(
                projectileDef, _pawn.Position, map);
            projectile.Launch(
                _pawn,
                _pawn.DrawPos,
                new LocalTargetInfo(targetCell),
                new LocalTargetInfo(targetCell),
                ProjectileHitFlags.All);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref _pawn, "playerPawn");
        }
    }
}
