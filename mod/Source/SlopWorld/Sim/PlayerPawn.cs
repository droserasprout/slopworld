using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    public class PlayerPawn : GameComponent
    {
        const int RetargetFrames = 15;
        const float MinRetargetDist = 3f;
        const float FireballCooldown = 0.5f;
        const float AutopilotIdleDelay = 10f;

        Pawn _pawn;
        IntVec3 _lastTarget;
        IntVec3 _lastCameraCenter;
        int _frame;
        float _lastAction;
        float _autopilotResumeAt;
        bool _autopilotPathing;

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

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            var map = Find.CurrentMap;
            if (map == null) return;

            EnsurePawn(map);
            if (_pawn == null || !_pawn.Spawned) return;
            if (_pawn.Map != map) return;

            var now = Time.realtimeSinceStartup;
            var centre = Find.CameraDriver.CurrentViewRect.CenterCell;
            if (_lastCameraCenter.IsValid
                && centre.InBounds(map)
                && centre.DistanceTo(_lastCameraCenter) >= MinRetargetDist)
                _autopilotResumeAt = now + AutopilotIdleDelay;
            if (centre.InBounds(map)) _lastCameraCenter = centre;

            if (_autopilotPathing && _pawn.CurJobDef != JobDefOf.Goto)
            {
                _autopilotPathing = false;
                _autopilotResumeAt = now + AutopilotIdleDelay;
            }

            if (++_frame < RetargetFrames) return;
            _frame = 0;
            if (now < _autopilotResumeAt) return;

            WalkToCenter();
        }

        public override void GameComponentOnGUI()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;
            if (_pawn == null || !_pawn.Spawned) return;
            if (Cutscene.Playing) return;
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;

            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode != KeyCode.Alpha1 && e.keyCode != KeyCode.Alpha2
                && e.keyCode != KeyCode.Alpha3) return;
            if (e.alt || e.control || e.shift || e.command) return;

            e.Use();
            _autopilotResumeAt = Time.realtimeSinceStartup + AutopilotIdleDelay;
            if (e.keyCode == KeyCode.Alpha1) CastFireball();
            else if (e.keyCode == KeyCode.Alpha2) CastWaterBall();
            else TeleportToCursor();
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

        void WalkToCenter()
        {
            var map = _pawn.Map;
            if (map == null) return;

            var center = Find.CameraDriver.CurrentViewRect.CenterCell;
            if (!center.InBounds(map)) return;
            if (_lastTarget.IsValid && center.DistanceTo(_lastTarget) < MinRetargetDist) return;
            _lastTarget = center;

            var target = Walkable(map, center);
            if (!target.IsValid) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            _pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
            _autopilotPathing = true;
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
            Cast(SlopDefOf.SlopFireball);
        }

        void CastWaterBall()
        {
            Cast(SlopDefOf.SlopWaterBall);
        }

        void TeleportToCursor()
        {
            var map = _pawn.Map;
            if (map == null || map != Find.CurrentMap) return;

            var target = UI.MouseMapPosition().ToIntVec3();
            if (!target.InBounds(map)) return;

            target = Walkable(map, target);
            if (!target.IsValid) return;

            _pawn.Position = target;
            _pawn.Notify_Teleported();
            _autopilotPathing = false;

            // The normal follow-camera job should not immediately undo the teleport.
            var centre = Find.CameraDriver.CurrentViewRect.CenterCell;
            if (centre.InBounds(map)) _lastTarget = centre;
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
