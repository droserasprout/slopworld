using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class TaskTextLayoutTests
    {
        [Test]
        public void TaskWrappingKeepsUnicodeElementsAndSourceOffsets()
        {
            const string source = "😀e\u0301𝄞界";
            Assert.That(TextElementLayout.Boundaries(source), Is.EqualTo(new[] { 0, 2, 4, 6, 7 }));
            var rows = TextElementLayout.Wrap(source, 0.5f, _ => 1f);
            Assert.That(rows.Select(row => source.Substring(row.Start, row.End - row.Start)),
                Is.EqualTo(new[] { "😀", "e\u0301", "𝄞", "界" }));
            Assert.That(string.Concat(rows.Select(row => source.Substring(row.Start, row.End - row.Start))),
                Is.EqualTo(source));
        }

        [Test]
        public void TaskWrappingPreservesWhitespaceAndEmptyLines()
        {
            const string source = "ab cd\n\n";
            var rows = TextElementLayout.Wrap(source, 3f, _ => 1f);
            Assert.That(rows.Select(row => source.Substring(row.Start, row.End - row.Start)),
                Is.EqualTo(new[] { "ab ", "cd", "", "" }));
            Assert.That(TextElementLayout.Wrap("", 1f, _ => 1f).Count, Is.EqualTo(1));
        }

    }
}
