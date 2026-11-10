using System;
using System.Diagnostics;
using System.IO;
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
                    string imagePath = image == null ? null : LocalImagePath(value);
                    if (imagePath != null)
                    {
                        byte[] bytes = ReadImageFile(imagePath);
                        _complete(() => image(bytes));
                    }
                    else if (!string.IsNullOrEmpty(value) || image == null)
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

        // Resolve only a standalone local file URI, on the desktop that owns the
        // clipboard. Ordinary text/PRIMARY and host shells never enter this path.
        static string LocalImagePath(string text)
        {
            string value = text?.Trim();
            if (
                string.IsNullOrEmpty(value)
                || !value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                || value.Contains('\r')
                || value.Contains('\n')
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !uri.IsFile
                || (uri.Host.Length != 0 && uri.Host != "localhost")
                || uri.Query.Length != 0
                || uri.Fragment.Length != 0
            )
                return null;
            string path = uri.LocalPath;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".webp":
                    return path;
                default:
                    return null;
            }
        }

        static byte[] ReadImageFile(string path)
        {
            // Bound allocation and reads even if the file grows after opening.
            // The daemon validates the actual format and decoded dimensions.
            const int limit = 20 * 1024 * 1024;
            using (var file = File.OpenRead(path))
            {
                long length = file.Length;
                if (length == 0 || length > limit)
                    throw new InvalidOperationException(
                        "Image file must be between 1 byte and 20 MiB."
                    );
                byte[] bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = file.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0)
                        throw new IOException("Image file changed while reading.");
                    offset += count;
                }
                if (file.ReadByte() != -1)
                    throw new IOException("Image file changed while reading.");
                return bytes;
            }
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
