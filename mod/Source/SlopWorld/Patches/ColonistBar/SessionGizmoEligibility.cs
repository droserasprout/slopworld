namespace SlopWorld
{
    // Match ordinary Edit eligibility in the sidebar; worker identity belongs to its task.
    static class SessionGizmoEligibility
    {
        internal static bool CanResume(SessionInfo info) =>
            info != null
            && info.ResumeSupported
            && !info.Alive
            && !info.AutoResumePending
            && !info.Host
            && !info.Worker
            && !info.Ephemeral;

        internal static bool CanEdit(SessionInfo info) =>
            info != null && !info.Ephemeral && !info.Host && !info.Worker;
    }
}
