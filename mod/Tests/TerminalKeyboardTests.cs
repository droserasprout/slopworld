using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalKeyboardTests
    {
        public static void ShiftEnterRemainsADistinctKeyOnBothScreensAndKeypads()
        {
            foreach (var key in new[] { KeyCode.Return, KeyCode.KeypadEnter })
            foreach (bool altScreen in new[] { false, true })
            foreach (bool shift in new[] { false, true })
            {
                var e = new Event { keyCode = key, shift = shift };
                Assert.That(
                    TerminalInputController.MapKey(e, altScreen),
                    Is.EqualTo(shift ? "S-Enter" : "Enter")
                );
            }
        }

        public static void ShiftAltForwardsWorkspaceLettersAndDigitsWithoutShift()
        {
            foreach (bool altScreen in new[] { false, true })
            {
                foreach (var key in new[] { KeyCode.A, KeyCode.S, KeyCode.Z, KeyCode.X })
                {
                    var e = new Event
                    {
                        keyCode = key,
                        alt = true,
                        shift = true,
                    };
                    Assert.That(
                        TerminalInputController.MapKey(e, altScreen),
                        Is.EqualTo("M-" + (char)('a' + (key - KeyCode.A)))
                    );
                }
                for (int digit = 0; digit <= 9; digit++)
                    foreach (var first in new[] { KeyCode.Alpha0, KeyCode.Keypad0 })
                    {
                        var e = new Event
                        {
                            keyCode = (KeyCode)((int)first + digit),
                            alt = true,
                            shift = true,
                        };
                        Assert.That(
                            TerminalInputController.MapKey(e, altScreen),
                            Is.EqualTo("M-" + digit)
                        );
                        e.alt = false;
                        Assert.That(TerminalInputController.MapKey(e, altScreen), Is.Null);
                    }
            }
        }

        public static void FileNavigationRequiresUnshiftedAltKeyDown()
        {
            foreach (var key in new[] { KeyCode.A, KeyCode.S, KeyCode.Z, KeyCode.X })
            foreach (bool alt in new[] { false, true })
            foreach (bool control in new[] { false, true })
            foreach (bool shift in new[] { false, true })
            foreach (var type in new[] { EventType.KeyDown, EventType.Repaint, EventType.Used })
            {
                var e = new Event
                {
                    keyCode = key,
                    alt = alt,
                    control = control,
                    shift = shift,
                    type = type,
                };
                bool expected =
                    alt
                    && !control
                    && !shift
                    && type == EventType.KeyDown
                    && (key == KeyCode.A || key == KeyCode.S);
                Assert.That(
                    TerminalInputController.TryFileWalkDirection(e, out int direction),
                    Is.EqualTo(expected)
                );
                Assert.That(direction, Is.EqualTo(expected ? (key == KeyCode.A ? -1 : 1) : 0));
            }
        }

        public static void PreservesTabModifiersAndStripsOnlyShiftFromFunctionKeys()
        {
            foreach (bool altScreen in new[] { false, true })
            foreach (bool control in new[] { false, true })
            foreach (bool alt in new[] { false, true })
            foreach (bool shift in new[] { false, true })
            {
                var e = new Event
                {
                    control = control,
                    alt = alt,
                    shift = shift,
                    keyCode = KeyCode.Tab,
                };
                string prefix = (control ? "C-" : "") + (alt ? "M-" : "");
                Assert.That(
                    TerminalInputController.MapKey(e, altScreen),
                    Is.EqualTo(prefix + (shift ? "BTab" : "Tab"))
                );
                for (int f = 1; f <= 12; f++)
                {
                    e.keyCode = (KeyCode)((int)KeyCode.F1 + f - 1);
                    Assert.That(
                        TerminalInputController.MapKey(e, altScreen),
                        Is.EqualTo(prefix + "F" + f)
                    );
                }
            }
        }

        public static void ControlLettersRetainAltWithoutAddingShift()
        {
            foreach (bool altScreen in new[] { false, true })
            foreach (bool alt in new[] { false, true })
            foreach (bool shift in new[] { false, true })
                for (int letter = 0; letter < 26; letter++)
                {
                    var e = new Event
                    {
                        keyCode = (KeyCode)((int)KeyCode.A + letter),
                        control = true,
                        alt = alt,
                        shift = shift,
                    };
                    string expected =
                        e.keyCode == KeyCode.V
                            ? null
                            : (alt ? "C-M-" : "C-") + (char)('a' + letter);
                    Assert.That(TerminalInputController.MapKey(e, altScreen), Is.EqualTo(expected));
                }
        }

        public static void ControlPunctuationRetainsAltWithoutAddingShift()
        {
            var keys = new[]
            {
                (KeyCode.Space, "@"),
                (KeyCode.At, "@"),
                (KeyCode.LeftBracket, "["),
                (KeyCode.Backslash, "\\"),
                (KeyCode.RightBracket, "]"),
                (KeyCode.Caret, "^"),
                (KeyCode.Underscore, "_"),
                (KeyCode.Alpha2, "@"),
                (KeyCode.Alpha6, "^"),
                (KeyCode.Minus, "_"),
            };
            foreach (bool altScreen in new[] { false, true })
            foreach (bool alt in new[] { false, true })
            foreach (bool shift in new[] { false, true })
            foreach (var (key, character) in keys)
            {
                var e = new Event
                {
                    keyCode = key,
                    control = true,
                    alt = alt,
                    shift = shift,
                };
                bool requiresShift =
                    key == KeyCode.Alpha2 || key == KeyCode.Alpha6 || key == KeyCode.Minus;
                string expected =
                    requiresShift && !shift ? null : (alt ? "C-M-" : "C-") + character;
                Assert.That(TerminalInputController.MapKey(e, altScreen), Is.EqualTo(expected));
            }
        }

        public static void RetainsControlCharactersAndNavigationPolicy()
        {
            var e = new Event
            {
                control = true,
                shift = true,
                keyCode = KeyCode.Alpha2,
            };
            Assert.That(TerminalInputController.MapKey(e, true), Is.EqualTo("C-@"));
            e.keyCode = KeyCode.Alpha6;
            Assert.That(TerminalInputController.MapKey(e, true), Is.EqualTo("C-^"));
            e.keyCode = KeyCode.Minus;
            Assert.That(TerminalInputController.MapKey(e, true), Is.EqualTo("C-_"));
            e.keyCode = KeyCode.V;
            Assert.That(TerminalInputController.MapKey(e, true), Is.Null);
            e.keyCode = KeyCode.UpArrow;
            Assert.That(TerminalInputController.MapKey(e, false), Is.EqualTo("C-Up"));
            Assert.That(TerminalInputController.MapKey(e, true), Is.EqualTo("C-S-Up"));
        }
    }
}
