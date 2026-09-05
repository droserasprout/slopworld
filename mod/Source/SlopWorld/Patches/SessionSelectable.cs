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

            if (info.Alive)
            {
                yield return BuildTerminalGizmo(info.State);
                yield return BuildStopGizmo();
            }
            else
            {
                yield return BuildStartGizmo();
            }

            if (info.Host) yield break;

            yield return BuildEditGizmo();
            yield return BuildLabelGizmo(info.Label);

            if (!string.IsNullOrEmpty(info.Project))
            {
                yield return BuildDuplicateGizmo(info);
            }

            yield return BuildRemoveGizmo();
        }

        Gizmo BuildTerminalGizmo(AgentState state)
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Terminal",
                defaultDesc = $"Open the terminal for '{Session}'.\nState: {state.ToString().ToLower()}",
                icon = Icons.Terminal,
                defaultIconColor = TerminalWindow.StateColor(state),
                hotKey = ModDefOf.SlopOpenTerminal,
                action = () => TerminalWindow.Open(Session),
            };
        }

        Gizmo BuildStopGizmo()
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Stop",
                defaultDesc = $"Stop '{Session}'. The colonist stays on the floor "
                            + "until the process runs again.",
                icon = Icons.Stop,
                defaultIconColor = UiWidgets.Bad,
                hotKey = ModDefOf.SlopToggleSession,
                action = () => Find.WindowStack.Add(ConfirmDialog.Create(
                    $"Stop '{Session}'? This kills the tmux session; whatever the agent "
                  + "is in the middle of goes with it.",
                    () => SessionHub.Instance.Stop(Session, UiWidgets.Fail),
                    destructive: true)),
            };
        }

        Gizmo BuildStartGizmo()
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Start",
                defaultDesc = $"Start '{Session}' and put its colonist back on its feet.",
                icon = Icons.Play,
                defaultIconColor = UiWidgets.Yes,
                hotKey = ModDefOf.SlopToggleSession,
                action = () => SessionHub.Instance.Start(Session, UiWidgets.Fail),
            };
        }

        Gizmo BuildEditGizmo()
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Edit",
                defaultDesc = $"Edit '{Session}': name, project, command, or sandbox.",
                icon = Icons.Edit,
                defaultIconColor = UiWidgets.Accent,
                hotKey = ModDefOf.SlopEditSession,
                action = () =>
                {
                    var current = SessionHub.Instance.Get(Session);
                    if (current != null) TerminalWindow.OpenOverPane(new EditSessionDialog(current));
                    else Messages.Message($"SlopWorld: no session '{Session}' to edit.",
                        MessageTypeDefOf.RejectInput, false);
                },
            };
        }

        Gizmo BuildLabelGizmo(string label)
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Label",
                defaultDesc = string.IsNullOrWhiteSpace(label)
                    ? $"Set a manual summary label for '{Session}'. This disables automatic summaries."
                    : $"Change or remove '{Session}'s manual label. Removing it re-enables automatic summaries.",
                icon = Icons.Type,
                defaultIconColor = UiWidgets.Accent,
                action = () => LabelDialog.Open(Session, label),
            };
        }

        Gizmo BuildDuplicateGizmo(SessionInfo info)
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Duplicate",
                defaultDesc = $"Duplicate '{Session}' as a new agent in {info.Project}.",
                icon = Icons.Add,
                defaultIconColor = UiWidgets.Accent,
                hotKey = ModDefOf.SlopDuplicateSession,
                action = () => TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info)),
            };
        }

        Gizmo BuildRemoveGizmo()
        {
            return new UiCommandAction(UiWidgets.Btn.Default)
            {
                defaultLabel = "Remove",
                defaultDesc = $"Remove '{Session}' and its private state.",
                icon = Icons.Cross,
                defaultIconColor = UiWidgets.Bad,
                hotKey = ModDefOf.SlopRemoveSession,
                action = () => Find.WindowStack.Add(ConfirmDialog.Create(
                    $"Remove session '{Session}'? This kills it, drops it from config.toml, and moves " +
                    "its private state to recoverable trash for 14 days.",
                    () => SessionHub.Instance.Remove(Session, UiWidgets.Fail), destructive: true)),
            };
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
        // MapUIOnGUI owns the bottom action-button pass. The rest of the map chrome already
        // follows Cutscene.Playing, so stop this pass too rather than leaving selected-session
        // gizmos visible (and clickable) over a scene.
        static bool Prefix() => !Cutscene.Playing;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SessionGizmoSelection.InjectIntoMapUI(instructions);
    }
}
