using Verse;

namespace RimTalkRealitySync
{
    public class RealitySyncSettings : ModSettings
    {
        public string WeatherApiProvider = "none";

        // Separated API keys for different providers to prevent state mismatch
        public string OpenWeatherApiKey = "";
        public string HeWeatherApiKey = "";

        // Custom API Host for QWeather (Empty by default to force user input)
        public string HeWeatherApiHost = "";

        public string CustomCity = "Beijing";
        public bool UseCelsius = true;
        public int UpdateIntervalMinutes = 30;
        public bool DebugMode = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref WeatherApiProvider, "weatherApiProvider", "none");

            // Save and load separated keys
            Scribe_Values.Look(ref OpenWeatherApiKey, "openWeatherApiKey", "");
            Scribe_Values.Look(ref HeWeatherApiKey, "heWeatherApiKey", "");

            // Save QWeather API Host without default values
            Scribe_Values.Look(ref HeWeatherApiHost, "heWeatherApiHost", "");

            Scribe_Values.Look(ref CustomCity, "customCity", "Beijing");
            Scribe_Values.Look(ref UseCelsius, "useCelsius", true);
            Scribe_Values.Look(ref UpdateIntervalMinutes, "updateIntervalMinutes", 30);
            Scribe_Values.Look(ref DebugMode, "debugMode", false);
        }
    }
}