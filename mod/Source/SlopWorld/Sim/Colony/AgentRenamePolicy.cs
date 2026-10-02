using System;

namespace SlopWorld
{
    // The colony name table and daemon session list can differ during a rename.
    // Keep this logic independent of Pawn so tests can run without RimWorld.
    internal static class AgentRenamePolicy
    {
        public static bool KeepsBinding(string bindingName, Func<string, bool> hasSession,
                                        Func<string, string> renameDestination,
                                        Func<string, string> renameSource)
        {
            var other = renameDestination(bindingName);
            if (other != null && hasSession(other)) return true;
            other = renameSource(bindingName);
            return other != null && hasSession(other);
        }

        public static bool HasBindingForSession(string sessionName, Func<string, bool> hasBinding,
                                                Func<string, string> renameDestination,
                                                Func<string, string> renameSource)
        {
            var other = renameSource(sessionName);
            if (other != null && hasBinding(other)) return true;
            other = renameDestination(sessionName);
            return other != null && hasBinding(other);
        }

        public static string ResolveSessionName(string bindingName, Func<string, bool> hasSession,
                                                Func<string, string> renameDestination,
                                                Func<string, string> renameSource)
        {
            var other = renameDestination(bindingName);
            if (other != null && hasSession(other)) return other;
            other = renameSource(bindingName);
            return other != null && hasSession(other) ? other : bindingName;
        }
    }
}
