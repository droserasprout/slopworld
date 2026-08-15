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

    public enum ShortcutKind { Prompt, Shell, Breadcrumb, FileAction }

    // Temp is a fresh scratch directory per run; Ask is decided at the button.
    public enum ShortcutLink { Project, Temp, Ask }
}
