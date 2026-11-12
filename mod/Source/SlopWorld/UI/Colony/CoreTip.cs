using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A left click on the persona core opens a context menu.
    // "Hint" shows 2-4 tip bubbles in sequence, each lasting three seconds.
    // "Advice" uses the same bubble for a Magic 8 Ball answer.
    // "Kill something" sends 5 to 10 lightning strikes at a random person, animal,
    // or tree on the map, selected independently for each strike.
    // Grandma's visiting and Eco mode hide this option.
    // "Next planet" burns the map and lands a new colony.
    public class CoreTip : MapComponent
    {
        // Both clients embed the same canonical answers; rolling needs no daemon connection.
        static readonly string[] AdviceAnswers = LoadAdviceAnswers();

        static string[] LoadAdviceAnswers()
        {
            using (
                var stream = typeof(CoreTip).Assembly.GetManifestResourceStream(
                    "SlopWorld.Magic8Ball"
                )
            )
            using (var reader = new StreamReader(stream))
                return reader
                    .ReadToEnd()
                    .Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        }

        // Real seconds between lightning strikes in the kill sequence.
        const float MinStrikeDelay = 0.2f;
        const float MaxStrikeDelay = 0.33f;

        // Pending lightning strike sequence.
        int _strikesLeft;
        float _nextStrikeAt;

        public CoreTip(Map map)
            : base(map) { }

        // Real-time, so the sequence runs even when the game is paused.
        public override void MapComponentUpdate()
        {
            // Cancel, rather than defer, destruction disabled after a sequence was armed.
            if (Settings.EcoMode || Settings.GrandmaMode)
            {
                _strikesLeft = 0;
                return;
            }
            if (_strikesLeft <= 0)
                return;
            if (Time.realtimeSinceStartup < _nextStrikeAt)
                return;

            var target = PickTarget();
            if (target == null)
            {
                _strikesLeft = 0;
                return;
            }

            map.weatherManager.eventHandler.AddEvent(
                new WeatherEvent_LightningStrike(map, target.Position)
            );
            _strikesLeft--;

            if (_strikesLeft > 0)
                _nextStrikeAt =
                    Time.realtimeSinceStartup + Rand.Range(MinStrikeDelay, MaxStrikeDelay);
        }

        public override void MapComponentOnGUI()
        {
            // Cutscenes, Eco's frame-only map, and an opaque terminal have no core to point at.
            if (Cutscene.Playing || Eco.Resting || WorkspaceWindow.Covering)
                return;

            var cell = UI.MouseCell();
            var core = map.thingGrid.ThingAt(cell, ModDefOf.Ship_ComputerCore);
            bool over = core != null;

            // LMB on the core opens the context menu. Only if no float menu is already up.
            if (
                over
                && !SessionGizmoInput.MouseOverActionGrid
                && Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && Find.WindowStack.FloatMenu == null
            )
            {
                Event.current.Use();
                Open(Event.current.mousePosition);
            }
        }

        public static void OpenMenu()
        {
            var comp = Find.CurrentMap?.GetComponent<CoreTip>();
            comp?.Open(UI.MousePositionOnUIInverted);
        }

        // Entry actions capture the opening pointer just like the map menu. The
        // callbacks must still belong to this workspace when the menu is answered.
        public static void OpenWorkspaceMenu()
        {
            var window = Find.WindowStack?.WindowOfType<WorkspaceWindow>();
            if (window == null || Current.ProgramState != ProgramState.Entry)
                return;
            var at = UI.MousePositionOnUIInverted;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Hint", () => ShowWorkspaceBubble(window, at, false)),
                new FloatMenuOption("Advice", () => ShowWorkspaceBubble(window, at, true)),
                new FloatMenuOption("Keyboard shortcuts", ShortcutHelpView.Toggle),
            };
            if (!Settings.GrandmaMode)
                options.Add(new FloatMenuOption("Kill something", null));
            options.Add(new FloatMenuOption("New looks", null));
            options.Add(new FloatMenuOption("Next planet", null));
            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        public static bool CanShowHint() =>
            Current.ProgramState == ProgramState.Entry
                ? Find.WindowStack?.WindowOfType<WorkspaceWindow>() != null
                : Current.ProgramState == ProgramState.Playing;

        // Palette requests use the current pointer; menu requests retain their opening anchor.
        public static void ShowHint()
        {
            if (Current.ProgramState == ProgramState.Entry)
            {
                ShowWorkspaceBubble(
                    Find.WindowStack?.WindowOfType<WorkspaceWindow>(),
                    UI.MousePositionOnUIInverted,
                    false
                );
                return;
            }
            if (Current.ProgramState == ProgramState.Playing)
                HintAction(UI.MousePositionOnUIInverted);
        }

        static void ShowWorkspaceBubble(WorkspaceWindow window, Vector2 at, bool advice)
        {
            if (
                window == null
                || Current.ProgramState != ProgramState.Entry
                || Find.WindowStack?.WindowOfType<WorkspaceWindow>() != window
            )
                return;
            StartBubble(
                at,
                advice,
                () =>
                    Current.ProgramState == ProgramState.Entry
                    && Find.WindowStack?.WindowOfType<WorkspaceWindow>() == window
            );
        }

        public static void KillSomething()
        {
            if (Settings.GrandmaMode || Settings.EcoMode)
                return;
            Find.CurrentMap?.GetComponent<CoreTip>()?.KillAction();
        }

        public static void NewLooks()
        {
            var colony = AgentColony.Current;
            if (colony == null)
                return;

            foreach (var kv in colony.All)
            {
                var pawn = kv.Value;
                if (pawn == null || pawn.Destroyed)
                    continue;
                AgentLook.Reroll(pawn);
            }
        }

        void Open(Vector2 at)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Hint", () => HintAction(at)),
                new FloatMenuOption("Advice", () => AdviceAction(at)),
                new FloatMenuOption("Keyboard shortcuts", ShortcutHelpView.Toggle),
            };
            // Grandma mode omits violence; Eco keeps unavailable actions discoverable.
            if (!Settings.GrandmaMode)
                options.Add(
                    new FloatMenuOption(
                        "Kill something",
                        Settings.EcoMode ? null : (System.Action)KillAction
                    )
                );
            options.Add(new FloatMenuOption("New looks", NewLookAction));
            options.Add(
                new FloatMenuOption(
                    "Next planet",
                    Settings.EcoMode ? null : (System.Action)NextPlanet.Begin
                )
            );
            options.Add(new FloatMenuOption("Quit", QuitInterceptor.Request));

            // OpenOverPane rather than a plain Add. The status bar is drawn over a terminal as well
            // as over the map, and a menu opened from it belongs above both.
            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        // Whether this map has one. The status bar asks before it draws the icon: a door onto
        // a thing that is not there is not a door.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore).Count > 0;

        static void HintAction(Vector2 at) => StartBubble(at, false);

        static void AdviceAction(Vector2 at) => StartBubble(at, true);

        static void StartBubble(Vector2 at, bool advice, System.Func<bool> alive = null) =>
            UiBubble.Show(
                advice
                    ? AdviceAnswers[Rand.Range(0, AdviceAnswers.Length)]
                    : Patch_LoadingTips.RandomTip,
                at,
                advice ? 0 : Rand.RangeInclusive(2, 4) - 1,
                () => Patch_LoadingTips.RandomTip,
                alive
            );

        // Arm 5-10 strikes, each choosing a random living thing independently.
        void KillAction()
        {
            if (Settings.GrandmaMode || Settings.EcoMode)
                return;

            var target = PickTarget();
            if (target == null)
            {
                Messages.Message(
                    "SlopWorld: nothing to kill on this map.",
                    MessageTypeDefOf.RejectInput,
                    false
                );
                return;
            }

            _strikesLeft = Rand.RangeInclusive(5, 10);
            _nextStrikeAt = Time.realtimeSinceStartup;
        }

        // Reroll the visible parts of every agent at once.
        void NewLookAction()
        {
            NewLooks();
        }

        // Pick a random spawned pawn (humanlike or animal) or tree on the map.
        Thing PickTarget()
        {
            var pool = new List<Thing>();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p.Dead || p.Downed)
                    continue;
                if (p.RaceProps.Humanlike || p.RaceProps.Animal)
                    pool.Add(p);
            }

            var plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            for (int i = 0; i < plants.Count; i++)
            {
                var t = plants[i];
                if (t.Destroyed)
                    continue;
                if (t.def.plant != null && t.def.plant.IsTree)
                    pool.Add(t);
            }

            if (pool.Count == 0)
                return null;
            return pool[Rand.RangeInclusive(0, pool.Count - 1)];
        }
    }
}
