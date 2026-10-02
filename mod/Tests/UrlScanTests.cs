using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UrlScanTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("extracts web URLs with boundary and scheme-case rules", ExtractsWebUrlWithBoundaryAndSchemeRules);
            yield return ("ignores non-web schemes and bare schemes", IgnoresNonWebAndBare);
            yield return ("trims trailing punctuation and balances brackets", TrimsAndBalances);
            yield return ("parses OSC 8 link bodies", ParsesOsc);
            yield return ("stops at URL-forbidden punctuation", StopsAtForbiddenPunctuation);
        }

        static void ExtractsWebUrlWithBoundaryAndSchemeRules()
        {
            var spans = UrlScan.FindUrls("see http://example.com/x here");

            AssertEx.True(spans != null, "a url is found");
            AssertEx.Equal(1, spans.Count, "exactly one span");
            AssertEx.Equal("http://example.com/x", spans[0].Url, "captured url");
            AssertEx.Equal(4, spans[0].Start, "span starts at the scheme");
            AssertEx.Equal(spans[0].Start + spans[0].Url.Length, spans[0].End, "span end matches url length");

            // The scheme is walked back to from the "://" over letters, so a non-letter
            // boundary is where the scheme (and the link) begins.
            var mixed = UrlScan.FindUrls("x=https://host/path");
            AssertEx.True(mixed != null, "a url after a non-letter boundary is found");
            AssertEx.Equal("https://host/path", mixed[0].Url, "capture starts at the scheme");
            AssertEx.Equal(2, mixed[0].Start, "the span starts after the '='");

            AssertEx.Equal("HtTpS://host/path", UrlScan.FindUrls("HtTpS://host/path")[0].Url,
                           "scheme matching ignores case without changing the target");

            // Letters abutting the scheme are pulled into it, so it no longer reads as http.
            AssertEx.True(UrlScan.FindUrls("prefixhttps://host/path") == null,
                          "letters before the scheme poison it");
        }

        static void IgnoresNonWebAndBare()
        {
            AssertEx.True(UrlScan.FindUrls("ftp://host/file") == null, "ftp is not linked");
            AssertEx.True(UrlScan.FindUrls("no links in here") == null, "plain text has no spans");
            AssertEx.True(UrlScan.FindUrls("https://") == null, "a bare scheme is the word, not a link");
        }

        static void TrimsAndBalances()
        {
            var dot = UrlScan.FindUrls("visit https://a.io/x.");
            AssertEx.Equal("https://a.io/x", dot[0].Url, "a trailing full stop is prose, not the link");

            var wrapped = UrlScan.FindUrls("(https://a.io/x)");
            AssertEx.Equal("https://a.io/x", wrapped[0].Url, "an unmatched closing paren is dropped");

            var wiki = UrlScan.FindUrls("https://en.wikipedia.org/wiki/Foo_(bar)");
            AssertEx.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", wiki[0].Url,
                           "a paren the url opened is kept");

            string balanced = "https://a.io/([x])";
            AssertEx.Equal(balanced, UrlScan.FindUrls(balanced + ")].)];!")[0].Url,
                           "mixed excess brackets and punctuation preserve balanced suffix");
            AssertEx.Equal("https://a.io/x", UrlScan.FindUrls("https://a.io/x" +
                new string(')', 4096))[0].Url, "long unmatched suffix is removed");
            AssertEx.Equal("https://a.io/(x)", UrlScan.FindUrls("https://a.io/(x)]")[0].Url,
                           "trimming stops at a balanced closer");
        }

        static void ParsesOsc()
        {
            AssertEx.Equal("http://x", UrlScan.Osc("8;;http://x"), "empty params, then the uri");
            AssertEx.Equal("http://x", UrlScan.Osc("8;id=1;http://x"), "params are skipped");
            AssertEx.Equal("", UrlScan.Osc("8;;"), "an empty uri closes a link");
            AssertEx.True(UrlScan.Osc("0;title") == null, "a non-link OSC leaves the caller's url standing");
            AssertEx.True(UrlScan.Osc("8;") == null, "missing URI does not close a link");
            AssertEx.True(UrlScan.Osc("8;id=1") == null, "parameters without URI leave the link open");
            AssertEx.True(UrlScan.Osc(null) == null, "null body is not a link");
        }

        static void StopsAtForbiddenPunctuation()
        {
            var spans = UrlScan.FindUrls("open http://example.test/a^b now");

            AssertEx.Equal(1, spans.Count, "one URL before forbidden punctuation");
            AssertEx.Equal("http://example.test/a", spans[0].Url,
                           "caret is not part of a URL");
        }
    }
}
