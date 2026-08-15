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
        }

        static void ParsesFlatValuesAndComments()
        {
            var values = Toml.ParseFlat(
                "# endpoint\r\n" +
                "url = \"http://127.0.0.1:7717\" # inline comment\n" +
                "token = 'abc#123'\n" +
                "query = \"a=b # stays in the quoted value\"\n" +
                "plain = hello # trailing comment\n");

            AssertEx.Equal("http://127.0.0.1:7717", values["url"], "quoted value");
            AssertEx.Equal("abc#123", values["token"], "literal value");
            AssertEx.Equal("a=b # stays in the quoted value", values["query"],
                           "equals and hash in quoted value");
            AssertEx.Equal("hello", values["plain"], "bare value comment");
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
    }
}
