namespace SlopWorld
{
    // Down is "the process is not running". The colonist is put on the floor rather than killed,
    // the same process being able to get it back up.
    public enum AgentState { Down, Working, Waiting, Idle }

    public enum LibraryItemKind { Prompt, Shell, Breadcrumb, FileAction }

    public enum FileActionMode { Ask, ShowResult, OpenTerminal, Nothing }

    public static class FileActionModeText
    {
        public static FileActionMode Parse(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case WireProtocol.FileActionMode.ShowResult: return FileActionMode.ShowResult;
                case WireProtocol.FileActionMode.OpenTerminal: return FileActionMode.OpenTerminal;
                case WireProtocol.FileActionMode.Nothing: return FileActionMode.Nothing;
                default: return FileActionMode.Ask;
            }
        }

        public static string Name(FileActionMode mode) => mode == FileActionMode.ShowResult
            ? WireProtocol.FileActionMode.ShowResult
            : mode == FileActionMode.OpenTerminal ? WireProtocol.FileActionMode.OpenTerminal
            : mode == FileActionMode.Nothing ? WireProtocol.FileActionMode.Nothing
            : WireProtocol.FileActionMode.Ask;


    }

    // Temp is a fresh scratch directory per run. Ask is decided at the button.
    public enum LibraryItemLink { Project, Temp, Ask }
}
