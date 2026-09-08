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

    public enum FileActionMode { Ask, ShowResult, OpenTerminal, Nothing }

    public static class FileActionModeText
    {
        public static FileActionMode Parse(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case WireContract.FileActionMode.ShowResult: return FileActionMode.ShowResult;
                case WireContract.FileActionMode.OpenTerminal: return FileActionMode.OpenTerminal;
                case WireContract.FileActionMode.Nothing: return FileActionMode.Nothing;
                default: return FileActionMode.Ask;
            }
        }

        public static string Name(FileActionMode mode) => mode == FileActionMode.ShowResult
            ? WireContract.FileActionMode.ShowResult
            : mode == FileActionMode.OpenTerminal ? WireContract.FileActionMode.OpenTerminal
            : mode == FileActionMode.Nothing ? WireContract.FileActionMode.Nothing
            : WireContract.FileActionMode.Ask;

        public static string Label(FileActionMode mode) => mode == FileActionMode.ShowResult
            ? "Show result"
            : mode == FileActionMode.OpenTerminal ? "Open terminal"
            : mode == FileActionMode.Nothing ? "Nothing" : "Ask every time";
    }

    // Temp is a fresh scratch directory per run; Ask is decided at the button.
    public enum LibraryItemLink { Project, Temp, Ask }
}
