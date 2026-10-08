using System;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class SandboxEditorLayoutTests
    {
        public static void Geometry()
        {
            var previousHub = SessionHub.Instance;
            SessionHub.Instance = new SessionHub();
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "global" });
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "one" });
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "two" });
            try
            {
                SeparatorSpacing();
                foreach (string source in new[] { "system", "user", "override" })
                    foreach (float width in new[] { 60f, 240f })
                    {
                        var preset = new PresetInfo
                        {
                            Name = "sample",
                            Source = source,
                            Description = new string('x', 100),
                            Escapes = "host access",
                        };
                        preset.Ro.Add(" /raw/path " + $" ({source}, width {width})");
                        preset.Setenv["A"] = " value ";
                        var page = new SandboxPage();
                        string before = preset.ToJson();
                        EditorTrace.Draws.Clear();
                        EditorTrace.EditValue = "measurement must not edit";
                        float measured = page.TestPreset(preset, width, false);
                        AssertEx.Equal(0, EditorTrace.Draws.Count, "preset measurement invokes no controls" + $" ({source}, width {width})");
                        AssertEx.Equal(before, preset.ToJson(), "preset measurement preserves draft data" + $" ({source}, width {width})");
                        EditorTrace.EditValue = null;
                        AssertEx.Equal(measured, page.TestPreset(preset, width, true),
                            "preset draw and measurement use the same geometry" + $" ({source}, width {width})");
                        var description = EditorTrace.Draws.Single(d => d.Name == "preset.description").Rect;
                        AssertEx.True(description.height <= 240f, "preset description growth stays bounded");
                        AssertEx.True(description.height > 44f, "real form expands wrapped descriptions" + $" ({source}, width {width})");
                        AssertEx.True(EditorTrace.Draws.Where(d => d.Name.StartsWith("preset.", StringComparison.Ordinal))
                            .All(d => d.Rect.width == width && d.Rect.yMax <= measured),
                            "preset fields remain inside the measured extent" + $" ({source}, width {width})");
                        CheckHost(page, preset, null, width);

                        var command = new CommandInfo { Name = "agent", Source = source };
                        before = command.ToJson();
                        EditorTrace.Draws.Clear();
                        EditorTrace.EditValue = "measurement must not edit";
                        measured = page.TestCommand(command, width, false);
                        AssertEx.Equal(0, EditorTrace.Draws.Count, "command measurement invokes no controls" + $" ({source}, width {width})");
                        AssertEx.Equal(before, command.ToJson(), "command measurement preserves draft data" + $" ({source}, width {width})");
                        EditorTrace.EditValue = null;
                        AssertEx.Equal(measured, page.TestCommand(command, width, true),
                            "command draw and measurement use the same geometry" + $" ({source}, width {width})");
                        AssertEx.Equal("check:one,check:two", string.Join(",",
                            EditorTrace.Draws.Where(d => d.Name.StartsWith("check:", StringComparison.Ordinal)).Select(d => d.Name)),
                            "command dependencies retain catalog order and omit global" + $" ({source}, width {width})");
                        CheckHost(page, null, command, width);
                    }

                var newPreset = new PresetInfo { Source = "user" };
                EditorTrace.Draws.Clear();
                new SandboxPage().TestPreset(newPreset, 240f, true, isNew: true);
                AssertEx.True(EditorTrace.Draws.Any(d => d.Name == "preset.name"),
                    "new empty name remains an editable field");
            }
            finally
            {
                EditorTrace.EditValue = null;
                EditorTrace.Draws.Clear();
                SessionHub.Instance = previousHub;
            }
        }

        static void SeparatorSpacing()
        {
            var page = new SandboxPage();
            var preset = new PresetInfo { Name = "sample", Source = "system" };
            EditorTrace.Draws.Clear();
            float bottom = page.TestPreset(preset, 240f, true);
            // Baseline from the original preset form with the test font metrics. The three
            // hairlines sit inside their gaps, including when all optional fields are hidden.
            AssertEx.Equal("136,152,168", string.Join(",",
                EditorTrace.Draws.Where(d => d.Name == "rule").Select(d => d.Rect.y)),
                "preset separators retain the original positions");
            AssertEx.Equal(184f, bottom, "hidden optional fields leave the original action position");
            AssertEx.Equal(1, EditorTrace.Draws.Count(d => d.Name.StartsWith("preset.", StringComparison.Ordinal)),
                "empty system fields are hidden except the nonempty name");

            preset.Source = "user";
            EditorTrace.Draws.Clear();
            bottom = page.TestPreset(preset, 240f, true);
            AssertEx.Equal("238,546,854", string.Join(",",
                EditorTrace.Draws.Where(d => d.Name == "rule").Select(d => d.Rect.y)),
                "editable preset separators include the cache directory editor");
            AssertEx.Equal(1016f, bottom, "editable action position includes the cache directory editor");
            AssertEx.Equal(13, EditorTrace.Draws.Count(d => d.Name.StartsWith("preset.", StringComparison.Ordinal)),
                "editable empty fields remain visible");
        }

        static void CheckHost(SandboxPage page, PresetInfo preset, CommandInfo command, float width)
        {
            EditorTrace.Draws.Clear();
            if (preset != null) page.TestPresetHost(preset, width);
            else page.TestCommandHost(command, width);
            Rect view = EditorTrace.Draws.Single(d => d.Name == "scroll").Rect;
            AssertEx.True(EditorTrace.Draws.All(d => d.Rect.yMax <= view.height),
                "real editor host reserves space for every control");
            string source = preset != null ? preset.Source : command.Source;
            AssertEx.Equal(source == "system", EditorTrace.Draws.Any(d => d.Name == "Copy to user"),
                "only system entries show copy");
            AssertEx.Equal(source != "system", EditorTrace.Draws.Any(d => d.Name == "Save"),
                "only editable entries show save");
            AssertEx.Equal(source == "override", EditorTrace.Draws.Any(d => d.Name == "Reset to system"),
                "overrides retain reset action");
        }
    }
}
