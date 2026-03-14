using Verse;

namespace RimTalkRealitySync
{
    public class RealitySyncSettings : ModSettings
    {
        public string WeatherApiProvider = "none";
        public string WeatherApiKey = "";
        public string CustomCity = "Beijing";
        public bool UseCelsius = true;
        public int UpdateIntervalMinutes = 30;
        public bool DebugMode = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref WeatherApiProvider, "weatherApiProvider", "none");
            Scribe_Values.Look(ref WeatherApiKey, "weatherApiKey", "");
            Scribe_Values.Look(ref CustomCity, "customCity", "Beijing");
            Scribe_Values.Look(ref UseCelsius, "useCelsius", true);
            Scribe_Values.Look(ref UpdateIntervalMinutes, "updateIntervalMinutes", 30);
            Scribe_Values.Look(ref DebugMode, "debugMode", false);
        }
    }
}