using Verse;

namespace SlopWorld
{
    // Save compatibility only: retire landed and pending Settings objects on load.
    // Keep the component and ThingDef identities so old saves deserialize cleanly.
    public class SettingsHeater : MapComponent
    {
        public SettingsHeater(Map map)
            : base(map) { }

        Thing _heater;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref _heater, "settingsHeater");
        }

        public override void FinalizeInit()
        {
            if (_heater != null && !_heater.Destroyed)
                _heater.Destroy(DestroyMode.Vanish);
            _heater = null;
            var heaters = map.listerThings.ThingsOfDef(ModDefOf.SlopSettingsHeater);
            while (heaters.Count > 0)
                heaters[heaters.Count - 1].Destroy(DestroyMode.Vanish);
        }
    }
}
