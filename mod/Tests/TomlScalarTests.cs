using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TomlScalarTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string value in new[] { "2147483648", "-2147483649", "1.5", "true", "\"12\"" })
                yield return ($"TOML integer reader rejects {value}", () =>
                {
                    var scalar = Toml.ParseFlatScalars("value = " + value)["value"];
                    Assert.That(scalar.TryGetInteger(out int number), Is.False);
                    Assert.That(number, Is.Zero);
                }
                );
            foreach (string value in new[] { "nan", "inf", "-inf", "1e100", "true", "\"1.5\"" })
                yield return ($"TOML float reader rejects {value}", () =>
                {
                    var scalar = Toml.ParseFlatScalars("value = " + value)["value"];
                    Assert.That(scalar.TryGetFloat(out _), Is.False);
                }
                );
        }

        public static void TypedReadersDoNotCoerceStringsOrBooleans()
        {
            var values = Toml.ParseFlatScalars("text = 'false'\nflag = false\ninteger = -2147483648\nmaximum = 2147483647\nratio = 1.25\n");
            Assert.That(values["text"].TryGetString(out var text), Is.True);
            Assert.That(text, Is.EqualTo("false"));
            Assert.That(values["flag"].TryGetString(out text), Is.False);
            Assert.That(text, Is.Null);
            Assert.That(values["text"].TryGetBoolean(out bool flag), Is.False);
            Assert.That(flag, Is.False);
            Assert.That(values["flag"].TryGetBoolean(out flag), Is.True);
            Assert.That(flag, Is.False);
            Assert.That(values["integer"].TryGetInteger(out int minimum), Is.True);
            Assert.That(minimum, Is.EqualTo(int.MinValue));
            Assert.That(values["maximum"].TryGetInteger(out int maximum), Is.True);
            Assert.That(maximum, Is.EqualTo(int.MaxValue));
            Assert.That(values["integer"].TryGetFloat(out float converted), Is.True);
            Assert.That(converted, Is.EqualTo((float)int.MinValue));
            Assert.That(values["ratio"].TryGetFloat(out converted), Is.True);
            Assert.That(converted, Is.EqualTo(1.25f));
        }

        public static void ScalarTextUsesInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var values = Toml.ParseFlat("ratio = 1.25\ncount = 1_024\nflag = false\n");
                Assert.That(values["ratio"], Is.EqualTo("1.25"));
                Assert.That(values["count"], Is.EqualTo("1024"));
                Assert.That(values["flag"], Is.EqualTo("false"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
