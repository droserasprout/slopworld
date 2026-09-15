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

        static string Label(Type type)
        {
            var name = type.Name.Substring(0, type.Name.Length - "Tests".Length);
            return name == "Json" ? "JVal" : name == "Toml" ? "TOML" : name == "ModBugfix"
                ? "Mod bugfix" : name;
        }

        static string NamedLabel(Type type, MethodInfo method)
        {
            var key = type.Name + "." + method.Name;
            switch (key)
            {
                case "ModSettingsTests.Persistence": return "ModSettings: persistence";
                case "FramePolicyTests.Transitions": return "FramePolicy: focus and settings transitions";
                case "IdleWorkTests.Messages": return "Idle work: message ordering and bounded batches";
                case "IdleWorkTests.Scheduling": return "Idle work: elapsed-time scheduling";
                case "IdleWorkTests.Titles": return "Idle work: sidebar title invalidation";
                case "EcoWorkTests.Maintenance": return "Eco: maintenance transitions";
                case "EcoWorkTests.Membership": return "Eco: colony membership revisions";
                case "EcoWorkTests.Usage": return "Eco: usage snapshot and settings invalidation";
                case "EcoWorkTests.Clock": return "Eco: clock boundaries and locale";
                case "HubCatalogTests.Ordering": return "HubCatalog: ordering";
                case "WorkspaceLayoutTests.Geometry": return "Workspace: geometry";
                case "UiMetricsTests.Values": return "UI metrics: density and font floors";
                case "UiMetricsTests.Invalidation": return "UI metrics: invalidation channels";
                case "UiCompositionTests.Arrange": return "UI composition: measure and arrange";
                case "ScrollableGeometryTests.Policies": return "Scrollable geometry: named reservation policy";
                case "SandboxLayoutTests.Placement": return "Sandbox layout: master/detail geometry";
                case "SandboxEditorLayoutTests.Geometry": return "Sandbox editor layout: rows and visibility";
                case "SettingsLayoutTests.Bounds": return "Settings layout: bounded page and footer";
                case "SettingsLayoutTests.Measurement": return "Settings layout: frame-stable content height";
                case "WorkspacePanelTests.Lifecycle": return "Workspace panels: ownership and focus lifecycle";
                case "WorkspacePanelTests.Geometry": return "Terminal panels: independent geometry";
                case "WorkspacePanelTests.SplitLifecycle": return "Workspace split: focus and retention";
                case "WorkspacePanelTests.SplitGeometry": return "Workspace split: bounded geometry";
                case "FieldFocusTests.Availability": return "Field focus: live form changes";
                case "FieldFocusTests.Restoration": return "Field focus: restoration and owner isolation";
                default: return Label(type) + ": " + method.Name;
            }
        }
    }
}
