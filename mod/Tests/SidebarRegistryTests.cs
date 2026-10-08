using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SidebarRegistryTests
    {
        static SidebarTabDefinition Definition(SidebarTab tab, string name, SidebarTabHandlers handlers = null) =>
            new SidebarTabDefinition(tab, name, "icon", null, true, false, true, handlers ?? new SidebarTabHandlers());

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (bool explicitNull in new[] { false, true })
                yield return ($"sidebar handlers tolerate {(explicitNull ? "explicit null" : "default")} callbacks", () => OptionalHandlers(explicitNull));
            foreach (string kind in new[] { "null", "empty", "null-entry", "duplicate-tab", "duplicate-name", "missing-agents" })
                yield return ($"sidebar registry rejects {kind}", () => InvalidRegistry(kind));
        }

        static void OptionalHandlers(bool explicitNull)
        {
            var handlers = new SidebarTabHandlers();
            if (explicitNull)
            {
                handlers.Draw = handlers.Click = handlers.Refresh = handlers.FilterChanged = null;
                handlers.Close = handlers.Entered = handlers.Reselected = null;
                handlers.DrawActions = null;
                handlers.SetAllFolds = null;
                handlers.AllFolded = null;
            }
            var tab = Definition(SidebarTab.Agents, "agents", handlers);
            Assert.DoesNotThrow(() =>
            {
                tab.Draw(); tab.Click(); tab.DrawActions(default);
                tab.Refresh(); tab.FilterChanged(); tab.SetAllFolds(true); tab.SetAllFolds(false);
                tab.Close(); tab.Entered(); tab.Reselected();
            });
            Assert.That(tab.AllFolded(), Is.False);
            Assert.That(tab.Tooltip, Is.Empty);
            Assert.That(tab.IconKey, Is.EqualTo("icon"));
            Assert.That(tab.HasActions && tab.CanToggleDotfiles, Is.True);
            Assert.That(tab.CanFold, Is.False);
        }

        static void InvalidRegistry(string kind)
        {
            var agents = Definition(SidebarTab.Agents, "agents");
            SidebarTabDefinition[] definitions;
            switch (kind)
            {
                case "null": definitions = null; break;
                case "empty": definitions = Array.Empty<SidebarTabDefinition>(); break;
                case "null-entry": definitions = new[] { agents, null }; break;
                case "duplicate-tab": definitions = new[] { agents, Definition(SidebarTab.Agents, "different") }; break;
                case "duplicate-name": definitions = new[] { agents, Definition(SidebarTab.Files, "agents") }; break;
                default: definitions = new[] { Definition(SidebarTab.Files, "files") }; break;
            }
            var error = Assert.Throws<ArgumentException>(() => new SidebarTabRegistry(definitions));
            Assert.That(error.ParamName, Is.EqualTo("definitions"));
        }

        public static void RegistryCopiesInputAndUsesExactPersistedNames()
        {
            var agents = Definition(SidebarTab.Agents, "agents");
            var files = Definition(SidebarTab.Files, "files");
            var input = new[] { agents, files };
            var registry = new SidebarTabRegistry(input);
            input[0] = files;
            Assert.That(registry.Definitions.ToArray(), Is.EqualTo(new[] { agents, files }), "caller array mutation cannot reorder navigation");
            Assert.That(registry.Definitions, Is.Not.InstanceOf<SidebarTabDefinition[]>());
            var exposed = (IList<SidebarTabDefinition>)registry.Definitions;
            Assert.Throws<NotSupportedException>(() => exposed[0] = files);
            Assert.That(registry.Definitions.ToArray(), Is.EqualTo(new[] { agents, files }), "rejected mutation leaves navigation order intact");
            Assert.That(registry.For(SidebarTab.Agents), Is.SameAs(agents));
            Assert.That(registry.FromPersisted("files"), Is.SameAs(files));
            foreach (string name in new[] { null, "", "FILES", "unknown" })
                Assert.That(registry.FromPersisted(name), Is.SameAs(agents));
        }

        public static void DefinitionRequiresStableIdentityAndHandlers()
        {
            foreach (string empty in new[] { null, "" })
            {
                Assert.That(Assert.Throws<ArgumentException>(() => new SidebarTabDefinition(
                    SidebarTab.Agents, empty, "icon", "", false, false, false, new SidebarTabHandlers())).ParamName, Is.EqualTo("persistedName"));
                Assert.That(Assert.Throws<ArgumentException>(() => new SidebarTabDefinition(
                    SidebarTab.Agents, "agents", empty, "", false, false, false, new SidebarTabHandlers())).ParamName, Is.EqualTo("iconKey"));
            }
            Assert.Throws<ArgumentNullException>(() => new SidebarTabDefinition(
                SidebarTab.Agents, "agents", "icon", "", false, false, false, null));
        }

        public static void DefinitionDispatchesSuppliedHandlersAndFoldState()
        {
            var calls = new List<string>();
            bool folded = false;
            var tab = Definition(SidebarTab.Files, "files", new SidebarTabHandlers
            {
                Draw = () => calls.Add("draw"),
                Click = () => calls.Add("click"),
                DrawActions = _ => calls.Add("actions"),
                Refresh = () => calls.Add("refresh"),
                FilterChanged = () => calls.Add("filter"),
                SetAllFolds = value => folded = value,
                AllFolded = () => folded,
                Close = () => calls.Add("close"),
                Entered = () => calls.Add("enter"),
                Reselected = () => calls.Add("reselect"),
            });
            tab.Draw(); tab.Click(); tab.DrawActions(default); tab.Refresh(); tab.FilterChanged();
            tab.SetAllFolds(true);
            Assert.That(tab.AllFolded(), Is.True);
            tab.SetAllFolds(false);
            Assert.That(tab.AllFolded(), Is.False);
            tab.Close(); tab.Entered(); tab.Reselected();
            Assert.That(calls, Is.EqualTo(new[] { "draw", "click", "actions", "refresh", "filter", "close", "enter", "reselect" }));
        }
    }
}
