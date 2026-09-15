using UnityEngine;

namespace SlopWorld
{
    sealed class StyleSet
    {
        public GUIStyle Normal = new GUIStyle();
        public GUIStyle For(InlineRun run, int heading) => Normal;
        public float MeasureChar(GUIStyle style, char value) => 1f;
    }
}
