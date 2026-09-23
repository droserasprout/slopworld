using System;
using System.Threading;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Run song recognition in the background. SongRecognizer owns the capture device and child process.
    // Synchronize recognition state between the worker thread and UI.
    // Check each result against playback state before applying it.
    public static partial class Radio
    {
        // Use a lock for recognition state shared by the worker thread and UI.
        // Retain the input label and error for display. The cancellation token can stop a slow request.
        static readonly object RecognitionGate = new object();
        static bool _recognizing;
        static string _recognitionInput;
        static string _recognitionError;
        static CancellationTokenSource _recognitionCancel;
        static readonly RecognitionTrackState RecognitionTrack = new RecognitionTrackState();

        // Permit tests and other hosts to supply a recognition service.
        // Use SongRec by default. Tests can check eligibility and delayed results without starting a process.
        internal static Func<IRecognitionService> RecognitionFactory = () =>
            new SongRecognizer(new SystemProcessRunner());

        // Read recognition state under the lock because the worker thread also accesses it.
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

        // Run one request at a time outside the Unity thread. Reject duplicate requests.
        // SongRecognizer controls capture, process execution, JSON parsing, and timeouts.
        // Retain the playback revision and source to validate the result.
        public static void Recognize()
        {
            Read();
            if (_spotify || !RecognitionTrack.Eligible)
            {
                UiLayout.Fail("nothing is playing");
                return;
            }

            int version;
            string source;
            CancellationToken token;
            lock (RecognitionGate)
            {
                if (_recognizing)
                {
                    UiLayout.Fail("recognition is already running");
                    return;
                }
                _recognizing = true;
                _recognitionError = null;
                _recognitionInput = null;
                _recognitionCancel = new CancellationTokenSource();
                token = _recognitionCancel.Token;
                version = RecognitionTrack.Revision;
                source = RecognitionTrack.Source;
            }

            Messages.Message("Jukebox: Recognizing", MessageTypeDefOf.NeutralEvent, false);
            ThreadPool.QueueUserWorkItem(_ => RunRecognition(version, source, token));
        }

        // Cancel the active request. Discard its result without a notification.
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
            RecognitionResult result;
            try
            {
                IRecognitionService recognizer = RecognitionFactory();
                // Publish the selected input label before starting recognition so the UI can show the capture source.
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

            DaemonClient.OnMainThread(() => FinishRecognition(version, source, token, result));
        }

        static void SetRecognitionInput(string label)
        {
            lock (RecognitionGate)
                if (_recognizing) _recognitionInput = label;
        }

        // Apply the result only if playback still matches the recorded revision and source.
        static void FinishRecognition(int version, string source, CancellationToken token,
                                       RecognitionResult result)
        {
            lock (RecognitionGate)
            {
                _recognizing = false;
                _recognitionCancel?.Dispose();
                _recognitionCancel = null;
                if (result != null && !string.IsNullOrEmpty(result.Input))
                    _recognitionInput = result.Input;
            }

            if (result == null || result.Status == RecognitionStatus.Canceled
                || token.IsCancellationRequested) return;

            if (!RecognitionTrack.IsCurrent(version, source))
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
            UiLayout.Fail(message);
        }
    }
}
