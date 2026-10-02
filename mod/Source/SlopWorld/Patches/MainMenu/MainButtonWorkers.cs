using Verse;

namespace SlopWorld
{
    // A def is the one thing a refusing mod cannot take back. These buttons are added to the
    // bar by XML, which is read whether we patched anything or not. So in somebody's ordinary game
    // they are doors onto the explanation rather than onto a daemon we never dialled.
    public abstract class MainButtonWorker_Slop : RimWorld.MainButtonWorker
    {
        public sealed override void Activate()
        {
            if (!ModProfile.Ok)
            {
                ModProfile.Complain();
                return;
            }
            Open();
        }

        protected abstract void Open();
    }

    public class MainButtonWorker_Config : MainButtonWorker_Slop
    {
        protected override void Open() => ModOptions.Toggle();
    }
}
