using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class BoundedQueueTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("coalesces the latest replaceable message", CoalescesLatestMessage);
            yield return ("evicts replaceable messages for important work",
                EvictsReplaceableMessages);
            yield return ("keeps important messages bounded", KeepsImportantMessagesBounded);
            yield return ("reserves action completion capacity", ReservesActionCapacity);
        }

        static void CoalescesLatestMessage()
        {
            var queue = new BoundedStringQueue(2, 20);
            AssertEx.True(queue.TryEnqueue("old", "session"), "initial live message");
            AssertEx.True(queue.TryEnqueue("new", "session"), "replacement live message");
            AssertEx.Equal(1, queue.Count, "coalesced count");
            AssertEx.True(queue.TryDequeue(out var value), "dequeue coalesced message");
            AssertEx.Equal("new", value, "coalesced value");
        }

        static void EvictsReplaceableMessages()
        {
            var queue = new BoundedStringQueue(2, 20);
            AssertEx.True(queue.TryEnqueue("live-1", "one"), "first live message");
            AssertEx.True(queue.TryEnqueue("live-2", "two"), "second live message");
            AssertEx.True(queue.TryEnqueue("control"), "important message");

            AssertEx.True(queue.TryDequeue(out var first), "dequeue remaining live message");
            AssertEx.Equal("live-2", first, "oldest live message was evicted");
            AssertEx.True(queue.TryDequeue(out var second), "dequeue important message");
            AssertEx.Equal("control", second, "important message order");
        }

        static void KeepsImportantMessagesBounded()
        {
            var queue = new BoundedStringQueue(1, 5);
            AssertEx.True(queue.TryEnqueue("one"), "first important message");
            AssertEx.False(queue.TryEnqueue("two"), "second important message is rejected");
            AssertEx.Equal(1, queue.Count, "important message count");
            AssertEx.Equal(3, queue.Bytes, "important message bytes");
        }

        static void ReservesActionCapacity()
        {
            var queue = new BoundedActionQueue(2, 10);
            Action callback = () => { };
            AssertEx.True(queue.TryReserve(6), "reserve completion capacity");
            AssertEx.False(queue.TryEnqueue(callback, 5), "reserved bytes stay unavailable");
            AssertEx.True(queue.EnqueueReserved(callback, 6, 3), "enqueue reserved completion");
            AssertEx.Equal(1, queue.Count, "completion count");
            AssertEx.Equal(3, queue.Bytes, "completion bytes");
        }
    }
}
