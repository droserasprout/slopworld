using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Return to the menu before QuickStart generates the next map; otherwise the scenario page
    // opens over a live map and builds a second one. GoToMainMenu is a long event, so the
    // menu-first-frame flag is the seam Patch_AutoResume uses. Run from GameComponentTick so
    // pauses do not strand the scene; explosions are Things and need ticking.
    public class NextPlanet : GameComponent
    {
        // Everything below is wall clock, so the front reaches the map edge exactly as the
        // last wave's time runs out whatever speed the game is at.
        const float HoldSeconds = 1.5f;

        // Each wave takes its own share of the way out, so the front carries on from where
        // the last one left it.
        const int Waves = 3;
        const float WaveSeconds = 1.6f;

        const float LullSeconds = 0.7f;
        const float SettleSeconds = 1.6f;

        // Off the area rather than a rate per tick, or the wave thins as it widens, the outer
        // rings being where nearly all of the map is. A default map is 350-odd blasts.
        const float CellsPerBlast = 175f;

        // A dropped frame hands the next one all the ground it did not cover, and two hundred
        // explosions in one tick is a hang.
        const int BlastsPerTick = 32;

        const float BlastRadius = 12f;
        const int FlameDamage = 40;
        const int BombDamage = 120;

        // A map that only burns reads as a wildfire, which is a thing that happens to a
        // colony rather than the end of one.
        const float BombChance = 0.25f;

        enum Phase { Off, Hold, Wave, Lull, Settle }

        Phase _phase = Phase.Off;

        // _owed is fireballs earned and not yet given: a tick moves the front about half a
        // cell, a third of a blast, and rounding that off every tick drops none at all.
        Map _map;
        IntVec3 _origin;
        float _front;
        float _owed;
        float _at;
        int _wave;
        float _from;
        float _to;

        // Read by AutoSaver, which must not write out a colony on its way to the bin, and by
        // Patch_AutoResume, which would otherwise take the menu frame this passes through.
        public static bool Pending { get; private set; }

        // Read through Cutscene, and by Patch_ContainFire, which stops holding the fire in.
        public static bool Leaving { get; private set; }

        // Both are static, so a planet left behind would otherwise hand the next one a
        // hidden interface.
        public NextPlanet(Game game)
        {
            Pending = false;
            Leaving = false;
        }

        // Idempotent, because the option is drawn every frame the menu is up.
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

            // Grandma mode: the colony still goes, it is just not set on fire on the way out.
            // Straight to the teardown, so there is no scene to sit through and no burning
            // map to watch it from.
            if (Settings.GrandmaMode) { Leave(); return; }

            // Cutscene.Playing takes the map's own interface away; a window sits above all of
            // that and has to be closed by hand.
            foreach (var w in Find.WindowStack.Windows.ToList())
                if (!(w is MainTabWindow)) w.Close(false);
            Find.MainTabsRoot?.EscapeCurrentTab(false);
            Find.Selector?.ClearSelection();

            _origin = TheCore(_map)?.Position ?? _map.Center;
            _front = 0f;
            _owed = 0f;
            _wave = 0;

            // Zoom off the driver's own config rather than a number of ours - where the mouse
            // wheel would stop, so this is a view the player could have got to.
            var cam = Find.CameraDriver;
            if (cam != null)
            {
                cam.JumpToCurrentMapLoc(_origin);
                if (cam.config != null) cam.SetRootSize(cam.config.sizeRange.max);
            }

            Go(Phase.Hold, HoldSeconds);

            Log.Message("[SlopWorld] leaving this planet; burning the map on the way out");
        }

        // So no phase inherits the timer of the one before it; IntroDirector's Go likewise.
        void Go(Phase phase, float seconds)
        {
            _phase = phase;
            _at = Time.realtimeSinceStartup + seconds;
        }

        // In real time, so a window that forces a pause cannot strand the scene.
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
                    // Over when its time is, whether or not the front got where it was going.
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

        // Shares by wave count rather than area, so later waves cover more ground in the same
        // time - a front picking up speed.
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

            // The ring just taken, over what one fireball stands for; banked, then paid whole.
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
            // A circle drawn on a square map spends its last rings mostly outside it.
            if (!cell.InBounds(map)) return;

            bool bomb = Rand.Chance(BombChance);
            GenExplosion.DoExplosion(cell, map, BlastRadius,
                bomb ? DamageDefOf.Bomb : DamageDefOf.Flame, null,
                damAmount: bomb ? BombDamage : FlameDamage);
        }

        void Leave()
        {
            _phase = Phase.Off;
            // Nothing of this map is meant to reach the next one, so the site goes down rather
            // than being trusted to.
            Worksite.Wipe(_map);
            _map = null;
            // Leaving stays up: the frames between here and the teardown are still this map's,
            // so putting the interface back would flash a colony already gone.
            Log.Message("[SlopWorld] discarding the colony, landing a new one");
            GenScene.GoToMainMenu();
        }

        static Thing TheCore(Map map)
        {
            var found = map?.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            return found != null && found.Count > 0 ? found[0] : null;
        }

        // The furthest corner from wherever the core happens to be standing, which is not
        // the middle.
        static float Reach(Map map, IntVec3 from)
        {
            float x = Mathf.Max(from.x, map.Size.x - from.x);
            float z = Mathf.Max(from.z, map.Size.z - from.z);
            return Mathf.Sqrt(x * x + z * z);
        }

        // Hook the completed option list: DoMainMenuControls builds startup/options lists and
        // draws the web-link column in a second pass. Column marks the first pass; labels or
        // rects would not survive layout changes. Both passes drop the same four translated rows.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_MenuOption
        {
            static readonly string[] Dropped =
                { "Save", "LoadGame", "ReviewScenario", "QuitToMainMenu" };

            public static bool Column;

            // Written down *and* overwritten with what the last listing did. RequestedTabSize
            // is read on PreOpen rather than per frame, so a measured-only figure is wrong the
            // first time the menu opens and a written-down one is wrong for good the day
            // vanilla stops shipping one of the four.
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

        // The menu tab asks for a fixed 450x390 with no scrolling, so a row added without this
        // is drawn past the bottom edge, and with more coming out than going in vanilla's
        // height leaves a third of the box empty.
        [HarmonyPatch(typeof(MainTabWindow_Menu), "RequestedTabSize", MethodType.Getter)]
        public static class Patch_MenuSize
        {
            // ListableOption's own minHeight, plus DrawOptionListing's spacing.
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
