namespace SlopWorld
{
    // Retains the scale until dragging ends, including when native control state is lost.
    sealed class UiScaleDrag
    {
        public float? Pending { get; private set; }

        public float? Update(float value, bool held, bool released, bool mouseDown)
        {
            if (held)
            {
                Pending = value;
                return null;
            }
            if (!Pending.HasValue || (!released && mouseDown))
                return null;

            // A reported release includes its final pointer sample. A missing release uses
            // our last retained sample, rather than the slider's reset/current preference.
            float applied = released ? value : Pending.Value;
            Pending = null;
            return applied;
        }
    }
}
