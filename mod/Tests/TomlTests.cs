using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TomlTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("parses flat values and comments", ParsesFlatValuesAndComments);
            yield return ("round trips quoted values", RoundTripsQuotedValues);
            yield return ("rejects malformed entries", RejectsMalformedEntries);
            yield return ("finds separators after escaped quoted text", FindsEscapedSeparators);
            yield return ("canonicalizes typed scalar values", CanonicalizesTypedValues);
            yield return ("rejects structured values in the flat schema", RejectsStructuredValues);
        }

        static void ParsesFlatValuesAndComments()
        {
            var values = Toml.ParseFlat(
                "# endpoint\r\n" +
                "url = \"http://127.0.0.1:7717\" # inline comment\n" +
                "token = 'abc#123'\n" +
                "query = \"a=b # stays in the quoted value\"\n" +
                "plain = \"hello\" # trailing comment\n");

            AssertEx.Equal("http://127.0.0.1:7717", values["url"], "quoted value");
            AssertEx.Equal("abc#123", values["token"], "literal value");
            AssertEx.Equal("a=b # stays in the quoted value", values["query"],
                           "equals and hash in quoted value");
            AssertEx.Equal("hello", values["plain"], "quoted value with trailing comment");
            AssertEx.Equal(4, values.Count, "parsed entry count");
            AssertEx.Equal(0, Toml.ParseFlat(null).Count, "null TOML text");
        }

        static void RoundTripsQuotedValues()
        {
            string original = "quote \" slash \\ newline\n tab\t backspace\b " +
                              "formfeed\f carriage\r unicode ♥";
            string text = "value = " + Toml.Quote(original);

            AssertEx.Equal(original, Toml.ParseFlat(text)["value"], "quoted value round trip");
            AssertEx.Equal("\"\"", Toml.Quote(null), "null TOML string");
            AssertEx.Equal("☃", Toml.ParseFlat("value = \"\\u2603\"")["value"],
                           "short unicode escape");
            AssertEx.Equal("😀", Toml.ParseFlat("value = \"\\U0001F600\"")["value"],
                           "long unicode escape");
        }

        static void RejectsMalformedEntries()
        {
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("missing separator"),
                                             "missing equals");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat(" = value"),
                                             "empty key");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("value = \"unterminated"),
                                             "unterminated basic string");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("value = \"bad\\q\""),
                                             "invalid basic escape");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("value = \"ok\" trailing"),
                                             "trailing string text");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("value = \"\\uD800\""),
                                             "surrogate unicode escape");
        }

        static void FindsEscapedSeparators()
        {
            var values = Toml.ParseFlat("value = \"a\\\"=b\" # equals stays quoted\n");

            AssertEx.Equal("a\"=b", values["value"], "escaped quote does not end the value");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("value = 'ok' trailing"),
                                             "literal strings reject trailing text");
        }

        static void CanonicalizesTypedValues()
        {
            var values = Toml.ParseFlat(
                "enabled = true\n" +
                "count = 1_024\n" +
                "ratio = 1.25e0\n" +
                "day = 2025-01-02\n");

            AssertEx.Equal("true", values["enabled"], "boolean scalar conversion");
            AssertEx.Equal("1024", values["count"], "integer scalar conversion");
            AssertEx.Equal("1.25", values["ratio"], "float scalar conversion");
            AssertEx.Equal("2025-01-02", values["day"], "date scalar conversion");
        }

        public static void DatesRemainInvariantAcrossCultures()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var values = Toml.ParseFlat("day = 2025-01-02\ntime = 12:34:56.123\nstamp = 2025-01-02T12:34:56Z\n");
                AssertEx.Equal("2025-01-02", values["day"], "invariant date");
                AssertEx.Equal("12:34:56.123", values["time"], "invariant fractional time");
                AssertEx.Equal("2025-01-02T12:34:56Z", values["stamp"], "invariant timestamp");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }

        static void RejectsStructuredValues()
        {
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("items = [1, 2]"),
                                             "arrays are outside the flat schema");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("[section]\nvalue = 1"),
                                             "tables are outside the flat schema");
            AssertEx.Throws<FormatException>(() => Toml.ParseFlat("section.value = 1"),
                                             "dotted tables are outside the flat schema");
        }
    }
}
