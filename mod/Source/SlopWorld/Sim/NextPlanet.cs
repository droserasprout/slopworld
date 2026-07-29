using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Leaving. This planet burns, and then the colony lands on the next one.
    //
    // The landing is vanilla's: New colony is `Find.WindowStack.Add(new
    // Page_SelectScenario())` and Patch_QuickStart turns that into a generated map.
    // All this does is get back to the menu first, because a scenario page opened
    // over a live game builds a second one underneath it. GoToMainMenu queues the
    // teardown as a long event, hence the flag: the page is opened on the menu's
    // first frame, the same seam Patch_AutoResume hooks.
    //
    // Nothing asks whether you meant it, because the nine seconds in the middle say
    // it better than a dialog. It is paced rather than continuous: the camera lands a
    // beat before anything happens, the fire comes in waves with a lull between them,
    // and the map is left burning afterwards - a cut on the final explosion says the
    // scene ran out where a hold says it finished.
    //
    // Real-time beats off GameComponentUpdate so a pause cannot strand it, and the
    // work off GameComponentTick, because an explosion is a Thing and a Thing that
    // never ticks never goes off.
    public class NextPlanet : GameComponent
    {
        // All off the wall clock rather than ticks, so the front reaches the map edge
        // exactly as the last wave's time runs out whatever speed the game is at.

        // What is being shown is the thing about to go off, so it has to be on screen
        // before it does.
        const float HoldSeconds = 1.5f;

        // Each wave takes its own share of the way out, so the front stops where the last
        // one left it rather than starting again in the middle.
        const int Waves = 3;
        const float WaveSeconds = 1.6f;

        // Long enough that the wave that just passed is over, short enough that nothing
        // reads as having gone wrong.
        const float LullSeconds = 0.7f;

        // The front has reached the edge, so there is nothing left to watch but what it
        // did. Short, because a hold that outstays what it holds on is a scene waiting
        // for the player.
        const float SettleSeconds = 1.6f;

        // The count comes off the area rather than being a rate per tick, or the wave
        // thins out as it widens - the outer rings are where nearly all of the map is. A
        // default map works out at something over three hundred and fifty of them.
        const float CellsPerBlast = 175f;

        // A dropped frame hands the next one all the ground it did not cover, and two
        // hundred explosions in one tick is a hang rather than a spectacle.
        const int BlastsPerTick = 32;

        const float BlastRadius = 12f;
        const int FlameDamage = 40;
        const int BombDamage = 120;

        // Most of it catches; some goes up. A map that only burns reads as a wildfire,
        // which is a thing that happens to a colony rather than the end of one.
        const float BombChance = 0.25f;

        // Each pause is a phase of its own rather than a flag on the burn, so the beat
        // that ends one is the same line that starts the next.
        enum Phase { Off, Hold, Wave, Lull, Settle }

        Phase _phase = Phase.Off;

        // _owed is the fireballs the ground taken has earned and not yet been given: a
        // tick moves the front about half a cell, which is a third of a blast, and
        // rounding that off every tick is a wave that never drops one at all.
        Map _map;
        IntVec3 _origin;
        float _front;
        float _owed;
        float _at;
        int _wave;
        float _from;
        float _to;

        // Read by AutoSaver, which must not write out a colony on its way to the bin, and
        // by Patch_AutoResume, which would otherwise take the menu frame this passes
        // through.
        public static bool Pending { get; private set; }

        // Read through Cutscene by everything that stands down for one, and by
        // Patch_ContainFire, which stops holding the fire in.
        public static bool Leaving { get; private set; }

        // Both flags are static, so whatever the last game was in the middle of would
        // otherwise still be in force: a planet left behind must not hand the next one a
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
            if (Leaving) return;
            Current.Game?.GetComponent<NextPlanet>()?.Start();
        }

        void Start()
        {
            _map = Find.CurrentMap;
            if (_map == null) { Leave(); return; }

            Leaving = true;
            Pending = true;

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

            // The core is where the plague came out of, so it is where this goes in. The zoom
            // is read off the driver's own config rather than being a number of ours: it is
            // where the mouse wheel would stop, so this is a view the player could have got
            // to themselves.
            var cam = Find.CameraDriver;
            if (cam != null)
            {
                cam.JumpToCurrentMapLoc(_origin);
                if (cam.config != null) cam.SetRootSize(cam.config.sizeRange.max);
            }

            Go(Phase.Hold, HoldSeconds);

            Log.Message("[SlopWorld] leaving this planet; burning the map on the way out");
        }

        // So no phase can inherit the timer of the one before it - the same rule
        // IntroDirector's Go follows.
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
                    // The wave is over when its time is, whether or not the front got where it was
                    // going: a wave that ran short has nothing left to lay down.
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

        // The share is by wave count rather than area, so the later ones cover more
        // ground in the same time - what a front picking up speed looks like.
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

            // The ring the front has just taken, over what one fireball stands for - banked,
            // then paid out whole.
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
            // Belt and braces: a new planet is a new map and nothing of this one's is
            // meant to reach it, so the site goes down before the colony is discarded
            // rather than being trusted to.
            Worksite.Wipe(_map);
            _map = null;
            // Leaving stays up: the frames between here and the teardown are still this
            // map's, so putting the interface back would be a flash of a colony already gone.
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

        // "Next planet", in the menu behind Escape and the last button in the bottom bar.
        // It used to be on the agents window, which was the wrong shelf: that list is the
        // daemon's sessions, and this touches none of them.
        //
        // The seam is the option listing rather than the menu: DoMainMenuControls builds
        // its whole list in one method with nothing to hook in the middle, and hands the
        // finished list here. The same listing draws the startup menu and the options
        // dialog, hence the two checks.
        //
        // It also draws *twice* per menu - the second call is the column of web links -
        // so a prefix that only looked at the window put the row in both. `Column` is
        // armed on the way into DoMainMenuControls and spent by the first listing to
        // arrive, where a rect width or a label would be a guess about a layout that is
        // free to move.
        //
        // The same pass drops four rows. Save and Load are answered already by AutoSaver
        // and Patch_AutoResume, and a hand-made save here is a colony restorable under
        // sessions it no longer matches. Review scenario describes SlopScenario, which
        // nobody picked. Quit to main menu is a road with nothing at the end:
        // Patch_AutoResume would meet you there and put you straight back in. Matched on
        // the translated label, which is what the option carries, so it holds in any
        // language.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_MenuOption
        {
            static readonly string[] Dropped =
                { "Save", "LoadGame", "ReviewScenario", "QuitToMainMenu" };

            public static bool Column;

            // Written down as the answer we expect and overwritten with what the last listing
            // actually did, which is both halves of it: RequestedTabSize is read on PreOpen
            // rather than per frame, so a measured-only figure is wrong the first time the
            // menu is opened and a written-down one is wrong for good the day vanilla stops
            // shipping one of the three.
            public static int Net = 1 - Dropped.Length;

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

                // First, because leaving is what this menu is for here.
                optList.Insert(0, new ListableOption("Next planet", Begin));
                Net = 1 - gone;
            }
        }

        // Its own prefix rather than a counter reset inside the listing patch, because
        // "how many listings have gone by" is only worth asking from the call that draws
        // them.
        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
        public static class Patch_MenuFirstColumn
        {
            static void Prefix() => Patch_MenuOption.Column = true;
        }

        // The menu tab asks for a fixed 450x390 with no scrolling, so a row added without
        // this is drawn past the bottom edge - and with three more coming out than going
        // in, vanilla's height leaves a third of the box empty.
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
