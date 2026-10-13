using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;
using static SlopWorld.LibraryRowTarget;

namespace SlopWorld.Tests
{
    [TestFixture]
    internal class LibraryRowTests
    {
        sealed class Effects : IEffects
        {
            internal readonly List<(object Target, string Action)> Calls = new List<(object, string)>();
            internal readonly List<(string Name, bool Template)> Remembered = new List<(string, bool)>();
            public void Run(LibraryItemInfo target) => Calls.Add((target, "Run"));
            public void Edit(LibraryItemInfo target) => Calls.Add((target, "Edit"));
            public void CopyText(LibraryItemInfo target) => Calls.Add((target, "CopyText"));
            public void Menu(LibraryItemInfo target) => Calls.Add((target, "Menu"));
            public void Create(AgentTemplateInfo target) => Calls.Add((target, "Create"));
            public void Edit(AgentTemplateInfo target) => Calls.Add((target, "Edit"));
            public void Menu(AgentTemplateInfo target) => Calls.Add((target, "Menu"));
            public void Edit(ProjectInfo target) => Calls.Add((target, "Edit"));
            public void Worktrees(ProjectInfo target) => Calls.Add((target, "Worktrees"));
            public void Menu(ProjectInfo target) => Calls.Add((target, "Menu"));
            public void Terminal(WorktreeEntry target) => Calls.Add((target, "Terminal"));
            public void Project(WorktreeEntry target) => Calls.Add((target, "Project"));
            public void Menu(WorktreeEntry target) => Calls.Add((target, "Menu"));
            public void Edit(PresetInfo target) => Calls.Add((target, "Edit"));
            public void Remove(PresetInfo target) => Calls.Add((target, "Remove"));
            public void Edit(CommandInfo target) => Calls.Add((target, "Edit"));
            public void Remove(CommandInfo target) => Calls.Add((target, "Remove"));
            public void RememberLibrary(string name, bool template) => Remembered.Add((name, template));
        }

        static WorktreeEntry Checkout(string project = "same", string id = "branch") => new WorktreeEntry
        {
            Project = project,
            Tree = new Wire.Worktree { Id = id, Name = "same", Path = "/" + project + "/" + id }
        };

