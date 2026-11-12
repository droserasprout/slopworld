namespace BrowserAdapters;

// GenThreading's original caller blocks waiting for thread-pool batches. Browser
// tasks cannot execute until that caller returns. Keep callback failures local,
// as the installed helper does, and complete all items before returning.
public static class SerialWork
{
    public static void ForEach<T>(List<T> list, Action<T> callback, int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism != -1 && maxDegreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        // Installed SliceWork copies the list before any callback runs.
        foreach (var item in list.ToArray())
        {
            try
            {
                callback(item);
            }
            catch (Exception error)
            {
                Verse.Log.Error("Error in ParallelForEach(): " + error);
            }
        }
    }
}
