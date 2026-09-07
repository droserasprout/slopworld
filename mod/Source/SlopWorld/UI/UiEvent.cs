using UnityEngine;

namespace SlopWorld
{
    // WindowStack can mark an event Used before a lower window's contents run. Selection and
    // link handlers still need the original event kind so a consumed mouse gesture can finish.
    static class UiEvent
    {
        public static EventType RawType(Event e) => e == null
            ? EventType.Ignore
            : e.type == EventType.Used ? e.rawType : e.type;
    }
}