        static LibraryRowTarget Target(int kind)
        {
            switch (kind)
            {
                case 0: return new Item(new LibraryItemInfo { Name = "same" });
                case 1: return new Template(new AgentTemplateInfo { Name = "same" });
                case 2: return new Project(new ProjectInfo { Name = "same" });
                case 3: return new Worktree(Checkout());
                case 4: return new SandboxPreset(new PresetInfo { Name = "same" });
                case 5: return new AppPreset(new CommandInfo { Name = "same" });
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        [Test]
        public void IdenticalNamesKeepCatalogAndCheckoutSelectionsDistinct()
        {
            var keys = Enumerable.Range(0, 6).Select(kind => Target(kind).Key).ToList();
            keys.Add(new Worktree(Checkout("other")).Key);
            keys.Add(new Worktree(Checkout("same", "main")).Key);
            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
            Assert.That(keys.Take(6), Is.EqualTo(new[]
            {
                "item:same", "template:same", "action:project:same",
                "action:worktree:same:branch", "action:sandbox:same", "action:app:same"
            }));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LeftClickSelectsAndRevealsWithoutExecutingActions(int kind)
        {
            var row = Target(kind);
            var effects = new Effects();
            var selection = new LibraryRowSelection();
            Assert.That(selection.Click(row, 0, effects), Is.True);
            Assert.That(selection.Key, Is.EqualTo(row.Key));
            Assert.That(selection.Click(row, 0, effects), Is.True, "repeated selection still reveals the row");
            Assert.That(effects.Calls, Is.Empty);
            Assert.That(effects.Remembered, Is.EqualTo(kind < 2
                ? new[] { ("same", kind == 1), ("same", kind == 1) }
                : Array.Empty<(string, bool)>()));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        public void RightClickOpensSupportedMenusAndPreservesSelectionForPresets(int kind, bool menu)
        {
            var row = Target(kind);
            var effects = new Effects();
            var selection = new LibraryRowSelection();
            selection.Focus("item:previous");
            Assert.That(selection.Click(row, 1, effects), Is.EqualTo(menu));
            Assert.That(selection.Key, Is.EqualTo(menu ? row.Key : "item:previous"));
            Assert.That(effects.Calls.Count, Is.EqualTo(menu ? 1 : 0));
            if (menu) Assert.That(effects.Calls[0].Action, Is.EqualTo("Menu"));
            Assert.That(effects.Remembered, Is.EqualTo(kind < 2
                ? new[] { ("same", kind == 1) } : Array.Empty<(string, bool)>()));
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(3)]
        public void UnsupportedButtonsLeaveSelectionAndEffectsUntouched(int button)
        {
            var effects = new Effects();
            var selection = new LibraryRowSelection();
            selection.Focus("item:previous");
            Assert.That(selection.Click(Target(0), button, effects), Is.False);
            Assert.That(selection.Key, Is.EqualTo("item:previous"));
            Assert.That(effects.Calls, Is.Empty);
            Assert.That(effects.Remembered, Is.Empty);
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        public void FilteringRetainsItemIdentityButClearsHiddenActionSelections(int kind, bool retained)
        {
            var row = Target(kind);
            var selection = new LibraryRowSelection();
            selection.Focus(row.Key);
            Assert.That(selection.Reconcile(new[] { row.Key }), Is.True);
            Assert.That(selection.Reconcile(new[] { "item:unrelated" }), Is.False);
            Assert.That(selection.Key, Is.EqualTo(retained ? row.Key : null));
            Assert.That(selection.Reconcile(Array.Empty<string>()), Is.False);
            Assert.That(selection.Reconcile(new[] { row.Key }), Is.EqualTo(retained));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        public void RefreshClearsOnlyActionSelectionAndRepeatedCleanupIsHarmless(int kind, bool retained)
        {
            var selection = new LibraryRowSelection();
            string key = Target(kind).Key;
            selection.Focus(key);
            selection.ClearAction();
            selection.ClearAction();
            Assert.That(selection.Key, Is.EqualTo(retained ? key : null));
        }

        [TestCase(LibraryItemKind.Prompt, LibraryItemLink.Project, "Run", "Edit", "Run")]
        [TestCase(LibraryItemKind.Shell, LibraryItemLink.Project, "Run", "Edit", "Run")]
        [TestCase(LibraryItemKind.Prompt, LibraryItemLink.Ask, "Choose a project", "Edit", "Run")]
        [TestCase(LibraryItemKind.Shell, LibraryItemLink.Ask, "Choose a project", "Edit", "Run")]
        [TestCase(LibraryItemKind.Breadcrumb, LibraryItemLink.Project, "Edit", "Copy text", "Edit")]
        [TestCase(LibraryItemKind.FileAction, LibraryItemLink.Project, "Edit", null, "Edit")]
        public void ItemDetailsOfferOnlyApplicableActions(LibraryItemKind kind, LibraryItemLink link,
            string primary, string secondary, string request)
        {
            var item = new LibraryItemInfo { Name = "selected", Kind = kind, Link = link, Text = "copy me" };
            var effects = new Effects();
            var actions = new Item(item).DetailsActions(effects);
            Assert.That(effects.Calls, Is.Empty, "building details must not execute actions");
            Assert.That(actions.PrimaryLabel, Is.EqualTo(primary));
            Assert.That(actions.SecondaryLabel, Is.EqualTo(secondary));
            actions.Primary();
            Assert.That(effects.Calls[0], Is.EqualTo(((object)item, request)));
            if (secondary == null) Assert.That(actions.Secondary, Is.Null);
            else
            {
                actions.Secondary();
                Assert.That(effects.Calls[1], Is.EqualTo(((object)item,
                    (kind == LibraryItemKind.Breadcrumb ? "CopyText" : "Edit"))));
            }
            actions.More();
            Assert.That(effects.Calls.Last(), Is.EqualTo(((object)item, "Menu")));
        }

        [Test]
        public void TemplateActionsRetainDefinitionAfterSameNameReplacementAndSelectionChange()
        {
            var original = new AgentTemplateInfo { Name = "same", Description = "original" };
            var replacement = new AgentTemplateInfo { Name = "same", Description = "replacement" };
            var effects = new Effects();
            var actions = new Template(original).DetailsActions(effects);
            var selection = new LibraryRowSelection();
            selection.Click(new Template(replacement), 0, effects);
            Assert.That(actions.PrimaryLabel, Is.EqualTo("Create agent"));
            Assert.That(actions.SecondaryLabel, Is.EqualTo("Edit"));
            actions.Primary();
            actions.Secondary();
            actions.More();
            Assert.That(effects.Calls.Select(call => call.Target), Is.All.SameAs(original));
            Assert.That(effects.Calls.Select(call => call.Action), Is.EqualTo(new string[]
                { "Create", "Edit", "Menu" }));
        }

        [Test]
        public void WorktreeActionsKeepProjectAndCheckoutWhenAnotherRowIsSelected()
        {
            var original = Checkout("first", "branch");
            var effects = new Effects();
            var actions = new Worktree(original).DetailsActions(effects);
            var selection = new LibraryRowSelection();
            selection.Click(new Worktree(Checkout("second", "main")), 0, effects);
            Assert.That(actions.PrimaryLabel, Is.EqualTo("Terminal"));
            Assert.That(actions.SecondaryLabel, Is.EqualTo("Project"));
            actions.Primary();
            actions.Secondary();
            actions.More();
            Assert.That(effects.Calls.Select(call => call.Target), Is.All.SameAs(original));
            Assert.That(effects.Calls.Select(call => call.Action), Is.EqualTo(new string[]
                { "Terminal", "Project", "Menu" }));
            Assert.That(original.Project, Is.EqualTo("first"));
            Assert.That(original.Tree.Id, Is.EqualTo("branch"));
        }

        [Test]
        public void ProjectDetailsRouteEditWorktreesAndMenuToSelectedSnapshot()
        {
            var project = new ProjectInfo { Name = "selected", Dir = "/selected" };
            var effects = new Effects();
            var actions = new Project(project).DetailsActions(effects);
            Assert.That(actions.PrimaryLabel, Is.EqualTo("Edit"));
            Assert.That(actions.SecondaryLabel, Is.EqualTo("Worktrees"));
            actions.Primary();
            actions.Secondary();
            actions.More();
            Assert.That(effects.Calls.Select(call => call.Target), Is.All.SameAs(project));
            Assert.That(effects.Calls.Select(call => call.Action), Is.EqualTo(new string[]
                { "Edit", "Worktrees", "Menu" }));
        }

        [TestCase(false, "user", "Remove")]
        [TestCase(false, "override", "Reset")]
        [TestCase(true, "user", "Remove")]
        [TestCase(true, "override", "Reset")]
        public void PresetActionsKeepCatalogTargetAndDistinguishResetFromRemove(bool app, string source, string label)
        {
            var preset = new PresetInfo { Name = "same", Source = source };
            var command = new CommandInfo { Name = "same", Source = source };
            LibraryRowTarget target = app ? (LibraryRowTarget)new AppPreset(command) : new SandboxPreset(preset);
            var effects = new Effects();
            var actions = target.DetailsActions(effects);
            Assert.That(actions.PrimaryLabel, Is.EqualTo("Edit"));
            Assert.That(actions.SecondaryLabel, Is.EqualTo(label));
            Assert.That(actions.More, Is.Null);
            actions.Primary();
            actions.Secondary();
            object model = app ? (object)command : preset;
            Assert.That(effects.Calls.Select(call => call.Target), Is.All.SameAs(model));
            Assert.That(effects.Calls.Select(call => call.Action), Is.EqualTo(new string[]
                { "Edit", "Remove" }));
        }
    }
}
