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
