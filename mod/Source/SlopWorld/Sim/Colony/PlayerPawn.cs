using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public class PlayerPawn : GameComponent
    {
        const float ProjectileCooldownSeconds = 0.5f;
        static readonly string PlayerName =
            System.Environment.GetEnvironmentVariable("USER") is string user && !string.IsNullOrWhiteSpace(user)
                ? user
                : System.Environment.UserName;

        Pawn _pawn;
        float _lastProjectileCast;

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

        public override void FinalizeInit()
        {
            if (!ModProfile.Ok) return;
            // Saved pawn identity survives, but its name follows the user running this game.
            if (_pawn != null) _pawn.Name = new NameSingle(PlayerName);
        }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            if (Eco.Resting) return;
            var map = Find.CurrentMap;
            if (map == null) return;

            EnsurePawn(map);
        }

        public override void GameComponentOnGUI()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            if (_pawn == null || !_pawn.Spawned) return;
            if (Eco.Bare) return;
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
            var pawn = ColonyPawn.Generate();
            pawn.Name = new NameSingle(PlayerName);

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
            if (!TryCursorTarget(out var map, out var target)) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            _pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
        }

        bool TryCursorTarget(out Map map, out IntVec3 target)
        {
            map = _pawn.Map;
            target = IntVec3.Invalid;
            if (map == null || map != Find.CurrentMap) return false;
            var cursor = UI.MouseMapPosition().ToIntVec3();
            if (!cursor.InBounds(map)) return false;
            target = Walkable(map, cursor);
            return target.IsValid;
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
            if (!TryCursorTarget(out var map, out var target)) return;

            _pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            _pawn.Position = target;
            _pawn.Notify_Teleported();
        }

        void CatWhistle()
        {
            if (!TryCursorTarget(out var map, out var target)) return;

            var cat = Pets.On(map).FirstOrDefault();
            if (cat == null) return;

            // Use the call sound from the cat's species definition.
            // The base game caller provides this sound when a tame animal is not angry.
            cat.caller?.DoCall();

            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            cat.jobs?.StartJob(job, JobCondition.InterruptForced);
        }

        void Cast(ThingDef projectileDef)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastProjectileCast < ProjectileCooldownSeconds) return;

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
            _lastProjectileCast = now;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref _pawn, "playerPawn");
        }
    }
}
