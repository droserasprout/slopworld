using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class DaemonConfigDraftTests
    {
        public static void NormalizationOnlyTouchesUneditedFieldsAndCanBeCanceled()
        {
            var draft = new DaemonConfigDraft();
            Assert.That(draft.HasDraft || draft.IsDirty, Is.False);
            Assert.That(draft.ConflictMessage, Is.Null);
            Assert.That(draft.NormalizeTextIfUnchanged("missing", "value"), Is.False);
            draft.MarkCurrentClean();
            draft.ResetToBaseline();
            draft.LoadServer(new DaemonConfig { TitleMinChars = 20 });
            Assert.That(draft.HasDraft, Is.True);
            draft.Text("title", "daemon.title_min_chars", "020");
            Assert.That(draft.NormalizeTextIfUnchanged("title", "20"), Is.True);
            Assert.That(draft.IsTextDirty("title"), Is.False);
            draft.QueueNormalization("title", "daemon.title_min_chars", "discarded");
            draft.ClearQueuedNormalizations();
            draft.ApplyQueuedNormalizations();
            Assert.That(draft.TextSnapshot()["title"], Is.EqualTo("20"));
            draft.SetText("title", "daemon.title_min_chars", "21");
            Assert.That(draft.IsTextDirty("title"), Is.True);
            Assert.That(draft.NormalizeTextIfUnchanged("title", "20"), Is.False);
            draft.QueueNormalization("title", "daemon.title_min_chars", "20");
            draft.ApplyQueuedNormalizations();
            Assert.That(draft.TextSnapshot()["title"], Is.EqualTo("21"));
            draft.MarkCurrentClean();
            draft.ApplyQueuedNormalizations();
            Assert.That(draft.TextSnapshot()["title"], Is.EqualTo("21"), "rejected normalization is consumed, not deferred");
            Assert.That(draft.IsDirty, Is.False);
        }

        public static void AcknowledgementPreservesEditsMadeWhileSaveWasPending()
        {
            var draft = new DaemonConfigDraft();
            draft.LoadServer(new DaemonConfig { TitleModel = "base" });
            draft.Text("model", "daemon.title_model", "base");
            draft.Config.TitleModel = "submitted";
            draft.SetText("model", "daemon.title_model", "submitted");
            var submitted = draft.Config.Snapshot();
            var text = draft.TextSnapshot();
            draft.Config.TitleModel = "newer edit";
            draft.SetText("model", "daemon.title_model", "newer edit");
            draft.Acknowledge(submitted, text);
            submitted.Daemon.TitleModel = "mutated caller snapshot";
            Assert.That(draft.BaselineValue().Daemon.TitleModel, Is.EqualTo("submitted"));
            var copy = draft.BaselineValue();
            copy.Daemon.TitleModel = "mutated copy";
            Assert.That(draft.BaselineValue().Daemon.TitleModel, Is.EqualTo("submitted"), "baseline accessor returns isolated copy");
            Assert.That(draft.Config.TitleModel, Is.EqualTo("newer edit"));
            Assert.That(draft.TextSnapshot()["model"], Is.EqualTo("newer edit"));
            Assert.That(draft.IsDirty && draft.IsTextDirty("model"), Is.True);
            draft.ResetToBaseline();
            Assert.That(draft.Config.TitleModel, Is.EqualTo("submitted"));
            Assert.That(draft.TextSnapshot()["model"], Is.EqualTo("submitted"));
            Assert.That(draft.IsDirty, Is.False);
        }

        public static void EscapedProviderKeysMergeAndDiscardAgainstLatestServer()
        {
            const string provider = "provider.v1~test";
            string path = "daemon.usage_items." + ProtoFields.Escape(provider) + ".interval_secs";
            DaemonConfig Server(int interval, string model)
            {
                var config = new DaemonConfig { TitleModel = model };
                config.UsageItems[provider] = new DaemonConfig.UsageItemConfig { Poll = true, IntervalSecs = interval };
                return config;
            }
            var draft = new DaemonConfigDraft();
            draft.LoadServer(Server(30, "base"));
            draft.Text("usage.interval", path, "30");
            draft.Text("model", "daemon.title_model", "base");
            draft.SetText("usage.interval", path, "unfinished input");
            draft.LoadServer(Server(60, "external"));
            Assert.That(draft.TextSnapshot()["usage.interval"], Is.EqualTo("unfinished input"));
            Assert.That(draft.TextSnapshot()["model"], Is.EqualTo("external"));
            Assert.That(draft.Conflicts, Does.Contain(path));
            Assert.That(draft.FieldKeys("usage."), Is.EqualTo(new[] { "usage.interval" }));
            Assert.That(draft.FieldKeys(), Is.EqualTo(new[] { "model", "usage.interval" }));
            Assert.That(draft.FieldKeys("Usage."), Is.Empty, "field prefixes are ordinal");
            draft.ResetToBaseline();
            Assert.That(draft.TextSnapshot()["usage.interval"], Is.EqualTo("60"));
            Assert.That(draft.Config.UsageItems[provider].IntervalSecs, Is.EqualTo(60));
            Assert.That(draft.HasConflicts || draft.IsDirty, Is.False);
        }
    }
}
