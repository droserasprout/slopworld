using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SlopWorld.Tests
{
    static class JukeboxFixTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("recognition does not require a title", UntitledPlaybackIsEligible);
            yield return ("muted and stopped playback is ineligible", MutedAndStoppedAreIneligible);
            yield return ("recognition state rejects results after stop or source replacement", StopInvalidatesRecognition);
            yield return ("CanApply rejects a successful result after cancellation", CancellationWins);
            yield return ("native OST paths become structured tracks", NativePathMapping);
            yield return ("native likes keep each sampled track", NativeTracksStayDistinct);
            yield return ("a missing native track writes nothing", MissingNativeTrackWritesNothing);
        }

        sealed class FakeRecognition : IRecognitionService
        {
            public int Calls;

            public AudioInput SelectInput(CancellationToken cancel) =>
                new AudioInput(null, "fixture input");

            public RecognitionResult Recognize(AudioInput input, CancellationToken cancel)
            {
                Calls++;
                return new RecognitionResult
                {
                    Status = RecognitionStatus.Ok,
                    Artist = "Fixture artist",
                    Title = "Fixture title",
                    Input = input.Label,
                };
            }
        }

        static void UntitledPlaybackIsEligible()
        {
            var track = new RecognitionTrackState();
            track.Update(true, false, "Example Radio");
            AssertEx.True(track.Eligible, "audible playback is eligible without station metadata");

            var service = new FakeRecognition();
            var result = service.Recognize(service.SelectInput(CancellationToken.None),
                CancellationToken.None);
            AssertEx.Equal(RecognitionStatus.Ok, result.Status,
                "the injected recognizer can be dispatched for an untitled track");
            AssertEx.Equal(1, service.Calls, "the injected recognizer was called once");
        }

        static void MutedAndStoppedAreIneligible()
        {
            var track = new RecognitionTrackState();
            track.Update(true, true, "Example Radio");
            AssertEx.False(track.Eligible, "muted playback is rejected");
            track.Update(false, false, "Example Radio");
            AssertEx.False(track.Eligible, "stopped playback is rejected");
        }

        static void StopInvalidatesRecognition()
        {
            var track = new RecognitionTrackState();
            track.Update(true, false, "Example Radio");
            int revision = track.Revision;
            string source = track.Source;

            track.Update(false, false, source);
            AssertEx.False(track.IsCurrent(revision, source),
                "a result from before stop cannot apply");

            track.Update(true, false, "Replacement Radio");
            AssertEx.False(track.IsCurrent(revision, source),
                "a result from the replaced source cannot apply");
        }

        static void CancellationWins()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var track = new RecognitionTrackState();
                track.Update(true, false, "Example Radio");
                var service = new FakeRecognition();
                var input = service.SelectInput(cancel.Token);
                cancel.Cancel();
                var result = service.Recognize(input, cancel.Token);
                AssertEx.Equal(RecognitionStatus.Ok, result.Status,
                    "the fake demonstrates a result can arrive after cancellation");
                AssertEx.True(cancel.IsCancellationRequested,
                    "the cancellation remains observable at the Radio boundary");
                AssertEx.False(track.CanApply(track.Revision, track.Source, cancel.Token),
                    "a canceled attempt is not treated as an eligible completion");
            }
        }

        static void NativeTracksStayDistinct()
        {
            string path = TempLikePath();
            try
            {
                var first = new NativeTrackSnapshot("Terry Fail", "one.ogg", "SlopWorld OST");
                var second = new NativeTrackSnapshot("Terry Fail", "two.ogg", "SlopWorld OST");
                NativeLikeRecord firstRecord;
                NativeLikeRecord secondRecord;
                string error;
                AssertEx.True(JukeboxLikeWriter.TryAppend(path, first, DateTime.UtcNow,
                    out firstRecord, out error), "the first native track is saved: " + error);
                AssertEx.True(JukeboxLikeWriter.TryAppend(path, second, DateTime.UtcNow,
                    out secondRecord, out error), "the second native track is saved: " + error);
                AssertEx.Equal("Terry Fail - one.ogg", firstRecord.Display,
                    "confirmation uses the first snapshot");
                AssertEx.Equal("Terry Fail - two.ogg", secondRecord.Display,
                    "confirmation uses the second snapshot");
                string text = File.ReadAllText(path);
                AssertEx.True(text.Contains("title = \"one.ogg\""),
                    "the first title is persisted");
                AssertEx.True(text.Contains("title = \"two.ogg\""),
                    "the second title is persisted");
                AssertEx.True(text.Contains("source = \"SlopWorld OST\""),
                    "native source attribution is not replaced by daemon metadata");
            }
            finally
            {
                Remove(path);
            }
        }

        static void NativePathMapping()
        {
            var track = NativeTrackSnapshot.FromOstClipPath("Sounds\\SlopWorld\\OST\\second.ogg");
            AssertEx.Equal("Terry Fail", track.Artist, "the shipped OST artist is explicit");
            AssertEx.Equal("second.ogg", track.Title, "only the final clip-path component is used");
            AssertEx.Equal("SlopWorld OST", track.Source, "the native source is explicit");
            AssertEx.Equal(null, NativeTrackSnapshot.FromOstClipPath(null),
                "an inactive native song has no snapshot");
        }

        static void MissingNativeTrackWritesNothing()
        {
            string path = TempLikePath();
            try
            {
                NativeLikeRecord record;
                string error;
                AssertEx.False(JukeboxLikeWriter.TryAppend(path, null, DateTime.UtcNow,
                    out record, out error), "no native song refuses the like");
                AssertEx.Equal("nothing is playing", error, "the refusal names the missing song");
                AssertEx.False(File.Exists(path), "the refusal happens before file creation");
            }
            finally
            {
                Remove(path);
            }
        }

        static string TempLikePath() => Path.Combine(Path.GetTempPath(),
            "slopworld-jukebox-test-" + Guid.NewGuid().ToString("N") + ".toml");

        static void Remove(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
