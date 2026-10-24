using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SidebarRegistryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("unbound tabs have safe default callbacks", OptionalCallbacks);
            yield return ("callbacks bind once in fixed navigation order", BindCallbacks);
        }

        static void OptionalCallbacks()
        {
            foreach (var tab in new SidebarTabRegistry().Definitions)
            {
                tab.Draw();
                tab.Click();
                tab.DrawActions(default);
                tab.Refresh();
                tab.FilterChanged();
                tab.SetAllFolds(true);
                tab.SetAllFolds(false);
                tab.Close();
                tab.Entered();
                tab.Reselected();
                Assert.That(tab.AllFolded(), Is.False);
            }
        }

        static void BindCallbacks()
        {
            var bound = new List<SidebarTabDefinition>();
            var registry = new SidebarTabRegistry(bound.Add);
            Assert.That(bound, Is.EqualTo(registry.Definitions));
            foreach (var definition in bound)
                Assert.That(registry.For(definition.Tab), Is.SameAs(definition));
        }

        public static void FixedCatalogUsesExactPersistedNames()
        {
            var registry = new SidebarTabRegistry();
            var agents = registry.For(SidebarTab.Agents);
            var files = registry.For(SidebarTab.Files);
            var expected = new[] { "agents", "files", "git", "search", "tasks", "library" };
            Assert.That(
                registry.Definitions.Select(tab => tab.PersistedName),
                Is.EqualTo(expected)
            );
            var exposed = (IList<SidebarTabDefinition>)registry.Definitions;
            Assert.Throws<NotSupportedException>(() => exposed[0] = files);
            Assert.That(
                registry.Definitions.Select(tab => tab.PersistedName),
                Is.EqualTo(expected)
            );
            Assert.That(registry.FromPersisted("files"), Is.SameAs(files));
            Assert.That(registry.For((SidebarTab)(-1)), Is.SameAs(agents));
            foreach (string name in new[] { null, "", "FILES", "unknown", " files", "files " })
                Assert.That(registry.FromPersisted(name), Is.SameAs(agents));
        }

        public static void FixedCatalogCapabilities()
        {
            var registry = new SidebarTabRegistry();
            foreach (var tab in registry.Definitions)
            {
                Assert.That(tab.IconKey, Is.EqualTo(tab.PersistedName));
                Assert.That(tab.Tooltip, Is.Not.Empty);
                Assert.That(tab.HasActions, Is.True);
                Assert.That(tab.CanFold, Is.EqualTo(tab.Tab != SidebarTab.Tasks));
                Assert.That(tab.CanToggleDotfiles, Is.EqualTo(tab.Tab == SidebarTab.Files));
            }
        }

        public static void DefinitionDispatchesSuppliedHandlersAndFoldState()
        {
            var calls = new List<string>();
            bool folded = false;
            var registry = new SidebarTabRegistry(tab =>
            {
                tab.Draw = () => calls.Add("draw");
                tab.Click = () => calls.Add("click");
                tab.DrawActions = _ => calls.Add("actions");
                tab.Refresh = () => calls.Add("refresh");
                tab.FilterChanged = () => calls.Add("filter");
                tab.SetAllFolds = value => folded = value;
                tab.AllFolded = () => folded;
                tab.Close = () => calls.Add("close");
                tab.Entered = () => calls.Add("enter");
                tab.Reselected = () => calls.Add("reselect");
            });
            var definition = registry.For(SidebarTab.Files);
            definition.Draw();
            definition.Click();
            definition.DrawActions(default);
            definition.Refresh();
            definition.FilterChanged();
            definition.SetAllFolds(true);
            Assert.That(definition.AllFolded(), Is.True);
            definition.SetAllFolds(false);
            Assert.That(definition.AllFolded(), Is.False);
            definition.Close();
            definition.Entered();
            definition.Reselected();
            Assert.That(
                calls,
                Is.EqualTo(
                    new[]
                    {
                        "draw",
                        "click",
                        "actions",
                        "refresh",
                        "filter",
                        "close",
                        "enter",
                        "reselect",
                    }
                )
            );
        }
    }
}
