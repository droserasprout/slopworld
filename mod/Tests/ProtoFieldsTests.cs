using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class ProtoFieldsTests
    {
        public static void SparseEditsCreateNestedMessagesAndEscapeMapKeys()
        {
            const string key = "provider.v1~custom";
            string path = "daemon.usage_items." + ProtoFields.Escape(key);
            Assert.That(ProtoFields.Escape(key), Is.EqualTo("provider~1v1~0custom"));
            var source = new Wire.EditableConfig();
            var changed = ProtoFields.Apply(
                source,
                new Dictionary<string, object>
                {
                    [path + ".poll"] = true,
                    [path + ".interval_secs"] = 0UL,
                    ["daemon.instructions.worker_prompt"] = "prompt",
                    ["commands.pager"] = "less",
                }
            );
            Assert.That(source.Daemon, Is.Null, "apply does not mutate baseline");
            Assert.That(source.Commands, Is.Null);
            Assert.That(changed.Daemon.UsageItems[key].Poll, Is.True);
            Assert.That(
                changed.Daemon.UsageItems[key].HasIntervalSecs,
                Is.True,
                "explicit zero preserves presence"
            );
            Assert.That(changed.Daemon.Instructions.WorkerPrompt, Is.EqualTo("prompt"));
            Assert.That(changed.Commands.Pager, Is.EqualTo("less"));
            Assert.That(ProtoFields.ValueAt(changed, path + ".interval_secs"), Is.EqualTo(0UL));
            Assert.That(ProtoFields.ValueAt(changed, "absent"), Is.Null);
            Assert.That(ProtoFields.Leaves(null), Is.Empty);
        }

        public static void ClearsAndListReplacementDoNotMutateOriginalSnapshot()
        {
            var source = new Wire.EditableConfig
            {
                Daemon = new Wire.Daemon { TitleModel = "model" },
            };
            source.Daemon.UsageItems["provider"] = new Wire.UsageItem
            {
                Poll = true,
                IntervalSecs = 30,
            };
            var changed = ProtoFields.Apply(
                source,
                new Dictionary<string, object>
                {
                    ["daemon.title_model"] = "",
                    ["daemon.usage_items.provider.interval_secs"] = null,
                    ["daemon.usage_items.provider.poll"] = false,
                }
            );
            Assert.That(changed.Daemon.TitleModel, Is.Empty);
            Assert.That(changed.Daemon.UsageItems["provider"].HasIntervalSecs, Is.False);
            Assert.That(changed.Daemon.UsageItems["provider"].Poll, Is.False);
            Assert.That(source.Daemon.TitleModel, Is.EqualTo("model"));
            Assert.That(source.Daemon.UsageItems["provider"].IntervalSecs, Is.EqualTo(30UL));
            var changes = ProtoFields.Changes(changed, source);
            Assert.That(
                changes.Keys,
                Is.EquivalentTo(
                    new[]
                    {
                        "daemon.title_model",
                        "daemon.usage_items.provider.interval_secs",
                        "daemon.usage_items.provider.poll",
                    }
                )
            );
            Assert.That(changes["daemon.usage_items.provider.interval_secs"], Is.Null);
            var policy = new Wire.WorkerPolicy { Templates = { "review", "build" } };
            var snapshot = ProtoFields.Leaves(policy);
            Assert.That(
                ProtoFields.Changes(policy.Clone(), policy),
                Is.Empty,
                "repeated values compare by contents"
            );
            policy.Templates.Clear();
            Assert.That(
                (object[])snapshot["templates"],
                Is.EqualTo(new[] { "review", "build" }),
                "repeated snapshots remain independent"
            );
            Assert.That(
                (object[])
                    ProtoFields.ChangesFromLeaves(ProtoFields.Leaves(policy), snapshot)[
                        "templates"
                    ],
                Is.Empty,
                "cleared lists are changed leaves"
            );
        }

        public static void LeafSnapshotsExcludeEndpointSecretsAndSupportScalarMaps()
        {
            var source = new Wire.EditableConfig
            {
                Daemon = new Wire.Daemon
                {
                    Token = "secret",
                    Bind = "private",
                    TitleModel = "model",
                },
            };
            var leaves = ProtoFields.Leaves(source);
            Assert.That(leaves.ContainsKey("daemon.token"), Is.False);
            Assert.That(leaves.ContainsKey("daemon.bind"), Is.False);
            var changed = source.Clone();
            changed.Daemon.Token = "new secret";
            changed.Daemon.Bind = "new bind";
            Assert.That(
                ProtoFields.Changes(changed, source),
                Is.Empty,
                "endpoint metadata cannot become a settings patch"
            );
            var preset = new Wire.SandboxPreset();
            preset.Setenv["key.with~characters"] = "literal $HOME";
            Assert.That(
                ProtoFields.ValueAt(preset, "setenv.key~1with~0characters"),
                Is.EqualTo("literal $HOME")
            );
            var error = Assert.Throws<ArgumentException>(() =>
                ProtoFields.Apply(
                    source,
                    new Dictionary<string, object> { ["daemon.typo"] = "bad" }
                )
            );
            Assert.That(error.Message, Does.Contain("Unknown editable field: typo"));
            Assert.That(source.Daemon.TitleModel, Is.EqualTo("model"));
        }
    }
}
