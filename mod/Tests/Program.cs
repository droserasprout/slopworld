using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--perf-bench") >= 0) return Benchmarks.Run();
            bool quiet = false;
            foreach (var arg in args)
                quiet |= arg == "--quiet";

            var tests = new List<(string Name, Action Body)>();
            foreach (var test in RepaintTests.Cases())
                tests.Add(("Repaint: " + test.Name, test.Body));
            foreach (var test in JsonTests.Cases())
                tests.Add(("JVal: " + test.Name, test.Body));
            foreach (var test in FuzzyTests.Cases())
                tests.Add(("Fuzzy: " + test.Name, test.Body));
            foreach (var test in TomlTests.Cases())
                tests.Add(("TOML: " + test.Name, test.Body));
            foreach (var test in DaemonConfigTests.Cases())
                tests.Add(("DaemonConfig: " + test.Name, test.Body));
            foreach (var test in EndpointTests.Cases())
                tests.Add(("Endpoint: " + test.Name, test.Body));
            foreach (var test in NetworkModeTests.Cases())
                tests.Add(("NetworkMode: " + test.Name, test.Body));
            foreach (var test in LibraryItemTests.Cases())
                tests.Add(("LibraryItem: " + test.Name, test.Body));
            foreach (var test in ProjectInfoTests.Cases())
                tests.Add(("ProjectInfo: " + test.Name, test.Body));
            foreach (var test in DnsConfigTests.Cases())
                tests.Add(("DnsConfig: " + test.Name, test.Body));
            foreach (var test in MountEntryTests.Cases())
                tests.Add(("MountEntry: " + test.Name, test.Body));
            foreach (var test in DaemonCapabilitiesTests.Cases())
                tests.Add(("DaemonCapabilities: " + test.Name, test.Body));
            foreach (var test in DaemonHealthTests.Cases())
                tests.Add(("DaemonHealth: " + test.Name, test.Body));
            foreach (var test in SessionLimitsTests.Cases())
                tests.Add(("SessionLimits: " + test.Name, test.Body));
            foreach (var test in UsageInfoTests.Cases())
                tests.Add(("UsageInfo: " + test.Name, test.Body));
            foreach (var test in UsageWindowTests.Cases())
                tests.Add(("UsageWindow: " + test.Name, test.Body));
            foreach (var test in SessionInfoTests.Cases())
                tests.Add(("SessionInfo: " + test.Name, test.Body));
            foreach (var test in TaskInfoTests.Cases())
                tests.Add(("TaskInfo: " + test.Name, test.Body));
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
            foreach (var test in TerminalHistoryTests.Cases())
                tests.Add(("TerminalHistory: " + test.Name, test.Body));
            foreach (var test in TextSelectionTests.Cases())
                tests.Add(("TextSelection: " + test.Name, test.Body));
            foreach (var test in PagerCommandsTests.Cases())
                tests.Add(("PagerCommands: " + test.Name, test.Body));
            foreach (var test in PagerLifecycleTests.Cases())
                tests.Add(("PagerLifecycle: " + test.Name, test.Body));
            foreach (var test in NameToolsTests.Cases())
                tests.Add(("NameTools: " + test.Name, test.Body));
            foreach (var test in SongRecognizerTests.Cases())
                tests.Add(("SongRecognizer: " + test.Name, test.Body));
            foreach (var test in JukeboxHistoryTests.Cases())
                tests.Add(("JukeboxHistory: " + test.Name, test.Body));
            tests.Add(("ModSettings: persistence", ModSettingsTests.Persistence));
            tests.Add(("FramePolicy: focus and settings transitions", FramePolicyTests.Transitions));
            tests.Add(("Idle work: message ordering and bounded batches", IdleWorkTests.Messages));
            tests.Add(("Idle work: elapsed-time scheduling", IdleWorkTests.Scheduling));
            tests.Add(("Idle work: sidebar title invalidation", IdleWorkTests.Titles));
            tests.Add(("Eco: maintenance transitions", EcoWorkTests.Maintenance));
            tests.Add(("Eco: colony membership revisions", EcoWorkTests.Membership));
            tests.Add(("Eco: usage snapshot and settings invalidation", EcoWorkTests.Usage));
            tests.Add(("Eco: clock boundaries and locale", EcoWorkTests.Clock));
            tests.Add(("HubCatalog: ordering", HubCatalogTests.Ordering));
            tests.Add(("Workspace: geometry", WorkspaceLayoutTests.Geometry));
            int failed = 0;

            foreach (var test in tests)
            {
                try
                {
                    test.Body();
                    if (!quiet)
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
