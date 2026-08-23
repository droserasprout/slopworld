namespace SlopWorld
{
    // ScreenBuf stores parsed terminal runs, but JSON hydration only needs the type shape.
    // The real renderer supplies the game-bound implementation, which also carries the
    // Unity colors. TerminalColumns reads only the absolute column and the run text, so the
    // stub carries those two and nothing that would drag UnityEngine into the test build.
    public struct SgrRun
    {
        public int Col;
        public string Text;
    }
}
