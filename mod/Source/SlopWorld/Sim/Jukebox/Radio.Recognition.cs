using System;
using System.Threading;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The Shazam-style background lookup. SongRecognizer owns the capture device and child
    // process; this partial serialises its state across the worker thread and the UI, and binds
    // each result to the track that was heard so a late answer never lands on the wrong song.
    public static partial class Radio
    {
        // Recognition is a background lookup; the gate serialises its state across the worker
        // thread and the UI. Input and error are kept for the UI to make the operation legible:
        // where it listened, and why it came back empty. The token source lets a cancel action
        // stop a slow Shazam call.
        static readonly object RecognitionGate = new object();
        static bool _recognizing;
        static string _recognitionInput;
        static string _recognitionError;
        static CancellationTokenSource _recognitionCancel;

        // The UI's window onto the background lookup. Read under the gate because the worker
        // thread writes them; each answers one question the recognizing state needs to show.
        public static bool Recognizing
        {
            get { lock (RecognitionGate) return _recognizing; }
        }

        public static string RecognizingInput
        {
            get { lock (RecognitionGate) return _recognitionInput; }
        }

        public static string RecognitionError
        {
            get { lock (RecognitionGate) return _recognitionError; }
        }

        // Start one lookup at a time. SongRecognizer owns the capture device, the child process,
        // its JSON and its timeout; this keeps it off the Unity thread so a slow Shazam response
        // cannot freeze the game, and binds the result to the track that was heard. Duplicate
        // requests are refused rather than queued: two Shazam calls on one song help nobody.
        public static void Recognize()
        {
            Read();
            if (_muted || !_playing || string.IsNullOrEmpty(NowPlaying))
            {
                UiWidgets.Fail("nothing is playing");
                return;
            }

            int version;
            string source;
            CancellationToken token;
            lock (RecognitionGate)
            {
                if (_recognizing)
                {
                    UiWidgets.Fail("recognition is already running");
                    return;
                }
                _recognizing = true;
                _recognitionError = null;
                _recognitionInput = null;
                _recognitionCancel = new CancellationTokenSource();
                token = _recognitionCancel.Token;
                version = _trackVersion;
                source = SourceLabel();
            }

            Messages.Message("Jukebox: recognizing...", MessageTypeDefOf.NeutralEvent, false);
            ThreadPool.QueueUserWorkItem(_ => RunRecognition(version, source, token));
        }

        // Cancel an in-flight lookup. The worker's result then comes back Canceled and is
        // dropped without a toast, because the player already knows they stopped it.
        public static void CancelRecognition()
        {
            CancellationTokenSource cancel;
            lock (RecognitionGate)
            {
                if (!_recognizing || _recognitionCancel == null) return;
                cancel = _recognitionCancel;
            }
            try { cancel.Cancel(); } catch { }
        }

        static void RunRecognition(int version, string source, CancellationToken token)
        {
            var recognizer = new SongRecognizer(new SystemProcessRunner());
            RecognitionResult result;
            try
            {
                // Choose the input first and publish its label so the recognizing state can
                // name where it is listening while the slow lookup runs.
                AudioInput input = recognizer.SelectInput(token);
                DaemonClient.OnMainThread(() => SetRecognitionInput(input.Label));
                result = recognizer.Recognize(input, token);
            }
            catch (Exception e)
            {
                result = new RecognitionResult
                {
                    Status = RecognitionStatus.Error,
                    Message = e.Message,
                };
            }

            DaemonClient.OnMainThread(() => FinishRecognition(version, source, result));
        }

        static void SetRecognitionInput(string label)
        {
            lock (RecognitionGate)
                if (_recognizing) _recognitionInput = label;
        }

        // Apply a result only if it still belongs to what is playing: SongRec heard a snippet,
        // and by the time it answers the station may have moved on or the source been switched.
        static void FinishRecognition(int version, string source, RecognitionResult result)
        {
            lock (RecognitionGate)
            {
                _recognizing = false;
                _recognitionCancel?.Dispose();
                _recognitionCancel = null;
                if (result != null && !string.IsNullOrEmpty(result.Input))
                    _recognitionInput = result.Input;
            }

            if (result == null || result.Status == RecognitionStatus.Canceled) return;

            if (version != _trackVersion || source != SourceLabel())
            {
                SetRecognitionError("track changed before recognition finished");
                return;
            }
            if (!result.Ok)
            {
                SetRecognitionError(string.IsNullOrEmpty(result.Message)
                    ? "recognition failed" : result.Message);
                return;
            }

            lock (RecognitionGate) _recognitionError = null;
            _recognizedArtist = result.Artist;
            _recognizedTitle = result.Title;
            Messages.Message("Jukebox: recognized " + _recognizedArtist + " - "
                + _recognizedTitle, MessageTypeDefOf.TaskCompletion, false);
        }

        static void SetRecognitionError(string message)
        {
            lock (RecognitionGate) _recognitionError = message;
            UiWidgets.Fail(message);
        }
    }
}
