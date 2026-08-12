using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>Supplies session gizmos for real pawns and sidebar ghost rows.</summary>
    public class SessionSelectable : ISelectable
    {
        /// The one session the mod considers "current". Set by a sidebar row click,
        /// comma/dot, Alt+Num, opening or switching a pane, and selecting an agent's
        /// colonist on the map. Read by <see cref="SessionGizmoSelection"/> to include
        /// it in the gizmo drawer's object list.
        public static string Current
        {
            get => _current;
            set
            {
                _current = value;

                // Map selection is authoritative each frame; clear it when another session
                // becomes current, except while syncing from the map or when it already matches.
                if (Syncing) return;

                var selector = Find.Selector;
                if (selector == null || selector.SelectedObjects.Count == 0) return;

                var pawn = selector.SingleSelectedThing as Pawn;
                if (pawn != null && value != null
                    && AgentColony.Current?.SessionOf(pawn) == value) return;

                selector.ClearSelection();
            }
        }

        static string _current;

        /// True for the length of <see cref="SessionGizmoSelection.SyncFromMapSelection"/>.
        internal static bool Syncing;

        public static bool HasCurrent => Current != null
            && SessionHub.Instance.Get(Current) != null;

        static SessionSelectable _cached;

        public static SessionSelectable CurrentObject
        {
            get
            {
                if (!HasCurrent) return null;
                if (_cached == null || _cached.Session != Current)
                    _cached = new SessionSelectable(Current);
                return _cached;
            }
        }

        public readonly string Session;

        public SessionSelectable(string session)
        {
            Session = session;
        }

        public IEnumerable<Gizmo> GetGizmos()
        {
            var info = SessionHub.Instance.Get(Session);
            if (info == null) yield break;
            var state = info.State;

            if (info.Alive)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Terminal",
                    defaultDesc = $"Open the terminal for '{Session}'.\nState: {state.ToString().ToLower()}",
                    icon = Icons.Terminal,
                    defaultIconColor = TerminalWindow.StateColor(state),
                    hotKey = SlopDefOf.SlopOpenTerminal,
                    action = () => TerminalWindow.Open(Session),
                };

                yield return new Command_Action
                {
                    defaultLabel = "Stop",
                    defaultDesc = $"Stop '{Session}'. The colonist stays on the floor "
                                + "until the process runs again.",
                    icon = Icons.Stop,
                    defaultIconColor = SlopWidgets.Bad,
                    hotKey = SlopDefOf.SlopToggleSession,
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Stop '{Session}'? This kills the tmux session; whatever the agent "
                      + "is in the middle of goes with it.",
                        () => SessionHub.Instance.Stop(Session, SlopWidgets.Fail),
                        destructive: true)),
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "Start",
                    defaultDesc = $"Start '{Session}' and put its colonist back on its feet.",
                    icon = Icons.Play,
                    defaultIconColor = SlopWidgets.Yes,
                    hotKey = SlopDefOf.SlopToggleSession,
                    action = () => SessionHub.Instance.Start(Session, SlopWidgets.Fail),
                };
            }
        }

        public string GetInspectString() => "";

        public IEnumerable<InspectTabBase> GetInspectTabs() =>
            Enumerable.Empty<InspectTabBase>();
    }

    /// <summary>Appends the current session to vanilla's temporary gizmo selection list.</summary>
    public static class SessionGizmoSelection
    {
        static readonly FieldInfo ObjectsField =
            AccessTools.Field(typeof(MapGizmoUtility), "tmpObjectsList");

        public static void AppendCurrent()
        {
            SyncFromMapSelection();
            if (!SessionSelectable.HasCurrent || ObjectsField == null) return;

            var objects = ObjectsField.GetValue(null) as List<object>;
            var current = SessionSelectable.CurrentObject;
            if (current != null) objects?.Add(current);
        }

        public static void SyncFromMapSelection()
        {
            var selector = Find.Selector;
            if (selector == null) return;

            SessionSelectable.Syncing = true;
            try
            {
                var selected = selector.SingleSelectedThing;
                if (selected == null)
                {
                    // A sidebar ghost click deliberately clears the pawn selection, leaving
                    // the session selection as the only selection. A real multi-selection,
                    // however, is vanilla selection and must not inherit a stale session.
                    if (selector.SelectedObjects.Count > 0)
                        SessionSelectable.Current = null;
                    return;
                }

                var pawn = selected as Pawn;
                var session = pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
                // Selecting anything outside the agent colony hands the inspect pane back to
                // vanilla. An agent pawn is the one-way map -> session synchronization point.
                SessionSelectable.Current = session;
            }
            finally
            {
                SessionSelectable.Syncing = false;
            }
        }

        public static IEnumerable<CodeInstruction> InjectIntoMapUI(
            IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var clear = AccessTools.Method(typeof(List<object>), nameof(List<object>.Clear));
            var append = AccessTools.Method(typeof(SessionGizmoSelection), nameof(AppendCurrent));
            bool inserted = false;

            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(clear)) continue;

                // Vanilla clears the shared list before loading the selector into it. Add
                // our object after the clear, while the evaluation stack is empty; the
                // remaining vanilla instructions then AddRange the selected objects too.
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, append));
                inserted = true;
                break;
            }

            if (!inserted)
                Log.Error("[SlopWorld] MapUIOnGUI changed; session gizmos were not inserted");
            return code;
        }
    }

    /// The vanilla map call is tiny but has an important early-out: an empty selector still
    /// needs to reach the gizmo drawer for a ghost session. Injecting into that call also
    /// avoids a second DrawGizmoGridFor patch competing with GizmoGridShift's flag.
    [HarmonyPatch(typeof(MapGizmoUtility), nameof(MapGizmoUtility.MapUIOnGUI))]
    public static class Patch_MapUIOnGUI_SessionSelection
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SessionGizmoSelection.InjectIntoMapUI(instructions);
    }
}
