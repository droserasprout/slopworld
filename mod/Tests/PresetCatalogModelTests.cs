using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class PresetCatalogModelTests
    {
        static PresetInfo Preset() => new PresetInfo
        {
            Name = "desktop",
            Description = "Desktop access",
            Source = "override",
            Requires = new List<string> { "base" },
            Ro = new List<string> { "/ro" },
            Rw = new List<string> { "/rw" },
            Dev = new List<string> { "/dev/dri" },
            Env = new List<string> { "DISPLAY" },
            Private = new List<string> { "~/.config" },
            Shared = new List<string> { "~/.config/token" },
            Seed = new List<string> { "/seed" },
            Skip = new List<string> { "/skip" },
            Escapes = "Host display",
            Tmux = true,
            DaemonConfig = true,
            Setenv = new Dictionary<string, string> { { "MODE", "desktop" } },
        };

        public static void PresetCopyPreservesEverySettingAndOwnsItsCollections()
        {
            var original = Preset();
            var copy = original.Copy();
            AssertEx.Equal(original.ToWire(), copy.ToWire(), "all editable fields survive copying");
            AssertEx.Equal("override", copy.Source, "response metadata survives copying");
            var collections = new (string Name, List<string> Original, List<string> Copy)[]
            {
                ("Requires", original.Requires, copy.Requires),
                ("Ro", original.Ro, copy.Ro),
                ("Rw", original.Rw, copy.Rw),
                ("Dev", original.Dev, copy.Dev),
                ("Env", original.Env, copy.Env),
                ("Private", original.Private, copy.Private),
                ("Shared", original.Shared, copy.Shared),
                ("Seed", original.Seed, copy.Seed),
                ("Skip", original.Skip, copy.Skip),
            };
            foreach (var pair in collections)
            {
                string value = pair.Original[0];
                pair.Copy[0] = "changed";
                pair.Copy.Add("added");
                AssertEx.Sequence(new[] { value }, pair.Original, "copy edits leave source " + pair.Name);
                pair.Original.Clear();
                AssertEx.Sequence(new[] { "changed", "added" }, pair.Copy, "source edits leave copy " + pair.Name);
            }
            copy.Setenv["MODE"] = "changed";
            copy.Setenv["NEW"] = "value";
            AssertEx.Equal("desktop", original.Setenv["MODE"], "environment values are independent");
            AssertEx.Equal(1, original.Setenv.Count, "environment additions are independent");
            original.Setenv.Clear();
            AssertEx.Equal(2, copy.Setenv.Count, "source environment edits leave copy intact");
        }

        public static void PresetWireRoundTripPreservesSettingsWithoutWritingSourceMetadata()
        {
            var original = Preset();
            var wire = original.ToWire();
            AssertEx.Equal("", wire.Source, "source is daemon-owned metadata");
            wire.Source = "system";
            var decoded = PresetInfo.FromWire(wire);
            AssertEx.Equal("system", decoded.Source, "reads daemon source metadata");
            AssertEx.Equal(original.ToWire(), decoded.ToWire(), "all editable fields round-trip");
            wire.Ro.Clear();
            wire.Setenv.Clear();
            AssertEx.Sequence(new[] { "/ro" }, decoded.Ro, "decoded paths own their storage");
            AssertEx.Equal("desktop", decoded.Setenv["MODE"], "decoded environment owns its storage");
            AssertEx.Sequence(new[] { "/ro" }, original.Ro, "wire edits leave original paths intact");
            AssertEx.Equal("desktop", original.Setenv["MODE"], "wire edits leave original environment intact");
        }

        public static void PresetTooltipShowsGrantedAccessAndGeneratedBinds()
        {
            var preset = Preset();
            AssertEx.True(preset.IsEscape, "host access is flagged");
            AssertEx.Sequence(new[] { "SlopWorld tmux socket", "SlopWorld daemon config (read-only)",
                "/ro", "/rw", "/dev/dri", "~/.config/token", "~/.config", "DISPLAY", "MODE=desktop" },
                preset.Gives, "tooltip groups access and omits seed/skip rules");
            preset.Gives.Clear();
            AssertEx.Equal(9, preset.Gives.Count, "tooltip results do not mutate the preset");
            var empty = new PresetInfo();
            AssertEx.False(empty.IsEscape, "default has no escape");
            empty.Escapes = null;
            AssertEx.False(empty.IsEscape, "null escape is absent");
            AssertEx.Equal(0, empty.Gives.Count, "disabled generated binds are absent");
        }

        public static void CommandCopyAndWireConversionPreserveSettingsWithoutAliasing()
        {
            var original = new CommandInfo
            {
                Name = "shell",
                Kind = CommandInfo.ShellKind,
                Description = "Interactive shell",
                Source = "user",
                Cmd = "bash -l",
                Sandbox = new List<string> { "base", "desktop" },
            };
            var copy = original.Copy();
            AssertEx.Equal(original.ToWire(), copy.ToWire(), "command settings survive copying");
            AssertEx.Equal("user", copy.Source, "copy retains source metadata");
            copy.Sandbox.Clear();
            AssertEx.Sequence(new[] { "base", "desktop" }, original.Sandbox, "editing copy keeps original dependencies");
            var wire = original.ToWire();
            AssertEx.Equal("", wire.Source, "writes omit daemon-owned source");
            wire.Source = "override";
            var decoded = CommandInfo.FromWire(wire);
            AssertEx.Equal("override", decoded.Source, "reads source metadata");
            AssertEx.Equal(original.ToWire(), decoded.ToWire(), "command settings round-trip");
            wire.Sandbox.Clear();
            AssertEx.Sequence(new[] { "base", "desktop" }, decoded.Sandbox, "decoded dependencies own their storage");
            AssertEx.Sequence(new[] { "base", "desktop" }, original.Sandbox, "wire dependencies own their storage");
        }
    }
}
