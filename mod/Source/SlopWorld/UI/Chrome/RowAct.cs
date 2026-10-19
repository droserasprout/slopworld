namespace SlopWorld
{
    // What a row of a tree can be asked to do to the file it names. An enum of bits rather than a
    // list, so a row states what it offers without allocating one a frame. The three are drawn in
    // this order, left to right, wherever they are drawn.
    [System.Flags]
    public enum RowAct
    {
        None = 0,
        View = 1,
        Edit = 2,
        Diff = 4,
    }
}
