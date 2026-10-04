using UnityEngine;

namespace SlopWorld
{
    // Agent semantics map onto the shared theme at the feature boundary.
    public static class AgentPresentation
    {
        public static Color AgentStateColor(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return UiTheme.StateWorking;
                case AgentState.Waiting: return UiTheme.StateWaiting;
                case AgentState.Idle: return UiTheme.StateIdle;
                default: return UiTheme.StateDown;
            }
        }

    }
}
