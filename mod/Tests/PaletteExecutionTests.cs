using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        [TestFixture]
        [NonParallelizable]
        public sealed partial class PaletteExecutionTests
        {
            CommandPalette palette;
            int calls;
            string value;
            string profile,
                previousProfile,
                previousHistory,
                previousFocus;
            SessionHub previousHub;
            Event previousEvent;
            object[] previousWindows;

            [SetUp]
            public void SetUp()
            {
                previousProfile = Verse.GenFilePaths.SaveDataFolderPath;
                profile = Path.Combine(Path.GetTempPath(), "slop-palette-" + Guid.NewGuid());
                Verse.GenFilePaths.SaveDataFolderPath = profile;
                previousHistory = Settings.S.commandPaletteHistory;
                previousHub = SessionHub.Instance;
                previousEvent = Event.current;
                previousFocus = GUI.FocusedControl;
                previousWindows = Verse.Find.WindowStack.ToArray();
                CommandTable.Clear();
                History.Load(null, Array.Empty<string>());
                _recentLoaded = false;
                Settings.S.commandPaletteHistory = "";
                SessionHub.Instance = new SessionHub();
                Verse.Find.WindowStack.Clear();
                UiText.PaletteEdit = null;
                GUI.FocusedControl = null;
                calls = 0;
                value = null;
                palette = null;
            }

            [TearDown]
            public void TearDown()
            {
                CommandTable.Clear();
                History.Load(null, Array.Empty<string>());
                _recentLoaded = false;
                Settings.S.commandPaletteHistory = previousHistory;
                SessionHub.Instance = previousHub;
                Event.current = previousEvent;
                GUI.FocusedControl = previousFocus;
                UiText.PaletteEdit = null;
                Verse.Find.WindowStack.Clear();
                Verse.Find.WindowStack.AddRange(previousWindows);
                Verse.GenFilePaths.SaveDataFolderPath = previousProfile;
                if (Directory.Exists(profile))
                    Directory.Delete(profile, true);
            }

            void Open(params CommandDef[] commands)
            {
                CommandTable.AddRange(commands);
                palette = new CommandPalette { _listH = 72 };
            }

            CommandDef Action(string id = "run", Func<bool> enabled = null) =>
                new CommandDef(
                    id,
                    id,
                    "Actions",
                    v =>
                    {
                        calls++;
                        value = v;
                    },
                    enabled
                );

            CommandDef Menu(Func<List<SubOption>> options) =>
                new CommandDef(
                    "menu",
                    "Choose…",
                    "Actions",
                    v =>
                    {
                        calls++;
                        value = v;
                    },
                    subAction: options
                );

            void Key(KeyCode key)
            {
                var e = new Event { type = EventType.KeyDown, keyCode = key };
                Assert.That(palette.HandleNavigation(e), Is.True);
                Assert.That(e.type, Is.EqualTo(EventType.Used));
            }

            [Test]
            public void ActionRunsOnceClosesAndPersistsRecency()
            {
                Open(Action());
                palette.ExecuteSelected();
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(value, Is.Null);
                Assert.That(palette.Closed, Is.True);
                Assert.That(Settings.CommandPaletteHistory, Is.EqualTo("run"));
            }

            [Test]
            public void AvailabilityIsRecheckedAfterSearchResultsWereBuilt()
            {
                bool enabled = true;
                Open(Action(enabled: () => enabled));
                enabled = false;
                palette.ExecuteSelected();
                Assert.That(calls, Is.Zero);
                Assert.That(palette.Closed, Is.False);
                Assert.That(History.Entries, Is.Empty);
                palette.RebuildMatches();
                Assert.That(palette._matches, Is.Empty);
            }

            [Test]
            public void DisabledMenuDoesNotCallItsProvider()
            {
                var command = new CommandDef(
                    "menu",
                    "Menu",
                    "A",
                    _ => calls++,
                    () => false,
                    () => throw new InvalidOperationException("disabled provider")
                );
                Open(command);
                palette.Execute(command);
                Assert.That(palette._mode, Is.EqualTo(Mode.Commands));
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void EmptyAndOutOfRangeSelectionsAreSafeAfterPaging()
            {
                Open();
                Key(KeyCode.PageDown);
                Key(KeyCode.Return);
                Key(KeyCode.UpArrow);
                Key(KeyCode.DownArrow);
                Key(KeyCode.PageUp);
                palette._selectedIndex = 20;
                palette.ExecuteSelected();
                Assert.That(palette.Closed, Is.False);
                palette.EnterSub(Menu(() => new List<SubOption>()));
                foreach (
                    var key in new[]
                    {
                        KeyCode.PageDown,
                        KeyCode.Return,
                        KeyCode.UpArrow,
                        KeyCode.DownArrow,
                        KeyCode.PageUp,
                    }
                )
                    Key(key);
                palette._subIndex = -1;
                palette.ToggleSub();
                palette._subIndex = 20;
                palette.ExecuteSub();
                Assert.That(calls, Is.Zero);
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void DisabledOptionsNeitherExecuteNorToggle()
            {
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption
                            {
                                Label = "Unavailable",
                                Enabled = false,
                                Checked = false,
                            },
                        }
                    )
                );
                palette.ExecuteSelected();
                Assert.That(palette.Checklist, Is.True);
                palette.ExecuteSub();
                palette.ToggleSub();
                Assert.That(calls, Is.Zero);
                Assert.That(palette.Closed, Is.False);
                Assert.That(History.Entries, Is.Empty);
            }

            [Test]
            public void LeafCallbackOverridesCommandActionAndTracksRootCommand()
            {
                int selected = 0;
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption
                            {
                                Label = "Leaf",
                                Value = "x",
                                Select = () => selected++,
                            },
                        }
                    )
                );
                palette.ExecuteSelected();
                Assert.That(palette.Checklist, Is.False);
                palette.ToggleSub();
                palette.ExecuteSub();
                Assert.That(selected, Is.EqualTo(1));
                Assert.That(calls, Is.Zero);
                Assert.That(palette.Closed, Is.True);
                Assert.That(History.Entries, Is.EqualTo(new[] { "menu" }));
            }

            [Test]
            public void PlainOptionPassesItsValueToCommand()
            {
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption { Label = "Leaf", Value = "identity" },
                        }
                    )
                );
                palette.ExecuteSelected();
                Key(KeyCode.KeypadEnter);
                Assert.That(value, Is.EqualTo("identity"));
                Assert.That(calls, Is.EqualTo(1));
            }

            [Test]
            public void ChecklistRefreshesAuthoritativeOptionsAndClampsSelection()
            {
                int loads = 0;
                Open(
                    Menu(() =>
                        ++loads == 1
                            ? new List<SubOption>
                            {
                                new SubOption { Label = "All", Checked = true },
                                new SubOption
                                {
                                    Label = "Project",
                                    Value = "p",
                                    Checked = false,
                                },
                            }
                            : new List<SubOption>
                            {
                                new SubOption
                                {
                                    Label = "Project",
                                    Value = "p",
                                    Checked = true,
                                },
                            }
                    )
                );
                palette.ExecuteSelected();
                palette._subIndex = 1;
                palette.ToggleSub();
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(value, Is.EqualTo("p"));
                Assert.That(loads, Is.EqualTo(2));
                Assert.That(palette._subIndex, Is.Zero);
                Assert.That(palette._subShown.Single().O.Checked, Is.True);
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void NestedBackRestoresParentQuerySelectionAndCommandQuery()
            {
                var children = new List<SubOption>
                {
                    new SubOption { Label = "Child", Value = "c" },
                };
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption { Label = "Parent one", Children = () => children },
                            new SubOption { Label = "Parent two", Children = () => children },
                        }
                    )
                );
                palette._filter = "choose";
                palette.ExecuteSelected();
                palette._subFilter = "parent";
                palette._subHasFilter = true;
                palette.RebuildSub();
                palette._subIndex = 1;
                palette.ExecuteSub();
                Assert.That(palette._subPrompt, Is.EqualTo("Choose › Parent two:"));
                Assert.That(palette._subFilter, Is.Empty);
                Assert.That(palette._subStack.Count, Is.EqualTo(1));
                Assert.That(History.Entries, Is.Empty);
                Key(KeyCode.Escape);
                Assert.That(palette._subFilter, Is.EqualTo("parent"));
                Assert.That(palette._subHasFilter, Is.True);
                Assert.That(palette._subIndex, Is.EqualTo(1));
                Assert.That(palette._subPrompt, Is.EqualTo("Choose:"));
                Key(KeyCode.Escape);
                Assert.That(palette._mode, Is.EqualTo(Mode.Commands));
                Assert.That(palette._filter, Is.EqualTo("choose"));
                Assert.That(palette._subStack, Is.Empty);
                Assert.That(palette.Closed, Is.False);
                Key(KeyCode.Escape);
                Assert.That(palette.Closed, Is.True);
            }

            [TestCase(false)]
            [TestCase(true)]
            public void EmptyChildProviderLeavesParentIntact(bool nullChildren)
            {
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption
                            {
                                Label = "Parent",
                                Children = () => nullChildren ? null : new List<SubOption>(),
                            },
                        }
                    )
                );
                palette.ExecuteSelected();
                palette.ExecuteSub();
                Assert.That(palette._subStack, Is.Empty);
                Assert.That(palette._subPrompt, Is.EqualTo("Choose:"));
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void AgentActionResolvesReplacementAndIgnoresRemovedTarget()
            {
                SessionInfo received = null;
                var command = CommandDef.ForAgent(
                    "agent",
                    "Agent",
                    () => new List<SubOption>(),
                    x => received = x
                );
                var old = new SessionInfo { Name = "agent" };
                var replacement = new SessionInfo { Name = "agent" };
                SessionHub.Instance.Sessions.Add("agent", old);
                Open(command);
                SessionHub.Instance.Sessions["agent"] = replacement;
                command.Action("agent");
                Assert.That(received, Is.SameAs(replacement));
                received = null;
                SessionHub.Instance.Sessions.Clear();
                command.Action("agent");
                command.Action(null);
                Assert.That(received, Is.Null);
            }

            [Test]
            public void TaskActionResolvesLatestSnapshotAndIgnoresMissingId()
            {
                TaskInfo received = null;
                var command = CommandDef.ForTask(
                    "task",
                    "Task",
                    () => new List<SubOption>(),
                    x => received = x
                );
                SessionHub.Instance.Tasks.Add(null);
                SessionHub.Instance.Tasks.Add(new TaskInfo { Id = "t" });
                var latest = new TaskInfo { Id = "t" };
                SessionHub.Instance.Tasks[1] = latest;
                command.Action("t");
                Assert.That(received, Is.SameAs(latest));
                received = null;
                command.Action("gone");
                Assert.That(received, Is.Null);
            }

            [Test]
            public void ArrowNavigationWrapsAndPagingClampsInBothModes()
            {
                Open(Enumerable.Range(0, 8).Select(i => Action("a" + i)).ToArray());
                Key(KeyCode.UpArrow);
                Assert.That(palette._selectedIndex, Is.EqualTo(7));
                Key(KeyCode.DownArrow);
                Assert.That(palette._selectedIndex, Is.Zero);
                Key(KeyCode.PageDown);
                Assert.That(palette._selectedIndex, Is.EqualTo(3));
                Key(KeyCode.PageUp);
                Assert.That(palette._selectedIndex, Is.Zero);
                palette.EnterSub(
                    Menu(() =>
                        Enumerable
                            .Range(0, 8)
                            .Select(i => new SubOption { Label = "row" + i })
                            .ToList()
                    )
                );
                Key(KeyCode.UpArrow);
                Assert.That(palette._subIndex, Is.EqualTo(7));
                Key(KeyCode.DownArrow);
                Assert.That(palette._subIndex, Is.Zero);
                Key(KeyCode.PageDown);
                Assert.That(palette._subIndex, Is.EqualTo(3));
                Key(KeyCode.PageUp);
                Assert.That(palette._subIndex, Is.Zero);
                palette._listH = 0;
                Key(KeyCode.PageDown);
                Assert.That(palette._subIndex, Is.EqualTo(1));
                Assert.That(palette.HandleNavigation(new Event { keyCode = KeyCode.A }), Is.False);
            }

            [Test]
            public void SearchEditResetsSelectionAndEmptyBackspaceReturnsToParent()
            {
                Open(Action("first"), Action("second"));
                palette._selectedIndex = 1;
                UiText.PaletteEdit = "FIRST";
                palette.DrawCommandInput(new Rect());
                Assert.That(palette._selectedIndex, Is.Zero);
                Assert.That(palette._filter, Is.EqualTo("first"));
                Assert.That(palette._matches.Single().Command.Id, Is.EqualTo("first"));
                palette.EnterSub(
                    Menu(() => new List<SubOption> { new SubOption { Label = "child" } })
                );
                UiText.PaletteEdit = "none";
                var e = new Event { type = EventType.KeyDown, keyCode = KeyCode.A };
                palette.DrawSubInput(new Rect(), e, true);
                Assert.That(palette._subShown, Is.Empty);
                Assert.That(palette._subHasFilter, Is.True);
                UiText.PaletteEdit = "";
                e.keyCode = KeyCode.Backspace;
                Assert.That(
                    palette.DrawSubInput(new Rect(), e, true),
                    Is.False,
                    "clearing the last character stays in the submenu"
                );
                Assert.That(palette.DrawSubInput(new Rect(), e, true), Is.True);
                Assert.That(palette._mode, Is.EqualTo(Mode.Commands));
            }

            [Test]
            public void ChecklistSpaceKeyTogglesButCharacterEventDoesNotToggleTwice()
            {
                Open(
                    Menu(() =>
                        new List<SubOption>
                        {
                            new SubOption { Label = "Box", Checked = false },
                        }
                    )
                );
                palette.ExecuteSelected();
                Event.current = new Event
                {
                    type = EventType.KeyDown,
                    keyCode = KeyCode.Space,
                    character = ' ',
                };
                palette.DrawInput(new Rect());
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
                Event.current = new Event
                {
                    type = EventType.KeyDown,
                    keyCode = KeyCode.None,
                    character = ' ',
                };
                palette.DrawInput(new Rect());
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(palette._subFilter, Is.Empty);
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void ToggleRefreshesCatalogsOnOpenAndClosesExistingWindowWithoutMoreRequests()
            {
                var previousRequests = DaemonClient.Requests.ToArray();
                DaemonClient.Requests.Clear();
                try
                {
                    CommandTable.Add(Action());
                    Toggle();
                    var opened = Verse.Find.WindowStack.OfType<CommandPalette>().Single();
                    Assert.That(opened.Closed, Is.False);
                    Assert.That(
                        DaemonClient.Requests.Count,
                        Is.EqualTo(5),
                        "tasks and four catalogs refresh on open"
                    );
                    Toggle();
                    Assert.That(opened.Closed, Is.True);
                    Assert.That(Verse.Find.WindowStack, Is.Empty);
                    Assert.That(DaemonClient.Requests.Count, Is.EqualTo(5));
                }
                finally
                {
                    DaemonClient.Requests.Clear();
                    DaemonClient.Requests.AddRange(previousRequests);
                }
            }

            [Test]
            public void CreationAdapterSkipsSeparatorsAndLoadsChildrenLazily()
            {
                int loads = 0;
                var leaf = new CreationActions.Entry { Label = "Leaf", Select = () => calls++ };
                var root = new CreationActions.Entry
                {
                    Label = "Root",
                    Children = () =>
                    {
                        loads++;
                        return new List<CreationActions.Entry>
                        {
                            CreationActions.Entry.Separator,
                            new CreationActions.Entry
                            {
                                Label = "Branch",
                                Children = () => new List<CreationActions.Entry> { leaf },
                            },
                        };
                    },
                };
                var command = CommandDef.ForCreation("create", "Create…", "Create", root);
                Open(command);
                Assert.That(loads, Is.Zero);
                palette.ExecuteSelected();
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(palette._subShown.Count, Is.EqualTo(1));
                palette.ExecuteSub();
                palette.ExecuteSub();
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(palette.Closed, Is.True);
                Assert.That(History.Entries, Is.EqualTo(new[] { "create" }));
                root.Enabled = false;
                Assert.That(command.IsEnabled, Is.False);
                var direct = CommandDef.ForCreation("leaf", "Leaf", "Create", leaf);
                direct.Action(null);
                Assert.That(calls, Is.EqualTo(2));
                leaf.Select = null;
                direct.Action(null);
                Assert.That(calls, Is.EqualTo(2));
            }

            [Test]
            public void EntityFactoriesUseCurrentProjectAndLibraryCatalogs()
            {
                ProjectInfo project = null;
                LibraryItemInfo item = null;
                var projectCommand = CommandDef.ForProject(
                    "p",
                    "Project",
                    () => new List<SubOption>(),
                    p => project = p
                );
                var libraryCommand = CommandDef.ForLibraryItem(
                    "l",
                    "Library",
                    () => new List<SubOption>(),
                    l => item = l
                );
                projectCommand.Action("p");
                Assert.That(project, Is.Not.Null);
                var entry = new LibraryItemInfo { Name = "entry" };
                SessionHub.Instance.Catalog.Library.Add(entry);
                libraryCommand.Action("entry");
                Assert.That(item, Is.SameAs(entry));
                item = null;
                SessionHub.Instance.Catalog.Library.Clear();
                libraryCommand.Action("entry");
                Assert.That(item, Is.Null);
            }

            [Test]
            public void RecentHistoryLoadsOnceAndGroupsRecentRowsAheadOfCatalog()
            {
                Settings.S.commandPaletteHistory = "second\nmissing";
                Open(Action("first"), Action("second"));
                Assert.That(palette._matches[0].Command.Id, Is.EqualTo("second"));
                Assert.That(palette.GroupOf(0), Is.EqualTo("Recently"));
                Assert.That(palette.GroupOf(1), Is.EqualTo("Actions"));
                Settings.S.commandPaletteHistory = "first";
                _ = new CommandPalette();
                Assert.That(History.Entries, Is.EqualTo(new[] { "second" }));
                TrackRecent("second");
                Assert.That(
                    Settings.CommandPaletteHistory,
                    Is.EqualTo("first"),
                    "unchanged recency does not write settings"
                );
            }

            [Test]
            public void WindowPositionUsesScreenSizeAndMinimumTopOffset()
            {
                Open(Action());
                float oldWidth = Verse.UI.screenWidth,
                    oldHeight = Verse.UI.screenHeight;
                try
                {
                    Verse.UI.screenWidth = 1200;
                    Verse.UI.screenHeight = 1000;
                    palette.SetInitialSizeAndPosition();
                    Assert.That(palette.windowRect.x, Is.EqualTo(340));
                    Assert.That(palette.windowRect.y, Is.EqualTo(80));
                    Verse.UI.screenHeight = 100;
                    palette.SetInitialSizeAndPosition();
                    Assert.That(palette.windowRect.y, Is.EqualTo(60));
                    Assert.That(palette.Margin, Is.Zero);
                    Assert.That(palette.preventCameraMotion, Is.True);
                }
                finally
                {
                    Verse.UI.screenWidth = oldWidth;
                    Verse.UI.screenHeight = oldHeight;
                }
            }

            [Test]
            public void DrawInputRoutesEnterAndEmptyBackspaceBeforeRestoringFocus()
            {
                Open(Menu(() => new List<SubOption> { new SubOption { Label = "Leaf" } }));
                Event.current = new Event { type = EventType.KeyDown, keyCode = KeyCode.Return };
                palette.DrawInput(new Rect());
                Assert.That(palette._mode, Is.EqualTo(Mode.Sub));
                Event.current = new Event { type = EventType.Repaint };
                palette.DrawInput(new Rect());
                Assert.That(GUI.FocusedControl, Is.EqualTo("paletteInput"));
                Event.current = new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace };
                palette.DrawInput(new Rect());
                Assert.That(palette._mode, Is.EqualTo(Mode.Commands));
                Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
            }

            [Test]
            public void InputFocusAndHeightAreBoundedForLargeAndEmptyLists()
            {
                Open(Enumerable.Range(0, 40).Select(i => Action("a" + i)).ToArray());
                Assert.That(palette.InitialSize.y, Is.EqualTo(MaxH));
                Event.current = new Event { type = EventType.Repaint };
                palette.DrawInput(new Rect());
                Assert.That(GUI.FocusedControl, Is.EqualTo("paletteInput"));
                Assert.That(palette._focusInput, Is.False);
                palette._filter = "missing";
                palette.RebuildMatches();
                Assert.That(
                    palette.ContentHeight(),
                    Is.EqualTo(Pad * 2 + InputH + UiTheme.GapXS + RowH)
                );
                palette.EnterSub(
                    Menu(() =>
                        Enumerable
                            .Range(0, 30)
                            .Select(i => new SubOption { Label = "r" + i })
                            .ToList()
                    )
                );
                Assert.That(
                    palette.ContentHeight(),
                    Is.EqualTo(Pad * 2 + InputH + UiTheme.GapXS + RowH * 10)
                );
            }
        }
    }
}
