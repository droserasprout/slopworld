using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class Program
    {
        static int Main()
        {
            var tests = new List<(string Name, Action Body)>();
            foreach (var test in JsonTests.Cases())
                tests.Add(("JVal: " + test.Name, test.Body));
            foreach (var test in FuzzyTests.Cases())
                tests.Add(("Fuzzy: " + test.Name, test.Body));
            foreach (var test in TomlTests.Cases())
                tests.Add(("TOML: " + test.Name, test.Body));
            foreach (var test in SlopConfigTests.Cases())
                tests.Add(("SlopConfig: " + test.Name, test.Body));
            foreach (var test in EndpointTests.Cases())
                tests.Add(("Endpoint: " + test.Name, test.Body));
            foreach (var test in NetworkModeTests.Cases())
                tests.Add(("NetworkMode: " + test.Name, test.Body));
            foreach (var test in DnsConfigTests.Cases())
                tests.Add(("DnsConfig: " + test.Name, test.Body));
            foreach (var test in DaemonCapabilitiesTests.Cases())
                tests.Add(("DaemonCapabilities: " + test.Name, test.Body));
            foreach (var test in SessionLimitsTests.Cases())
                tests.Add(("SessionLimits: " + test.Name, test.Body));
            foreach (var test in SessionInfoTests.Cases())
                tests.Add(("SessionInfo: " + test.Name, test.Body));
            foreach (var test in ScreenBufTests.Cases())
                tests.Add(("ScreenBuf: " + test.Name, test.Body));
            foreach (var test in SgrTests.Cases())
                tests.Add(("Sgr: " + test.Name, test.Body));
            foreach (var test in UrlScanTests.Cases())
                tests.Add(("UrlScan: " + test.Name, test.Body));
            foreach (var test in PathScanTests.Cases())
                tests.Add(("PathScan: " + test.Name, test.Body));
            foreach (var test in TerminalColumnsTests.Cases())
                tests.Add(("TerminalColumns: " + test.Name, test.Body));
            foreach (var test in PagerCommandsTests.Cases())
                tests.Add(("PagerCommands: " + test.Name, test.Body));
            foreach (var test in SessionNavigationTests.Cases())
                tests.Add(("SessionNavigation: " + test.Name, test.Body));
            foreach (var test in SongRecognizerTests.Cases())
                tests.Add(("SongRecognizer: " + test.Name, test.Body));
            foreach (var test in JukeboxHistoryTests.Cases())
                tests.Add(("JukeboxHistory: " + test.Name, test.Body));
            int failed = 0;

            foreach (var test in tests)
            {
                try
                {
                    test.Body();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
                }
            }

            Console.WriteLine($"{tests.Count - failed} tests passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
