namespace SlopWorld
{
    public class MainButtonWorker_Config : RimWorld.MainButtonWorker
    {
        public override void Activate() => ModOptions.Toggle();
    }
}
