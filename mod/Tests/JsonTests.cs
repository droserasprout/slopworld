using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class JsonTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("parses nested values and accessors", ParsesNestedValuesAndAccessors);
            yield return ("round trips quoted strings", RoundTripsQuotedStrings);
            yield return ("round trips supplementary emoji", RoundTripsSupplementaryEmoji);
            yield return ("parses numbers and literals", ParsesNumbersAndLiterals);
            yield return ("parses all short string escapes", ParsesShortEscapes);
            yield return ("decode and skip share strict JSON validation", ValidatesSkippedValues);
        }

        static void ParsesNestedValuesAndAccessors()
        {
            var value = JVal.Parse(
                "{\"name\":\"slop\",\"active\":true," +
                "\"items\":[1,2,{\"ok\":false}],\"ratio\":-1.25}");

            AssertEx.Equal("slop", value["name"].AsString(), "object string");
            AssertEx.True(value["active"].AsBool(), "true literal");
            AssertEx.Equal(3, value["items"].Count, "array count");
            AssertEx.Equal(2, value["items"][1].AsInt(), "array number");
            AssertEx.False(value["items"][2]["ok"].AsBool(true), "false literal");
            AssertEx.Equal(-1.25f, value["ratio"].AsFloat(), "decimal number");
            AssertEx.True(value["missing"].IsNull, "missing object key");
            AssertEx.Equal("fallback", value["missing"].AsString("fallback"),
                           "missing string fallback");
            AssertEx.Sequence(Array.Empty<JVal>(), JVal.Parse("[]").Items,
                              "empty array items");
        }

        static void RoundTripsQuotedStrings()
        {
            string original = "quote \" slash \\ newline\n tab\t backspace\b formfeed\f " +
                              "carriage\r unicode ♥";
            string encoded = JVal.Q(original);

            AssertEx.Equal(original, JVal.Parse(encoded).AsString(), "quoted string round trip");
            AssertEx.Equal("\"\"", JVal.Q(null), "null string becomes empty JSON string");
            AssertEx.Equal("true", JVal.B(true), "true JSON literal");
            AssertEx.Equal("false", JVal.B(false), "false JSON literal");
        }

        static void ParsesNumbersAndLiterals()
        {
            var value = JVal.Parse("[true,false,null,-12.5e2,7]");

            AssertEx.True(value[0].AsBool(), "array true literal");
            AssertEx.False(value[1].AsBool(true), "array false literal");
            AssertEx.True(value[2].IsNull, "array null literal");
            AssertEx.Equal(-1250f, value[3].AsFloat(), "exponent number");
            AssertEx.Equal(7L, value[4].AsLong(), "integer number");
            AssertEx.Equal(99, value[2].AsInt(99), "null integer fallback");
        }

        static void RoundTripsSupplementaryEmoji()
        {
            const string emoji = "\U0001F606";
            string wire = "{\"text\":" + JVal.Q(emoji) + "}";
            AssertEx.Equal(emoji, JVal.Parse(wire)["text"].AsString(),
                           "surrogate pair survives the paste JSON shape");
        }

        static void ValidatesSkippedValues()
        {
            foreach (string json in new[]
            {
                "{ \"a\" : 1, \"b\" : [true, false, null, {}, []] }",
                "\"\\b\\f\\n\\r\\t\\/\\\\\\\"\\u002d\\ud83d\\ude06\"",
                "[0, -0, 1.25, -12.5e+2, 1E-2]",
            })
            {
                JVal.Parse(json);
                var reader = new JsonReader(json);
                reader.Value(false);
                reader.Finish();
            }
            foreach (string json in new[]
            {
                "", "[", "{", "[1,]", "{\"a\":1,}", "{\"a\" 1}", "[1 2]",
                "true false", "tru", "nul", "01", "-", "1.", "1e+", "+1", "1١",
                "\"bad\\q\"", "\"bad\\u00xz\"", "\"bad\\u123\"", "\"unclosed",
                "\"bad\ntext\"", "\"bad\\\ntext\"", "\u00a0null",
                new string('[', 130) + new string(']', 130),
            })
            {
                AssertEx.Throws<FormatException>(() => JVal.Parse(json), "reject decoded " + json);
                AssertEx.Throws<FormatException>(() =>
                {
                    var reader = new JsonReader(json);
                    reader.Value(false);
                    reader.Finish();
                }, "reject skipped " + json);
            }
        }

        static void ParsesShortEscapes()
        {
            var value = JVal.Parse("\"\\b\\f\\/\"").AsString();

            AssertEx.Equal("\b\f/", value, "backspace, formfeed and slash escapes");
        }
    }
}
