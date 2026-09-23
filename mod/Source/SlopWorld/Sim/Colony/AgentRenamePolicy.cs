using System;

namespace SlopWorld
{
    // The colony name table and daemon session list can differ during a rename.
    // Keep this logic independent of Pawn so tests can run without RimWorld.
    internal static class AgentRenamePolicy
    {
        public static bool Keeps(string bindingName, Func<string, bool> member,
                                 Func<string, string> destination,
                                 Func<string, string> source)
        {
            var other = destination(bindingName);
            if (other != null && member(other)) return true;
            other = source(bindingName);
            return other != null && member(other);
        }

        public static bool Covers(string sessionName, Func<string, bool> binding,
                                  Func<string, string> destination,
                                  Func<string, string> source)
        {
            var other = source(sessionName);
            if (other != null && binding(other)) return true;
            other = destination(sessionName);
            return other != null && binding(other);
        }

        public static string SessionName(string bindingName, Func<string, bool> member,
                                         Func<string, string> destination,
                                         Func<string, string> source)
        {
            var other = destination(bindingName);
            if (other != null && member(other)) return other;
            other = source(bindingName);
            return other != null && member(other) ? other : bindingName;
        }
    }
}
