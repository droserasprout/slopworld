using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Verse;


namespace SlopWorld
{
    // Down is "the process is not running": the colonist is put on the floor rather than
    // killed, the same process being able to get it back up.
    public enum AgentState { Down, Working, Waiting, Idle }

    public enum LibraryItemKind { Prompt, Shell, Breadcrumb, FileAction }

    public enum FileActionMode { Ask, ShowResult, OpenTerminal }

    public static class FileActionModeText
    {
        public static FileActionMode Parse(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "show_result": return FileActionMode.ShowResult;
                case "open_terminal": return FileActionMode.OpenTerminal;
                default: return FileActionMode.Ask;
            }
        }

        public static string Name(FileActionMode mode) => mode == FileActionMode.ShowResult
            ? "show_result"
            : mode == FileActionMode.OpenTerminal ? "open_terminal" : "ask";

        public static string Label(FileActionMode mode) => mode == FileActionMode.ShowResult
            ? "Show result"
            : mode == FileActionMode.OpenTerminal ? "Open terminal" : "Ask every time";
    }

    // Temp is a fresh scratch directory per run; Ask is decided at the button.
    public enum LibraryItemLink { Project, Temp, Ask }
}
