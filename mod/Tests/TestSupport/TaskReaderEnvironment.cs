using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    static partial class Text
    {
        public static bool WordWrap;
    }

    static class TestRectExtensions
    {
        public static Rect ContractedBy(this Rect rect, float amount) =>
            new Rect(
                rect.x + amount,
                rect.y + amount,
                rect.width - 2 * amount,
                rect.height - 2 * amount
            );
    }
}

namespace SlopWorld
{
    static class TaskDetailView
    {
        public static TaskInfo Shown;
        public static IContentView ReturnTo;

        public static void Open(TaskInfo task, IContentView returnTo = null)
        {
            Shown = task;
            ReturnTo = returnTo;
        }
    }

    sealed partial class SessionHub
    {
        public readonly TaskStore TaskStore = new TaskStore(() => 0, error => throw error);
        public List<TaskInfo> Tasks => TaskStore.Tasks;
    }
}
