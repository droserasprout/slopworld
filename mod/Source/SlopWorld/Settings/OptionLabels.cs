namespace SlopWorld
{
    public static class OptionLabels
    {
        public static string Auto(string detected = null) =>
            string.IsNullOrWhiteSpace(detected) ? "Auto" : "Auto (" + detected + ")";
    }
}
