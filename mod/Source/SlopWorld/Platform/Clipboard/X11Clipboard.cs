using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SlopWorld
{
    // Owns selections on the game's X11/XWayland desktop, independently of endpoint.
    // Serialize operations so a paste submitted after a copy observes that copy. Completion
    // delivery belongs to the host's UI pump; no Unity APIs run on the worker.
    sealed class X11Clipboard
    {
        readonly IProcessRunner _runner;
        readonly Action<Action> _complete;
        readonly object _gate = new object();
        Task _tail = Task.CompletedTask;

        public X11Clipboard(IProcessRunner runner, Action<Action> complete)
        {
            _runner = runner;
            _complete = complete;
        }

        public void Read(bool primary, Action<string> receive, Action<string> fail) =>
            Submit(null, primary, receive, fail);

        public void Copy(string text, bool primary, Action ok, Action<string> fail) =>
            Submit(text, primary, _ => ok?.Invoke(), fail);

        // Text probing and image fallback are one queue entry. A later copy must
        // not replace the selection while the UI is waiting to pump a text callback.
        public void ReadPaste(Action<string> text, Action<byte[]> image, Action<string> fail)
        {
            Enqueue(() =>
            {
                try
                {
                    string value = null;
                    try
                    {
                        value = Run(null, false);
                    }
                    catch when (image != null) { }
                    if (!string.IsNullOrEmpty(value) || image == null)
                        _complete(() => text(value));
                    else
                    {
                        byte[] bytes = ReadImage();
                        _complete(() => image(bytes));
                    }
                }
                catch (Exception e)
                {
                    string error = e.Message;
                    _complete(() => fail?.Invoke(error));
                }
            });
        }

        byte[] ReadImage()
        {
            var run = _runner.Run(
                new ProcessSpec
                {
                    FileName = "xclip",
                    Arguments = "-selection clipboard -target image/png -out",
                    TimeoutMs = 1500,
                    BinaryOutputLimit = 20 * 1024 * 1024,
                },
                CancellationToken.None
            );
            if (
                !run.Started
                || run.TimedOut
                || run.Canceled
                || run.ExitCode != 0
                || run.BinaryOutput.Length == 0
            )
                throw new InvalidOperationException(
                    "Cannot read a PNG image from the local clipboard. " + run.StandardError
                );
            return run.BinaryOutput;
        }

        void Enqueue(Action work)
        {
            lock (_gate)
                _tail = _tail.ContinueWith(_ => work(), TaskScheduler.Default);
        }

        void Submit(string text, bool primary, Action<string> receive, Action<string> fail)
        {
            Enqueue(() =>
            {
                try
                {
                    string result = Run(text, primary);
                    _complete(() => receive?.Invoke(result));
                }
                catch (Exception e)
                {
                    string error = e.Message;
                    _complete(() => fail?.Invoke(error));
                }
            });
        }

        string Run(string text, bool primary)
        {
            bool copy = text != null;
            string selection = primary ? "primary" : "clipboard";
            var elapsed = Stopwatch.StartNew();
            string error = "X11 clipboard unavailable";
            foreach (string tool in new[] { "xclip", "xsel" })
            {
                int remaining = 3000 - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0)
                    break;
                string arguments =
                    tool == "xclip"
                        ? "-selection " + selection + (copy ? " -in" : " -target UTF8_STRING -out")
                        : "--" + selection + (copy ? " --input" : " --output");
                var run = _runner.Run(
                    new ProcessSpec
                    {
                        FileName = tool,
                        Arguments = arguments,
                        TimeoutMs = Math.Min(1500, remaining),
                        StandardInput = text,
                        InheritOutput = copy,
                    },
                    CancellationToken.None
                );
                if (run.Started && !run.TimedOut && !run.Canceled && run.ExitCode == 0)
                {
                    string result = run.StandardOutput ?? "";
                    return result.IndexOf('\0') < 0 ? result : "";
                }
                error =
                    !run.Started ? tool + " unavailable"
                    : run.TimedOut ? tool + " timed out"
                    : tool + ": " + run.StandardError;
            }
            throw new InvalidOperationException(error);
        }
    }
}
