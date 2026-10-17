using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class LibraryProjectFilterTests
    {
        public static void AllChecksEveryProjectAndTogglingOneHidesOnlyThatProject()
        {
            var filter = new LibraryProjectFilter();
            var projects = new[] { "a", "b", "[none]" };
            Assert.That(filter.Filtering, Is.False);
            Assert.That(filter.Label, Is.EqualTo("All"));
            foreach (var project in projects)
                Assert.That(filter.Passes(project), Is.True);
            Assert.That(filter.Passes("future"), Is.True);

            filter.Toggle("a", projects);
            Assert.That(filter.Filtering, Is.True);
            Assert.That(filter.Passes("a"), Is.False);
            Assert.That(filter.Passes("b"), Is.True);
            Assert.That(filter.Passes("[none]"), Is.True);
            Assert.That(filter.Passes("future"), Is.False);
        }

        public static void EmptySelectionStaysEmptyUntilAProjectOrAllIsSelected()
        {
            var filter = new LibraryProjectFilter();
            var projects = new[] { "a", "[none]" };
            foreach (var project in projects)
                filter.Toggle(project, projects);
            Assert.That(filter.Filtering, Is.True);
            Assert.That(filter.Label, Is.EqualTo("No projects"));
            foreach (var project in projects)
                Assert.That(filter.Passes(project), Is.False);
            Assert.That(filter.Passes("future"), Is.False);

            filter.Toggle("a", projects);
            Assert.That(filter.Label, Is.EqualTo("a"));
            Assert.That(filter.Passes("a"), Is.True);
            Assert.That(filter.Passes("[none]"), Is.False);
            filter.SelectAll();
            Assert.That(filter.Filtering, Is.False);
            Assert.That(filter.Passes("future"), Is.True);
            filter.Toggle("a", projects);
            Assert.That(filter.Passes("a"), Is.False);
            Assert.That(filter.Passes("[none]"), Is.True);
        }
    }
}
