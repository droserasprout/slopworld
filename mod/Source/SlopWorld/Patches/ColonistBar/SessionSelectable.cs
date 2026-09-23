using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>Supply session actions for pawns and sidebar sessions without pawns.</summary>
    public class SessionSelectable : ISelectable
    {
        /// The current session from sidebar selection, keyboard navigation, terminal panes, or map selection.
        /// <see cref="SessionGizmoSelection"/> adds this session to the action drawing list.
        public static string Current
        {
            get => _current;
            set
            {
                _current = value;
                AgentSidebar.RememberAgent(value);

                // Clear map selection when another session becomes current.
                // Retain it during map synchronization or when it already matches the session.
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

        /// True while <see cref="SessionGizmoSelection.SyncFromMapSelection"/> runs.
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

            if (info.Host)
            {
                if (info.Alive)
                    yield return BuildLabelGizmo(info.Label);
                yield break;
            }

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
            return new UiCommandAction(UiTheme.Btn.Default)
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
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Stop",
                defaultDesc = $"Stop '{Session}'. The colonist remains downed "
                            + "until the process starts again.",
                icon = Icons.Stop,
                defaultIconColor = UiTheme.Bad,
                hotKey = ModDefOf.SlopToggleSession,
                action = () => Find.WindowStack.Add(ConfirmDialog.Create(
                    $"Stop '{Session}'? This terminates the tmux session and interrupts the agent's current work.",
                    () => SessionHub.Instance.SessionStore.Stop(Session, UiLayout.Fail),
                    destructive: true)),
            };
        }

        Gizmo BuildStartGizmo()
        {
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Start",
                defaultDesc = $"Start '{Session}'. Its colonist becomes active again.",
                icon = Icons.Play,
                defaultIconColor = UiTheme.Yes,
                hotKey = ModDefOf.SlopToggleSession,
                action = () => SessionHub.Instance.SessionStore.Start(Session, UiLayout.Fail),
            };
        }

        Gizmo BuildEditGizmo()
        {
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Edit",
                defaultDesc = $"Edit '{Session}': name, project, command, or sandbox.",
                icon = Icons.Edit,
                defaultIconColor = UiTheme.Accent,
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
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Label",
                defaultDesc = string.IsNullOrWhiteSpace(label)
                    ? $"Set a fixed label for '{Session}'. Leave it blank to use the generated title."
                    : $"Change or remove '{Session}'s fixed label. Removing it restores the generated title.",
                icon = Icons.Type,
                defaultIconColor = UiTheme.Accent,
                hotKey = ModDefOf.SlopLabelSession,
                action = () => LabelDialog.Open(Session, label),
            };
        }

        Gizmo BuildDuplicateGizmo(SessionInfo info)
        {
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Duplicate",
                defaultDesc = $"Duplicate '{Session}' as a new agent in {info.Project}.",
                icon = Icons.Add,
                defaultIconColor = UiTheme.Accent,
                hotKey = ModDefOf.SlopDuplicateSession,
                action = () => TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info)),
            };
        }

        Gizmo BuildRemoveGizmo()
        {
            return new UiCommandAction(UiTheme.Btn.Default)
            {
                defaultLabel = "Remove",
                defaultDesc = $"Remove '{Session}' and its private state.",
                icon = Icons.Cross,
                defaultIconColor = UiTheme.Bad,
                hotKey = ModDefOf.SlopRemoveSession,
                action = () => Find.WindowStack.Add(ConfirmDialog.Create(
                    $"Remove session '{Session}'? This stops the session and removes it from config.toml. " +
                    "Its private state moves to trash. You can recover it for 14 days.",
                    () => SessionHub.Instance.SessionStore.Remove(Session, UiLayout.Fail), destructive: true)),
            };
        }

        public string GetInspectString() => "";

        public IEnumerable<InspectTabBase> GetInspectTabs() =>
            Enumerable.Empty<InspectTabBase>();
    }

    /// <summary>Add the current session to the base game temporary gizmo selection list.</summary>
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
                    // A sidebar session without a pawn clears map selection but retains session selection.
                    // Clear session selection if the map has multiple selected objects.
                    if (selector.SelectedObjects.Count > 0)
                        SessionSelectable.Current = null;
                    return;
                }

                var pawn = selected as Pawn;
                var session = pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
                // Synchronize session selection from the selected pawn.
                // Clear it for other objects so the base game inspect pane can appear.
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

                // Add the session after the base game clears the shared list, while the evaluation stack is empty.
                // The remaining instructions also add the map selection.
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, append));
                inserted = true;
                break;
            }

            if (!inserted)
                Log.Error("[SlopWorld] MapUIOnGUI changed. Session gizmos were not inserted.");
            return code;
        }
    }

    /// Include session actions even when map selection is empty.
    /// Modify MapUIOnGUI to avoid another DrawGizmoGridFor patch that conflicts with the GizmoGridShift flag.
    [HarmonyPatch(typeof(MapGizmoUtility), nameof(MapGizmoUtility.MapUIOnGUI))]
    public static class Patch_MapUIOnGUI_SessionSelection
    {
        // Hide the bottom action buttons during cutscenes to prevent drawing and input over the scene.
        static bool Prefix() => !Cutscene.Playing;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SessionGizmoSelection.InjectIntoMapUI(instructions);
    }
}
