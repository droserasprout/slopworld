using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeKeyDispatchTests
    {
        static bool _focused;
        static readonly List<KeyCode> Delivered = new List<KeyCode>();
        static readonly KeyCode[] Keys = { KeyCode.F12, KeyCode.A, KeyCode.C };
        static bool CapturesText
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => _focused;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Consumes(KeyCode key) => _focused;

        static bool GameFilter(KeyCode key, EventModifiers modifiers) =>
            _focused && (key != KeyCode.F12 || modifiers != 0);

        // Match the native argument positions and its distinct edit/game lanes.
        sealed class Dispatcher
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal void Dispatch(Vector2 mouse, EventModifiers modifiers)
            {
                if (modifiers != 0 && Consumes(KeyCode.C))
                    Delivered.Add(KeyCode.C);
                if (!CapturesText)
                    foreach (var key in Keys)
                        if (!Consumes(key))
                            Delivered.Add(key);
            }
        }

        static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code) =>
            NativeKeyDispatch.Rewrite(
                code,
                AccessTools.PropertyGetter(typeof(NativeKeyDispatchTests), nameof(CapturesText)),
                AccessTools.Method(typeof(NativeKeyDispatchTests), nameof(Consumes)),
                AccessTools.Method(typeof(NativeKeyDispatchTests), nameof(GameFilter))
            );

        [Test]
        public void FocusedFieldAdmitsWorkspaceKeysAndRetainsExactlyOneEditShortcut()
        {
            var harmony = new Harmony("SlopWorld.Tests.NativeKeys");
            var target = AccessTools.Method(typeof(Dispatcher), nameof(Dispatcher.Dispatch));
            try
            {
                harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(
                        AccessTools.Method(typeof(NativeKeyDispatchTests), nameof(Rewrite))
                    )
                );
                var dispatcher = new Dispatcher();
                _focused = true;
                Delivered.Clear();
                dispatcher.Dispatch(default, 0);
                Assert.That(Delivered, Is.EqualTo(new[] { KeyCode.F12 }));
                Delivered.Clear();
                dispatcher.Dispatch(default, (EventModifiers)2);
                Assert.That(Delivered, Is.EqualTo(new[] { KeyCode.C }));
                _focused = false;
                Delivered.Clear();
                dispatcher.Dispatch(default, 0);
                Assert.That(Delivered, Is.EqualTo(Keys));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                _focused = false;
                Delivered.Clear();
            }
        }

        [Test]
        public void UnknownDispatchShapeFailsInsteadOfRemovingTextProtection()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Rewrite(Array.Empty<CodeInstruction>()).ToArray()
            );
        }
    }
}
