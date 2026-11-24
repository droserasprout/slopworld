namespace BrowserAdapters;

internal static class BrowserSocketLimits
{
    // The bridge accepts a 32 MiB binary message, including 20 MiB image pastes
    // and their protobuf envelope. Keep the in-flight write inside the queue budget.
    internal static bool FitsOutgoing(int queuedBytes, int payloadBytes) =>
        payloadBytes <= 32 * 1024 * 1024 - queuedBytes;
}
