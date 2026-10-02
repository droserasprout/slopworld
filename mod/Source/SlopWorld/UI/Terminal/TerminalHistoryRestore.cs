namespace SlopWorld
{
    // Saved history identity decides reuse; the panel owns installing or discarding rows.
    internal struct TerminalHistoryRestore
    {
        internal long RunId;
        internal int ConnectionGeneration, Cols, Rows, History;
        internal ulong? Sequence;
        internal bool AltScreen;

        internal struct Result
        {
            internal bool Compatible;
            internal int Shift;
        }

        internal Result Evaluate(long? runId, int generation, ScreenBuf live)
        {
            if (live == null || runId != RunId || generation != ConnectionGeneration ||
                (Cols > 0 && live.Cols > 0 && Cols != live.Cols) ||
                (Rows > 0 && live.Rows > 0 && Rows != live.Rows) || AltScreen != live.AltScreen)
                return default(Result);

            if (Sequence.HasValue && live.Seq == Sequence)
                return new Result { Compatible = live.History == History };
            if (History >= 0)
                return new Result { Compatible = live.History >= History, Shift = live.History - History };
            return new Result { Compatible = !Sequence.HasValue };
        }
    }
}
