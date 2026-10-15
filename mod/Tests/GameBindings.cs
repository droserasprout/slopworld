using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace SlopWorld.Tests
{
    // Opt-in validation against installed assemblies, isolated from the harness's Unity stubs.
    // Resolve targets and inspect IL only: never initialize the engine or call game methods.
    static class GameBindings
    {
        internal static int Run(string managed, string modPath)
        {
            try
            {
                var context = new GameContext(Path.GetFullPath(managed), Path.GetFullPath(modPath));
                var mod = context.LoadFromAssemblyPath(Path.GetFullPath(modPath));
                // The executable normally loads these before ModBootstrap. Reflection-only
                // validation must load them explicitly for Harmony's type-name lookup.
                foreach (
                    string assembly in new[]
                    {
                        "Assembly-CSharp.dll",
                        "RimWorld.dll",
                        "Waylith.dll",
                    }
                )
                    context.LoadFromAssemblyPath(Path.Combine(Path.GetFullPath(managed), assembly));
                int count = 0;
                foreach (string owner in new[] { "Patch_NativeTextStyle", "Patch_NativeTextField" })
                {
                    var targets = (IEnumerable)Invoke(mod, owner, "TargetMethods");
                    foreach (MethodBase target in targets)
                    {
                        Require(target != null, owner + " has a missing game target");
                        count++;
                    }
                }
                var parser = (MethodBase)Invoke(mod, "Patch_NativeTextTags", "TargetMethod");
                Require(parser != null, "missing native rich-text parser");
                count++;

                var acquire = (MethodBase)Invoke(
                    mod,
                    "Patch_SwapchainAcquireTimeout",
                    "TargetMethod"
                );
                Require(acquire != null, "missing native swapchain acquisition");
                var original = PatchProcessor.GetOriginalInstructions(acquire);
                var rewritten =
                    (IEnumerable<CodeInstruction>)
                        Invoke(mod, "SwapchainAcquireTimeout", "Rewrite", original);
                Require(rewritten.Any(), "empty swapchain method");
                count++;

                var gui = context.LoadFromAssemblyPath(
                    Path.Combine(Path.GetFullPath(managed), "UnityStubs.dll")
                );
                var rect = gui.GetType("UnityEngine.Rect", throwOnError: true);
                var style = gui.GetType("UnityEngine.GUIStyle", throwOnError: true);
                var fieldRect = gui.GetType("UnityEngine.GUI", throwOnError: true)
                    .GetMethod(
                        "TextFieldTextRect",
                        BindingFlags.Static | BindingFlags.NonPublic,
                        null,
                        new[] { rect, typeof(bool), style },
                        null
                    );
                Require(
                    fieldRect != null && fieldRect.ReturnType == rect,
                    "unexpected field geometry signature"
                );
                count++;
                Console.WriteLine(
                    $"Validated {count} native game bindings and swapchain IL without starting the game."
                );
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        static object Invoke(Assembly mod, string owner, string method, params object[] args) =>
            mod.GetType("SlopWorld." + owner, throwOnError: true)
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        sealed class GameContext : AssemblyLoadContext
        {
            readonly string _managed;
            readonly string _mod;

            internal GameContext(string managed, string modPath)
                : base("game-bindings")
            {
                _managed = managed;
                _mod = Path.GetDirectoryName(modPath);
            }

            protected override Assembly Load(AssemblyName name)
            {
                // Share CodeInstruction identity with the checker; isolate game and mod types.
                if (name.Name == typeof(Harmony).Assembly.GetName().Name)
                    return typeof(Harmony).Assembly;
                foreach (string directory in new[] { _mod, _managed })
                {
                    string path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path))
                        return LoadFromAssemblyPath(path);
                }
                return null;
            }
        }
    }
}
