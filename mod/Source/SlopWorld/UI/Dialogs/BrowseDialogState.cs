using System;
using System.Linq;

namespace SlopWorld
{
    // Owns committed directory navigation and request identity; BrowseDialog owns input and drawing.
    public sealed class BrowseDialogState
    {
        readonly OperationGate _operations = new OperationGate();
        public string Path { get; private set; }
        public string Parent { get; private set; }
        public string[] Directories { get; private set; } = Array.Empty<string>();
        public string Error { get; private set; }
        public bool Pending { get; private set; }
        public bool Truncated { get; private set; }

        public long Begin()
        {
            Pending = true;
            Error = null;
            // Keep the committed listing available if navigation fails.
            return _operations.Begin();
        }

        public bool Accept(long generation, Wire.BrowseResult result)
        {
            if (!_operations.IsCurrent(generation))
                return false;
            if (result == null)
                return Fail(generation, "The daemon returned no directory path.");
            Path = result.Path;
            Parent = result.HasParent ? result.Parent : null;
            Directories = result.Dirs.ToArray();
            Truncated = result.Truncated;
            Pending = false;
            return true;
        }

        public bool Fail(long generation, string error)
        {
            if (!_operations.IsCurrent(generation))
                return false;
            Pending = false;
            Error = error;
            return true;
        }

        public void Close()
        {
            _operations.Invalidate();
            Pending = false;
        }
    }
}
