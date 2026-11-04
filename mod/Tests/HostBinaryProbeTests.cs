using System;
using System.IO;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class HostBinaryProbeTests
    {
        string _root;
        string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Environment.GetEnvironmentVariable("PATH");
            _root = Path.Combine(
                Path.GetTempPath(),
                "slopworld-probe-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_root);
            Environment.SetEnvironmentVariable("PATH", _root);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("PATH", _path);
            Directory.Delete(_root, true);
        }

        void Which(string body)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("The host probe uses the POSIX which command.");
                return;
            }
            string script = Path.Combine(_root, "which");
            File.WriteAllText(script, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(
                script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        [Test]
        public void WhichUsesFirstNonemptyLineAndTrimsItsOutput()
        {
            Which("printf '\\n  /resolved/tool with spaces  \\r\\n/ignored/tool\\n'");
            File.WriteAllText(Path.Combine(_root, "tool"), "fallback");
            Assert.That(HostBinaryProbe.Locate("tool"), Is.EqualTo("/resolved/tool with spaces"));
        }

        [TestCase("exit 1")]
        [TestCase("printf '/incorrect/tool\\n'; exit 1")]
        [TestCase("exit 0")]
        [TestCase("printf '   \\n'")]
        public void UnsuccessfulOrEmptyWhichFallsBackToPath(string response)
        {
            Which(response);
            string expected = Path.Combine(_root, "tool");
            File.WriteAllText(expected, "fallback");
            Assert.That(HostBinaryProbe.Locate("tool"), Is.EqualTo(expected));
        }

        [Test]
        public void MissingWhichFallsBackInPathOrderAndSkipsMissingDirectories()
        {
            string first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
            string second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
            File.WriteAllText(Path.Combine(first, "tool"), "first");
            File.WriteAllText(Path.Combine(second, "tool"), "second");
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, Path.Combine(_root, "missing"), first, second)
            );
            Assert.That(HostBinaryProbe.Locate("tool"), Is.EqualTo(Path.Combine(first, "tool")));
            File.Delete(Path.Combine(first, "tool"));
            Assert.That(HostBinaryProbe.Locate("tool"), Is.EqualTo(Path.Combine(second, "tool")));
            Assert.That(HostBinaryProbe.Locate("not-present"), Is.Null);
        }
    }
}
