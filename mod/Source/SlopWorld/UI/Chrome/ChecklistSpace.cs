using UnityEngine;

namespace SlopWorld
{
    // Native input can deliver a printable key as a key packet, a text packet, or both.
    internal struct ChecklistSpace
    {
        int _frame;
        int _unpairedKeys;

        public bool ShouldToggle(Event e)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                _unpairedKeys = 0;
            }
            bool key = e.keyCode == KeyCode.Space;
            bool paired = key ? _unpairedKeys < 0 : _unpairedKeys > 0;
            _unpairedKeys += key ? 1 : -1;
            return !paired;
        }
    }
}
