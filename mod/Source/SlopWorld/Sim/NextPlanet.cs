using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Leaving. This planet is finished with, so it burns, and then the colony
    /// lands on the next one.
    ///
    /// The landing itself is not ours - vanilla's own New colony button is exactly
    /// `Find.WindowStack.Add(new Page_SelectScenario())`, and `Patch_QuickStart`
    /// turns that into a generated map without asking five pages of questions. All
    /// this has to do is get back to the menu first, because a scenario page opened
    /// over a live game would build a second one underneath it.
    ///
    /// The gap between the two is why there is a flag rather than two statements:
    /// `GenScene.GoToMainMenu` queues the teardown as a long event, so the page has
    /// to be opened on the far side of it. The menu's first frame is where that is,
    /// for the same reason <see cref="Patch_AutoResume"/> hooks it - the moment the
    /// old game is gone and nothing has replaced it yet.
    ///
    /// Nothing asks whether you meant it. A confirmation box is what you write when
    /// the button is one word and the consequence is a paragraph, and the nine
    /// seconds in the middle of this say the paragraph better: the interface goes,
    /// the camera drops onto the core and pulls all the way out, and a front of
    /// fire walks out of it to the map edge. Anybody who did not mean to press it watches their colony burn,
    /// which lands harder than a dialog and costs the person who did mean it
    /// nothing but the time it takes to watch. What is lost is a map - the sessions
    /// are the daemon's, and every one still running gets a fresh colonist on the
    /// next planet.
    ///
    /// It is paced rather than continuous, and every pause in it is load-bearing.
    /// The interface goes and the camera lands a beat before anything happens, or
    /// the first blast is over before the player has been shown what they are
    /// looking at. The fire then comes in waves with a lull between them, because
    /// one smooth expanding ring is a process where three of them are a shelling:
    /// a wave has an end, and the quiet after it is what makes the next one an
    /// event rather than more of the same. And the last wave is not the last beat
    /// - the map is left burning, with nothing new landing on it, for long enough
    /// to be read. A cut on the final explosion says the scene ran out; a hold
    /// says it finished.
    ///
    /// It is the mirror of <see cref="IntroDirector"/> and is written the same way:
    /// real-time beats off <c>GameComponentUpdate</c>, so a pause cannot leave the
    /// scene half run, and the work itself off <c>GameComponentTick</c>, because an
    /// explosion is a Thing and a Thing that never ticks never goes off. Nothing
    /// here is persisted - the colony is not being saved again, see
    /// <see cref="Pending"/> - so there is no load that could come back into the
    /// middle of it.
    ///
    /// GameComponents are built for every subclass automatically, so this needs no
    /// def.
    /// </summary>
    public class NextPlanet : GameComponent
    {
        // The beats, all of them off the wall clock rather than off ticks, so the
        // front reaches the map edge exactly as the last wave's time runs out
        // whatever speed the game happens to be running at.

        // The interface is gone and the camera has landed on the core, and for this
        // long nothing else happens. What is being shown is the thing about to go
        // off, so it has to be on screen before it does.
        const float HoldSeconds = 1.5f;

        // The fire, in this many goes. Each wave takes its own share of the way out
        // to the map edge, so the front stops where the last one left it and the
        // next picks it up from there rather than starting again in the middle.
        const int Waves = 3;
        const float WaveSeconds = 1.6f;

        // Between them. Long enough that the wave that just passed is over - the
        // point of dealing the fire out in goes at all - and short enough that
        // nothing reads as having gone wrong.
        const float LullSeconds = 0.7f;

        // The map burns and nothing new lands on it. The scene's last beat, and the
        // one that makes it a scene: the front has reached the edge, so there is
        // nothing left to watch but what it did. Short, because what it is holding
        // on is already finished - a hold that outstays the thing it is holding on
        // is a scene waiting for the player rather than the other way round.
        const float SettleSeconds = 1.6f;

        // One fireball per this much ground the front has just taken. The count
        // comes off the area rather than being a rate per tick, or the wave thins
        // out as it widens - the outer rings are where nearly all of the map is.
        // A default map works out at something over three hundred and fifty of
        // them, which with the radius below is a front that leaves nothing behind
        // it rather than a scattering of craters the eye can count.
        const float CellsPerBlast = 175f;

        // A ceiling per tick, because a dropped frame hands the next one all the
        // ground it did not cover, and two hundred explosions in one tick is a hang
        // rather than a spectacle. A wave is a hundred ticks or so and owes about
        // one a tick, so this is headroom and not a rate.
        const int BlastsPerTick = 32;

        const float BlastRadius = 12f;
        const int FlameDamage = 40;
        const int BombDamage = 120;

        // Most of it catches; some of it goes up. A map that only burns reads as a
        // wildfire, which is a thing that happens to a colony rather than the end
        // of one.
        const float BombChance = 0.25f;

        // Hold, then Wave and Lull alternating until the waves are spent, then
        // Settle. Only Wave lays fire down; the other three are the pauses, and
        // each of them is one phase rather than a flag on the burn, so the beat
        // that ends one is the same line that starts the next.
        enum Phase { Off, Hold, Wave, Lull, Settle }

        Phase _phase = Phase.Off;

        // Where the fire starts, how far it has got, and when this phase ends.
        // _owed is the fireballs the ground taken so far has earned and not yet
        // been given: a tick moves the front about half a cell, which is a third of
        // a blast on a map this size, and rounding that off every tick is a wave
        // that never drops one at all. _from and _to are the current wave's share
        // of the reach, so the pacing inside a wave knows nothing about the others.
        Map _map;
        IntVec3 _origin;
        float _front;
        float _owed;
        float _at;
        int _wave;
        float _from;
        float _to;

        /// <summary>Set the moment the scene starts and cleared on the menu's own
        /// frame. Two things read it: <see cref="AutoSaver"/>, which must not write
        /// out a colony on its way to the bin, and <see cref="Patch_AutoResume"/>,
        /// which would otherwise take the menu frame this is passing through.</summary>
        public static bool Pending { get; private set; }

        /// <summary>True while the closing scene plays. Read through
        /// <see cref="Cutscene"/> by everything that stands down for one, and by
        /// <see cref="Plague.Patch_ContainFire"/>, which stops holding the fire in.
        /// Runtime only, and cleared by the constructor below.</summary>
        public static bool Leaving { get; private set; }

        // A new Game - a load, or the colony this scene went to fetch. Both flags
        // are static, so whatever the last game was in the middle of would
        // otherwise still be in force: a planet left behind must not hand the next
        // one a hidden interface.
        public NextPlanet(Game game)
        {
            Pending = false;
            Leaving = false;
        }

        /// <summary>The button, and the whole of it. Idempotent, because the option
        /// is drawn on every frame the menu is up and a second press during the burn
        /// is a player who has already been answered.</summary>
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

            // Everything the player could still be looking at, the menu it was
            // pressed in included. Cutscene.Playing takes the map's own interface
            // away; a window sits above all of that and has to be closed by hand.
            foreach (var w in Find.WindowStack.Windows.ToList())
                if (!(w is MainTabWindow)) w.Close(false);
            Find.MainTabsRoot?.EscapeCurrentTab(false);
            Find.Selector?.ClearSelection();

            _origin = TheCore(_map)?.Position ?? _map.Center;
            _front = 0f;
            _owed = 0f;
            _wave = 0;

            // The core is where the plague came out of, so it is where this goes
            // in - and the camera goes out to the map's own limit with it, because
            // what is being shown is a whole planet written off and working zoom
            // frames three shacks and a fire. The size is read off the driver's own
            // config rather than being a number of ours: it is where the mouse
            // wheel would stop, so this is a view the player could have got to
            // themselves, and a map config with a different range is honoured
            // rather than overridden.
            var cam = Find.CameraDriver;
            if (cam != null)
            {
                cam.JumpToCurrentMapLoc(_origin);
                if (cam.config != null) cam.SetRootSize(cam.config.sizeRange.max);
            }

            Go(Phase.Hold, HoldSeconds);

            Log.Message("[SlopWorld] leaving this planet; burning the map on the way out");
        }

        // Every move between phases goes through here, so no phase can inherit the
        // timer of the one before it - the same rule IntroDirector's own Go follows.
        void Go(Phase phase, float seconds)
        {
            _phase = phase;
            _at = Time.realtimeSinceStartup + seconds;
        }

        // The beats, in real time, so a window that forces a pause - or a clock
        // TimeKeeper has yet to get going again - cannot strand the scene.
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
                    // The wave is over when its time is, whether or not the front
                    // got where it was going: the fire is paced off this clock and
                    // a wave that ran short has simply nothing left to lay down.
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

        // The next wave, from wherever the last one stopped out to its own share of
        // the reach. The share is by wave count rather than by area, so the later
        // ones cover more ground in the same time - which is what a front picking
        // up speed as it goes looks like, and it earns its fireballs off the area
        // either way.
        void Wake()
        {
            if (_map == null || !Find.Maps.Contains(_map)) { Leave(); return; }

            _wave++;
            _from = _front;
            _to = Reach(_map, _origin) * _wave / Waves;
            _owed = 0f; // a fraction of a blast banked across a lull is not owed
            Go(Phase.Wave, WaveSeconds);
        }

        // The fire, on the game's own clock, for the reason in the class comment.
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

            // The ring the front has just taken, in cells, over what one fireball
            // stands for - banked, then paid out whole.
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
            // A circle drawn on a square map spends its last rings mostly outside
            // it, and a blast off the map is not one worth clamping back on.
            if (!cell.InBounds(map)) return;

            bool bomb = Rand.Chance(BombChance);
            GenExplosion.DoExplosion(cell, map, BlastRadius,
                bomb ? DamageDefOf.Bomb : DamageDefOf.Flame, null,
                damAmount: bomb ? BombDamage : FlameDamage);
        }

        void Leave()
        {
            _phase = Phase.Off;
            _map = null;
            // Leaving stays up. The teardown is a long event and the frames between
            // here and it are still this map's, so putting the interface back for
            // them would be a flash of a colony that is already gone; the next
            // Game's constructor is what clears it.
            Log.Message("[SlopWorld] discarding the colony, landing a new one");
            GenScene.GoToMainMenu();
        }

        static Thing TheCore(Map map)
        {
            var found = map?.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
            return found != null && found.Count > 0 ? found[0] : null;
        }

        // How far the front has to go to have covered the map: the furthest corner
        // from wherever the core happens to be standing, which is not the middle.
        static float Reach(Map map, IntVec3 from)
        {
            float x = Mathf.Max(from.x, map.Size.x - from.x);
            float z = Mathf.Max(from.z, map.Size.z - from.z);
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>
        /// "Next planet", in the menu behind Escape and the last button in the
        /// bottom bar - the one place in this game already about ending what you
        /// are in rather than doing anything inside it. It used to be a button on
        /// the agents window, which was the wrong shelf twice over: that list is
        /// the daemon's sessions, and this touches none of them.
        ///
        /// The seam is the option listing rather than the menu itself.
        /// `MainMenuDrawer.DoMainMenuControls` builds its List&lt;ListableOption&gt;
        /// in one method with nothing to hook in the middle of it, and hands the
        /// finished list here - so inserting into it is one prefix, and the row is
        /// drawn by vanilla in vanilla's own style. That same listing draws the
        /// startup menu and the options dialog, hence the two checks: in a game,
        /// and in the window that is the in-game menu.
        ///
        /// It also draws *twice* per menu, which is the whole of why there is a
        /// flag here. The second call is the column of web links - the fiction
        /// primer, the blog, the subreddit - built into a list of its own and drawn
        /// beside the first, in the in-game menu as well as at startup. A prefix
        /// that only looked at the window put the row in both of them, so the menu
        /// had two Next planets that did the same thing. `Column` is armed by
        /// <see cref="Patch_MenuFirstColumn"/> on the way into DoMainMenuControls
        /// and spent by the first listing to arrive, which is the options one -
        /// exact, where a rect width or a label would be a guess about a layout
        /// that is free to move.
        ///
        /// The same pass drops four rows. Save and Load game are answered already
        /// and better: <see cref="AutoSaver"/> writes on the clock and on the way
        /// out, <see cref="Patch_AutoResume"/> loads the newest save on launch, and
        /// a player who saves by hand here is one who can restore a colony from
        /// under the sessions it no longer matches. Review scenario describes
        /// <see cref="SlopScenario"/>, which nobody picked and nobody can change.
        /// Quit to main menu is a road with nothing at the end of it: the menu is
        /// somewhere this product passes through rather than somewhere it stops,
        /// and `Patch_AutoResume` is what would meet you there and put you straight
        /// back in. Quitting the process still works, and so does leaving this
        /// planet for the next one, which is the door that was actually wanted.
        /// They are matched on the translated label, because that is what the
        /// option carries - a key that has no translation comes back as itself, so
        /// this holds in any language including a missing one.
        /// </summary>
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_MenuOption
        {
            static readonly string[] Dropped =
                { "Save", "LoadGame", "ReviewScenario", "QuitToMainMenu" };

            /// <summary>Set on the way into DoMainMenuControls, spent by the first
            /// listing it draws.</summary>
            public static bool Column;

            /// <summary>How many rows this build adds to the in-game menu, net: one
            /// put in against three taken out. Written down as the answer we expect
            /// and then overwritten with what the last listing actually did, which
            /// is both halves of getting the window's height right.
            /// `RequestedTabSize` is read on PreOpen rather than per frame, so a
            /// figure only ever measured would leave the menu the wrong height the
            /// first time it is opened; a figure only ever written down would be
            /// wrong for good the day vanilla stops shipping one of the three.</summary>
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

                // First, because leaving is what this menu is for here: what is
                // left of it quits a colony sim that is not running.
                optList.Insert(0, new ListableOption("Next planet", Begin));
                Net = 1 - gone;
            }
        }

        /// <summary>Arms the flag above. Its own prefix rather than a counter reset
        /// inside the listing patch, because "how many listings have gone by" is
        /// only a question worth asking from the call that draws them.</summary>
        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
        public static class Patch_MenuFirstColumn
        {
            static void Prefix() => Patch_MenuOption.Column = true;
        }

        /// <summary>The box, sized to what is in it. The menu tab asks for a fixed
        /// 450x390 and the listing is drawn inside it with no scrolling, so a row
        /// added without this is one drawn past the bottom edge - and now that three
        /// more come out than go in, a box left at vanilla's height is a third of it
        /// standing empty.</summary>
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
