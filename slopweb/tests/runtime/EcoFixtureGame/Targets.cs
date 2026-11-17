namespace EcoFixtureGame;

public class Selector
{
    public bool Allow;
    public int Clicks;

    public void HandleMapClicks() => Clicks++;
}

public static class MapInterface
{
    public static int Clicks;

    public static void HandleMapClicks() => Clicks++;
}

public static class Dialogs
{
    public static int Value;

    public static void Add(int ignored, int window) => Value += window;
}
