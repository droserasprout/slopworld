using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    // The existing test bodies remain small, static policies. This source turns each
    // inventory entry into an independent NUnit test case without duplicating them.
    [TestFixture]
    [NonParallelizable]
    public sealed class NUnitTestHarness
    {
        [TestCaseSource(nameof(AllCases))]
        public void ExistingCase(Action body)
        {
            body();
        }

        public static IEnumerable<TestCaseData> AllCases()
        {
            foreach (var type in TestTypes())
            {
                var cases = type.GetMethod("Cases", BindingFlags.Public | BindingFlags.Static,
                                           null, Type.EmptyTypes, null);
                if (cases != null)
                {
                    foreach (var item in (IEnumerable)cases.Invoke(null, null))
                    {
                        var tuple = (ITuple)item;
                        var name = (string)tuple[0];
                        var body = (Action)tuple[1];
                        yield return new TestCaseData(body).SetName($"{Label(type)}: {name}");
                    }
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static |
                                                        BindingFlags.DeclaredOnly)
                    .Where(method => method.Name != "Cases" &&
                                     method.ReturnType == typeof(void) &&
                                     method.GetParameters().Length == 0)
                    .OrderBy(method => method.MetadataToken))
                {
                    yield return new TestCaseData((Action)method.CreateDelegate(typeof(Action)))
                        .SetName(NamedLabel(type, method));
                }
            }
        }

        static IEnumerable<Type> TestTypes() =>
            typeof(NUnitTestHarness).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(NUnitTestHarness).Namespace &&
                               type.Name.EndsWith("Tests", StringComparison.Ordinal))
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

        static readonly IReadOnlyDictionary<string, string> MethodLabels = new Dictionary<string, string>
        {
            ["FramePolicyTests.Transitions"] = "FramePolicy: focus and settings transitions",
            ["IdleWorkTests.Messages"] = "Idle work: message ordering and bounded batches",
            ["IdleWorkTests.Scheduling"] = "Idle work: elapsed-time scheduling",
            ["IdleWorkTests.Titles"] = "Idle work: sidebar title invalidation",
            ["EcoWorkTests.Maintenance"] = "Eco: maintenance transitions",
            ["EcoWorkTests.Membership"] = "Eco: colony membership revisions",
            ["UsageReadoutTests.Usage"] = "Usage readout: authoritative snapshot polling",
            ["UsageReadoutTests.Clock"] = "Usage readout: clock boundaries and locale",
            ["UiMetricsTests.Values"] = "UI metrics: density and font floors",
            ["UiMetricsTests.Invalidation"] = "UI metrics: invalidation channels",
            ["ScrollableGeometryTests.Policies"] = "Scrollable geometry: named reservation policy",
            ["SandboxLayoutTests.Placement"] = "Sandbox layout: master/detail geometry",
            ["SandboxEditorLayoutTests.Geometry"] = "Sandbox editor layout: rows and visibility",
            ["SettingsLayoutTests.Bounds"] = "Settings layout: bounded page and footer",
            ["SettingsLayoutTests.Measurement"] = "Settings layout: frame-stable content height",
            ["WorkspacePanelTests.Lifecycle"] = "Workspace panels: ownership and focus lifecycle",
            ["WorkspacePanelTests.Geometry"] = "Terminal panels: independent geometry",
            ["WorkspacePanelTests.SplitLifecycle"] = "Workspace split: focus and retention",
            ["WorkspacePanelTests.SplitGeometry"] = "Workspace split: bounded geometry",
            ["FieldFocusTests.Availability"] = "Field focus: live form changes",
            ["FieldFocusTests.Restoration"] = "Field focus: restoration and owner isolation",
        };

        static string Label(Type type)
        {
            var name = type.Name.Substring(0, type.Name.Length - "Tests".Length);
            switch (name)
            {
                case "Json": return "JVal";
                case "Toml": return "TOML";
                default: return name;
            }
        }

        static string NamedLabel(Type type, MethodInfo method) =>
            MethodLabels.TryGetValue(type.Name + "." + method.Name, out var label)
                ? label : Label(type) + ": " + method.Name;
    }
}
