using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TodoTextTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 28);
        const string Id = "9fd724f8-7d06-4d31-9a78-a158f402d03e";

        public static void PrefixFieldsOnlyApplyAtTheStartOfTheLine()
        {
            foreach (
                string text in new[]
                {
                    "  (A) Call someone",
                    "  2026-10-27 Call someone",
                    "Call someone (A)",
                    "X Not complete",
                }
            )
            {
                var entry = TodoText.Parse(text).Single();
                Assert.That(entry.Priority, Is.EqualTo(""));
                Assert.That(entry.Created, Is.EqualTo(""));
                Assert.That(entry.Completed, Is.False);
            }
        }

        public static void TaskFieldsAreProjectedWithoutExposingWorkerMetadataAsTitle()
        {
            var entry = TodoText
                .Parse(
                    "(A) 2026-10-27 Fix reconnect +slopworld @coding due:2026-11-01 rec:1w sw-id:"
                        + Id
                        + " sw-task:task-1"
                )
                .Single();
            Assert.That(entry.Title, Is.EqualTo("Fix reconnect"));
            Assert.That(entry.Priority, Is.EqualTo("A"));
            Assert.That(entry.Created, Is.EqualTo("2026-10-27"));
            Assert.That(entry.Projects, Is.EqualTo("slopworld"));
            Assert.That(entry.Contexts, Is.EqualTo("coding"));
            Assert.That(entry.DueDate, Is.EqualTo(new DateTime(2026, 11, 1)));
            Assert.That(entry.Id, Is.EqualTo(Id));
            Assert.That(entry.TaskId, Is.EqualTo("task-1"));
        }

        public static void FieldEditsKeepUnknownMetadataOtherLinesAndLineEndings()
        {
            string original =
                "(B) Fix  reconnect +slopworld @coding due:2026-11-01 rec:1w custom:keep sw-id:"
                + Id
                + " sw-task:task-1\r\n\r\nLeave  this untouched\r\n";
            var entry = TodoText.Parse(original).First();
            Assert.That(
                TodoText.Update(original, entry, new TodoDraft(entry), Today),
                Is.EqualTo(original)
            );
            var draft = new TodoDraft(entry)
            {
                Due = "2026-11-02",
                Projects = "slopworld client",
                Contexts = "review",
            };
            string edited = TodoText.Update(original, entry, draft, Today);
            Assert.That(edited, Does.StartWith("(B) Fix  reconnect"));
            Assert.That(edited, Does.Contain("+slopworld +client"));
            Assert.That(edited, Does.Contain("@review"));
            Assert.That(edited, Does.Contain("due:2026-11-02"));
            Assert.That(edited, Does.Contain("rec:1w custom:keep sw-id:" + Id + " sw-task:task-1"));
            Assert.That(edited, Does.EndWith("\r\n\r\nLeave  this untouched\r\n"));
        }

        public static void CompletingAndReopeningPreservePriorityCreationDateAndWorkerLink()
        {
            string text = "(A) 2026-10-27 Fix reconnect +client sw-task:task-1";
            var entry = TodoText.Parse(text).Single();
            string completed = TodoText.Update(
                text,
                entry,
                new TodoDraft(entry) { Completed = true },
                Today
            );
            Assert.That(completed, Does.StartWith("x 2026-10-28 2026-10-27 Fix reconnect"));
            Assert.That(completed, Does.Contain("pri:A"));
            entry = TodoText.Parse(completed).Single();
            Assert.That(entry.Completed, Is.True);
            Assert.That(entry.Priority, Is.EqualTo("A"));
            string reopened = TodoText.Update(
                completed,
                entry,
                new TodoDraft(entry) { Completed = false },
                Today.AddDays(1)
            );
            Assert.That(reopened, Does.StartWith("(A) 2026-10-27 Fix reconnect"));
            Assert.That(reopened, Does.Not.Contain("pri:A"));
            Assert.That(reopened, Does.Contain("sw-task:task-1"));
        }

        public static void EditingCompletedTaskDoesNotChangeItsCompletionDate()
        {
            string text = "x 2026-10-20 2026-10-18 Old title pri:B sw-task:task-1";
            var entry = TodoText.Parse(text).Single();
            string changed = TodoText.Update(
                text,
                entry,
                new TodoDraft(entry) { Title = "New title" },
                Today
            );
            var updated = TodoText.Parse(changed).Single();
            Assert.That(updated.CompletedOn, Is.EqualTo("2026-10-20"));
            Assert.That(updated.Title, Is.EqualTo("New title"));
            Assert.That(updated.Priority, Is.EqualTo("B"));
            Assert.That(updated.TaskId, Is.EqualTo("task-1"));
        }

        public static void MetadataCannotBeInjectedThroughEditableTaskFields()
        {
            var entry = TodoText.Parse("Task sw-task:pending").Single();
            foreach (
                string title in new[] { "Task sw-task:other", "Task\nAnother", "Task +project", "" }
            )
                Assert.That(new TodoDraft(entry) { Title = title }.Error, Is.Not.Null);
            Assert.That(
                new TodoDraft(entry) { Projects = "project sw-id:other" }.Error,
                Is.Not.Null
            );
            Assert.That(new TodoDraft(entry) { Due = "2026-02-30" }.Error, Is.Not.Null);
            Assert.That(new TodoDraft(entry) { Priority = "AA" }.Error, Is.Not.Null);
        }

        public static void QuickAddAndDeletePreserveOtherEntriesAndBlankLines()
        {
            string text = "First\r\n\r\nSecond";
            string added = TodoText.Add(text, "Third", "client", Today);
            Assert.That(added, Is.EqualTo(text + "\r\n2026-10-28 Third +client\r\n"));
            var second = TodoText.Parse(added)[1];
            string removed = TodoText.Replace(added, second, "");
            Assert.That(removed, Is.EqualTo("First\r\n\r\n\r\n2026-10-28 Third +client\r\n"));
            Assert.Throws<InvalidOperationException>(() =>
                TodoText.Replace("Changed\r\n\r\nOther", second, "")
            );
        }

        public static void FiltersDistinguishOverdueTodayUpcomingAndCompleted()
        {
            var entries = TodoText.Parse(
                "Overdue due:2026-10-27 +client\nToday due:2026-10-28 +client\nLater due:2026-11-01 +server\nNo date\nx 2026-10-27 Finished due:2026-10-27"
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.Active, "", "", Today)),
                Is.EqualTo(4)
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.Today, "", "", Today)),
                Is.EqualTo(2)
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.Overdue, "", "", Today)),
                Is.EqualTo(1)
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.Upcoming, "", "", Today)),
                Is.EqualTo(1)
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.Completed, "", "", Today)),
                Is.EqualTo(1)
            );
            Assert.That(
                entries.Count(e => TodoText.Matches(e, TodoFilter.All, "client", "TODAY", Today)),
                Is.EqualTo(1)
            );
            Assert.That(
                TodoText.Sort(entries, TodoSort.Due).Select(e => e.Title),
                Is.EqualTo(new[] { "Overdue", "Today", "Later", "No date", "Finished" })
            );
        }

        public static void StructuralLookingTitlesRemainLiteralAfterEdits()
        {
            foreach (
                string title in new[] { "x marks the spot", "(A) is a label", "2026-11-01 release" }
            )
            {
                string text = "Old title sw-task:pending";
                var entry = TodoText.Parse(text).Single();
                string updated = TodoText.Update(
                    text,
                    entry,
                    new TodoDraft(entry) { Title = title },
                    Today
                );
                var parsed = TodoText.Parse(updated).Single();
                Assert.That(parsed.Title, Is.EqualTo(title));
                Assert.That(parsed.Completed, Is.False);
                Assert.That(parsed.Priority, Is.EqualTo(""));
                Assert.That(parsed.TaskId, Is.EqualTo("pending"));
            }
        }

        public static void DateAndPrioritySortingAreIndependentOfFileOrder()
        {
            var entries = TodoText.Parse(
                "No priority\n(B) Medium\n(A) High\nx 2026-10-27 Done pri:A"
            );
            Assert.That(
                TodoText.Sort(entries, TodoSort.Priority).Select(e => e.Title),
                Is.EqualTo(new[] { "High", "Medium", "No priority", "Done" })
            );
            Assert.That(
                TodoText.Sort(entries, TodoSort.Title).Select(e => e.Title),
                Is.EqualTo(new[] { "High", "Medium", "No priority", "Done" })
            );
        }
    }
}
