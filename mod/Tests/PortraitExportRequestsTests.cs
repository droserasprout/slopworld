using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class PortraitExportRequestsTests
    {
        string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "slopworld-portraits-" + Guid.NewGuid().ToString("N")
            );
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        [Test]
        public void AcknowledgesAfterCaptureAndDoesNotRepeatTheSameRequest()
        {
            var mailbox = new PortraitExportRequests(root, 123);
            string id = new string('a', 32);
            File.WriteAllText(Path.Combine(root, "request"), id);
            int count = 0;
            mailbox.Poll(() =>
            {
                Assert.That(File.Exists(Path.Combine(root, "response")), Is.False);
                count++;
            });
            mailbox.Poll(() => count++);
            using var response = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "response"))
            );
            Assert.That(count, Is.EqualTo(1));
            Assert.That(response.RootElement.GetProperty("id").GetString(), Is.EqualTo(id));
            Assert.That(response.RootElement.GetProperty("ok").GetBoolean(), Is.True);
        }

        [Test]
        public void FailedCaptureIsAcknowledgedOnceAndANewRequestCanRecover()
        {
            var mailbox = new PortraitExportRequests(root, 123);
            string path = Path.Combine(root, "request");
            File.WriteAllText(path, new string('a', 32));
            mailbox.Poll(() => throw new IOException("readback failed"));
            using (
                var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "response")))
            )
                Assert.That(response.RootElement.GetProperty("ok").GetBoolean(), Is.False);
            mailbox.Poll(() => Assert.Fail("Repeated failed request"));
            File.WriteAllText(path, new string('b', 32));
            mailbox.Poll(() => { });
            using var recovered = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "response"))
            );
            Assert.That(
                recovered.RootElement.GetProperty("id").GetString(),
                Is.EqualTo(new string('b', 32))
            );
            Assert.That(recovered.RootElement.GetProperty("ok").GetBoolean(), Is.True);
        }

        [Test]
        public void MissingOrMalformedRequestsNeverCapture()
        {
            var mailbox = new PortraitExportRequests(root, 123);
            mailbox.Poll(() => Assert.Fail("Missing request"));
            foreach (
                string value in new[]
                {
                    "",
                    "../destination",
                    new string('g', 32),
                    new string('a', 33),
                }
            )
            {
                File.WriteAllText(Path.Combine(root, "request"), value);
                mailbox.Poll(() => Assert.Fail("Invalid request"));
            }
            Assert.That(File.Exists(Path.Combine(root, "response")), Is.False);
        }
    }
}
