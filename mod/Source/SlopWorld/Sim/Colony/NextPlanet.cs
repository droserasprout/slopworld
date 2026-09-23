using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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

        // Accumulate fractional explosion counts in _owed.
        // Rounding each tick separately can discard small increases and prevent explosions.
        Map _map;
        IntVec3 _origin;
        float _front;
        float _owed;
        float _at;
        int _wave;
        float _from;
        float _to;

        // AutoSaver uses this flag to suspend saves.
        // Patch_AutoResume uses it to leave the menu available for the next colony.
        public static bool Pending { get; private set; }

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
            _owed = 0f;
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

        // Advance phases in real time so a window that pauses the game cannot stop the scene.
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
                    _front = _to;
                    if (_wave < Waves) Go(Phase.Lull, LullSeconds);
                    else Go(Phase.Settle, SettleSeconds); // the front is at the edge
                    break;

                case Phase.Lull:
                    Wake();
                    break;

                case Phase.Settle:
                    Leave();
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
            _owed = 0f; // a fraction of a blast banked across a lull is not owed
            Go(Phase.Wave, WaveSeconds);
        }

        public override void GameComponentTick()
        {
            if (_phase != Phase.Wave) return;
            Step();
        }

        void Step()
        {
            if (_map == null || !Find.Maps.Contains(_map)) { Leave(); return; }

            float done = Mathf.Clamp01(1f - (_at - Time.realtimeSinceStartup) / WaveSeconds);
            float front = Mathf.Lerp(_from, _to, done);
            if (front <= _front) return;

            // Convert the new ring area to an explosion count. Retain fractional counts for later ticks.
            _owed += Mathf.PI * (front * front - _front * _front) / CellsPerBlast;
            int n = Mathf.Min(Mathf.FloorToInt(_owed), BlastsPerTick);
            _owed -= n;

            for (int i = 0; i < n; i++) Blast(_map, Rand.Range(_front, front));

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

        // Modify the completed option list. DoMainMenuControls draws options and web links in separate passes.
        // Column identifies the first pass without depending on labels or layout. Remove the four translated options from that pass.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_MenuOption
        {
            static readonly string[] Dropped =
                { "Save", "LoadGame", "ReviewScenario", "QuitToMainMenu" };

            public static bool Column;

            // Use the expected row count before the first menu display.
            // Update it after drawing the option list. The menu reads RequestedTabSize when it opens, not each frame.
            public static int Net = 0 - Dropped.Length;

            static void Prefix(List<ListableOption> optList)
            {
                bool first = Column;
                Column = false;

                if (!first) return; // the web links, drawn beside the options
                if (Current.ProgramState != ProgramState.Playing) return;
                if (Leaving) return;
                if (!(Find.WindowStack?.currentlyDrawnWindow is MainTabWindow_Menu)) return;

                int gone = optList.RemoveAll(o => o != null && Dropped.Any(
                    key => o.label == (string)key.Translate()));

                Net = 0 - gone;
            }
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
        public static class Patch_MenuFirstColumn
        {
            static void Prefix() => Patch_MenuOption.Column = true;
        }

        // Adjust the fixed menu height for removed rows. The menu has no scrolling.
        [HarmonyPatch(typeof(MainTabWindow_Menu), "RequestedTabSize", MethodType.Getter)]
        public static class Patch_MenuSize
        {
            // Use the minimum option height plus the space between options.
            const float RowH = 45f + 7f;

            static void Postfix(ref Vector2 __result) => __result.y += RowH * Patch_MenuOption.Net;
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
        public static class Patch_LandAgain
        {
            static void Prefix()
            {
                if (!Pending) return;
                Pending = false;
                Find.WindowStack.Add(new Page_SelectScenario());
            }
        }
    }
}
