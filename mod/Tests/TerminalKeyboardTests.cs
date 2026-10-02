using UnityEngine;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TerminalKeyboardTests
    {
        public static void PreservesTabAndFunctionModifiers()
        {
            foreach (bool altScreen in new[] { false, true })
                foreach (bool control in new[] { false, true })
                    foreach (bool alt in new[] { false, true })
                        foreach (bool shift in new[] { false, true })
                        {
                            var e = new Event { control = control, alt = alt, shift = shift, keyCode = KeyCode.Tab };
                            string prefix = (control ? "C-" : "") + (alt ? "M-" : "");
                            Assert.That(TerminalInputController.MapKey(e, altScreen), Is.EqualTo(prefix + (shift ? "BTab" : "Tab")));
                            for (int f = 1; f <= 12; f++)
                            {
                                e.keyCode = (KeyCode)((int)KeyCode.F1 + f - 1);
                                Assert.That(TerminalInputController.MapKey(e, altScreen), Is.EqualTo(prefix + (shift ? "S-" : "") + "F" + f));
                            }
                        }
        }

        public static void RetainsControlCharactersAndNavigationPolicy()
        {
            var e = new Event { control = true, shift = true, keyCode = KeyCode.Alpha2 };
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
