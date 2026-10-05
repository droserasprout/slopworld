using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Return to the menu before QuickStart generates the next map.
    // GoToMainMenu queues a long event. Pending lets the menu hook start the next colony.
    // Advance scene phases during updates. Create explosions during game ticks.
    public class NextPlanet : GameComponent
    {
        // Use real time for scene delays, independent of game speed.
        const float HoldSeconds = 1.5f;

        // Each wave extends the radius from the end of the previous wave.
        const int Waves = 3;
        const float WaveSeconds = 1.6f;

        const float LullSeconds = 0.7f;
        const float SettleSeconds = 1.6f;

        // Calculate explosion counts from area to keep density consistent as the radius increases.
        const float CellsPerBlast = 175f;

        // Limit explosions per tick to prevent long delays after a slow frame.
        const int BlastsPerTick = 32;

        const float BlastRadius = 12f;
        const int FlameDamage = 40;
        const int BombDamage = 120;

        // Add bomb explosions so the scene shows colony destruction as well as fire.
        const float BombChance = 0.25f;

        enum Phase { Off, Hold, Wave, Lull, Settle }

        Phase _phase = Phase.Off;

        // Keep ring geometry and fractional counts across waves and per-tick caps.
        Map _map;
        IntVec3 _origin;
        float _front;
        readonly BlastBacklog _blasts = new BlastBacklog();
        float _at;
        int _wave;
        float _from;
        float _to;

        // AutoSaver uses this flag to suspend saves.
        // Patch_AutoResume uses it to leave the menu available for the next colony.
        public static bool Pending { get; private set; }

        internal static void CompletePendingLanding() => Pending = false;

        // Cutscene uses this flag to hide the UI. Patch_ContainFire uses it to permit fire spread.
        public static bool Leaving { get; private set; }

        // Reset static state so the next colony does not inherit a hidden interface.
        public NextPlanet(Game game)
        {
            Pending = false;
            Leaving = false;
        }

        // Ignore repeated requests while the scene is active.
        public static void Begin()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (Settings.EcoMode) return;
            if (Leaving) return;
            Current.Game?.GetComponent<NextPlanet>()?.Start();
        }

        void Start()
        {
            _map = Find.CurrentMap;
            if (_map == null) { Leave(); return; }

            Leaving = true;
            Pending = true;

            // The code discards the colony without the fire scene when Grandma's visiting.
            if (Settings.GrandmaMode) { Leave(); return; }

            // Cutscene.Playing hides the map interface. Close windows separately.
            foreach (var w in Find.WindowStack.Windows.ToList())
                if (!(w is MainTabWindow)) w.Close(false);
            Find.MainTabsRoot?.EscapeCurrentTab(false);
            Find.Selector?.ClearSelection();

            _origin = TheCore(_map)?.Position ?? _map.Center;
            _front = 0f;
            _blasts.Clear();
            _wave = 0;

            // Use the maximum zoom distance from the camera configuration.
            var cam = Find.CameraDriver;
            if (cam != null)
            {
                cam.JumpToCurrentMapLoc(_origin);
                if (cam.config != null) cam.SetRootSize(cam.config.sizeRange.max);
            }

            Go(Phase.Hold, HoldSeconds);

            Log.Message("[SlopWorld] Leaving this planet. Burning the map on the way out.");
        }

        // Set a new deadline for each phase.
        void Go(Phase phase, float seconds)
        {
            _phase = phase;
            _at = Time.realtimeSinceStartup + seconds;
        }

        // Advance wave deadlines in real time. Blasts drain when game ticks resume.
        public override void GameComponentUpdate()
        {
            if (_phase == Phase.Off) return;
            if (Time.realtimeSinceStartup < _at) return;

            switch (_phase)
            {
                case Phase.Hold:
                    Wake();
                    break;

                case Phase.Wave:
                    // End the wave at its deadline, even if game ticks did not advance the front to its target.
                    AdvanceFront(_to);
                    if (_wave < Waves) Go(Phase.Lull, LullSeconds);
                    else Go(Phase.Settle, SettleSeconds); // the front is at the edge
                    break;

                case Phase.Lull:
                    Wake();
                    break;

                case Phase.Settle:
                    // Drain the final wave before discarding the map, even after the settle deadline.
                    if (!_blasts.Pending) Leave();
                    break;
            }
        }

        // Divide the radius equally between waves.
        // Later waves cover more area in the same time.
        void Wake()
        {
            if (_map == null || !Find.Maps.Contains(_map)) { Leave(); return; }

            _wave++;
            _from = _front;
            _to = Reach(_map, _origin) * _wave / Waves;
            Go(Phase.Wave, WaveSeconds);
        }

        public override void GameComponentTick()
        {
            if (_phase != Phase.Wave && _phase != Phase.Lull && _phase != Phase.Settle) return;
            if (_map == null || !Find.Maps.Contains(_map)) { Leave(); return; }

            if (_phase == Phase.Wave)
            {
                float done = Mathf.Clamp01(1f - (_at - Time.realtimeSinceStartup) / WaveSeconds);
                AdvanceFront(Mathf.Lerp(_from, _to, done));
            }

            for (int i = 0; i < BlastsPerTick && _blasts.TryTake(out var inner, out var outer); i++)
                Blast(_map, Mathf.Sqrt(Rand.Range(inner * inner, outer * outer)));
        }

        void AdvanceFront(float front)
        {
            if (front <= _front) return;
            _blasts.AddRing(_front, front, CellsPerBlast);
            _front = front;
        }

        void Blast(Map map, float dist)
        {
            float a = Rand.Range(0f, Mathf.PI * 2f);
            var cell = new IntVec3(_origin.x + Mathf.RoundToInt(Mathf.Cos(a) * dist), 0,
                                   _origin.z + Mathf.RoundToInt(Mathf.Sin(a) * dist));
            // Exclude explosion positions outside the rectangular map.
            if (!cell.InBounds(map)) return;

            bool bomb = Rand.Chance(BombChance);
            GenExplosion.DoExplosion(cell, map, BlastRadius,
                bomb ? DamageDefOf.Bomb : DamageDefOf.Flame, null,
                damAmount: bomb ? BombDamage : FlameDamage);
        }

        void Leave()
        {
            _phase = Phase.Off;
            // Clear worksite state before starting the next colony.
            Worksite.Wipe(_map);
            _map = null;
            // Keep Leaving true until the map closes to prevent a brief display of its interface.
            Log.Message("[SlopWorld] discarding the colony, landing a new one");
            LinuxGameWindow.WatchWindow();
            GenScene.GoToMainMenu();
        }

        static Thing TheCore(Map map)
        {
            var found = map?.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore);
            return found != null && found.Count > 0 ? found[0] : null;
        }

        // Calculate the distance to the furthest map corner from the supplied origin.
        static float Reach(Map map, IntVec3 from)
        {
            float x = Mathf.Max(from.x, map.Size.x - from.x);
            float z = Mathf.Max(from.z, map.Size.z - from.z);
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
