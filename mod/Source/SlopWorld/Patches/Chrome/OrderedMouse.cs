using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SlopWorld
{
    // Frame-level down/up flags cannot represent the release between two rapid clicks.
    [HarmonyPatch]
    static class OrderedMouse
    {
        internal record struct Edge(bool Down, Vector2 Position, EventModifiers Modifiers);

        internal static readonly List<Edge> Primary = new();

        static MethodBase TargetMethod() => OrderedKeyboard.RuntimeMethod("QueueMouseButton");

        static void Prefix(int __0, bool __1, Vector2 __2)
        {
            if (__0 == 0 && Input.GetMouseButton(0) != __1)
                Primary.Add(new Edge(__1, __2, OrderedKeyboard.Modifiers()));
        }

        [HarmonyPatch]
        internal static class Dispatch
        {
            static readonly Type Owner = AccessTools.TypeByName(
                "RimWorldWaylith.Imgui.ImguiDispatch"
            );
            static readonly MethodInfo Pass = AccessTools.Method(Owner, "RunInputPass");
            static bool _replayed;

            static MethodBase TargetMethod() => AccessTools.Method(Owner, "DispatchInput");

            static bool Down(int button, object dispatcher, Vector2 mouse)
            {
                if (button != 0 || !_replayed)
                    return Input.GetMouseButtonDown(button);
                // Convert bottom-origin input coordinates relative to the host's actual pointer.
                Vector2 last = Input.mousePosition;
                foreach (var edge in Primary)
                {
                    var delta = edge.Position - last;
                    var point = mouse + new Vector2(delta.x, -delta.y);
                    Pass.Invoke(
                        dispatcher,
                        new object[]
                        {
                            edge.Down ? EventType.MouseDown : EventType.MouseUp,
                            0,
                            point,
                            edge.Modifiers,
                        }
                    );
                    if (!edge.Down)
                        GUIUtility.hotControl = 0;
                }
                Primary.Clear();
                return false;
            }

            static bool Up(int button) =>
                (button != 0 || !_replayed) && Input.GetMouseButtonUp(button);

            static void Prefix() =>
                _replayed = WorkspaceWindow.HasTerminalKeyboardInput && Primary.Count > 1;

            static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions
            )
            {
                var down = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown));
                var up = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp));
                foreach (var instruction in instructions)
                {
                    if (instruction.Calls(down))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldloc_1);
                        yield return CodeInstruction.Call(typeof(Dispatch), nameof(Down));
                    }
                    else if (instruction.Calls(up))
                        yield return CodeInstruction.Call(typeof(Dispatch), nameof(Up));
                    else
                        yield return instruction;
                }
            }
        }
    }
}
