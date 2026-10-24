namespace SlopWorld
{
    // Match ordinary Edit eligibility in the sidebar; worker identity belongs to its task.
    static class SessionGizmoEligibility
    {
        internal static bool CanEdit(SessionInfo info) =>
            info != null && !info.Ephemeral && !info.Host && !info.Worker;
    }
}
