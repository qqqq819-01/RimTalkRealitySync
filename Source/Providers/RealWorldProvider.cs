using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimTalkRealitySync;
using RimWorld;
using Verse;

namespace RimTalkRealitySync
{
    /// <summary>
    /// Provides real-world data (time, weather, save info) to be injected into RimTalk.
    /// </summary>
    public static class RealWorldProvider
    {
        // --- Save Tracking ---
        private static DateTime _lastSaveTime = DateTime.MinValue;

        // --- Weather Caching ---
        private static WeatherData _cachedWeather = new WeatherData();
        private static DateTime _lastWeatherUpdateTime = DateTime.MinValue;
        private static bool _isFetchingWeather = false;

        public class WeatherData
        {
            public string Condition = "Unknown";
            public float TemperatureC = 20f;
            public int Humidity = 50;
            public string Location = "Unknown";
            public string Source = "None";

            public float TemperatureF => TemperatureC * 9f / 5f + 32f;
        }

        // ==========================================
        // Utility: GZip Enabled WebClient
        // ==========================================
        /// <summary>
        /// A custom WebClient that automatically handles GZip compression.
        /// Essential because QWeather v7 strictly forces GZip responses!
        /// </summary>
        private class GZipWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                HttpWebRequest request = base.GetWebRequest(address) as HttpWebRequest;
                if (request != null)
                {
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    request.Timeout = 10000; // 10 seconds timeout to prevent UI freezing
                }
                return request;
            }
        }

        // ==========================================
        // Utility: WebException Detail Extractor
        // ==========================================
        /// <summary>
        /// Safely extracts the actual JSON error message returned by the server on a 404/401 response.
        /// </summary>
        private static string GetWebExceptionMessage(WebException wex)
        {
            if (wex.Response != null)
            {
                try
                {
                    using (var stream = wex.Response.GetResponseStream())
                    {
                        if (stream != null)
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                return reader.ReadToEnd();
                            }
                        }
                    }
                }
                catch { /* Ignore stream reading errors */ }
            }
            return "No additional details available.";
        }

        // ==========================================
        // 1. Time & Date API
        // ==========================================
        public static string GetRealTime() => DateTime.Now.ToString("HH:mm");
        public static string GetRealDate() => DateTime.Now.ToString("yyyy-MM-dd");

        // ==========================================
        // 2. Save Tracking API
        // ==========================================
        public static string GetLastSaveTime()
        {
            if (_lastSaveTime == DateTime.MinValue) return "Never Saved";
            return _lastSaveTime.ToString("yyyy-MM-dd HH:mm");
        }

        public static string GetRealSaveDiff()
        {
            if (_lastSaveTime == DateTime.MinValue) return "Never Saved";

            TimeSpan diff = DateTime.Now - _lastSaveTime;
            if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays} days ago";
            if (diff.TotalHours >= 1) return $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalMinutes >= 1) return $"{(int)diff.TotalMinutes} minutes ago";
            return "Just now";
        }

        internal static void UpdateSaveTime(DateTime time)
        {
            _lastSaveTime = time;
            if (RimTalkRealitySyncMod.Settings.DebugMode)
                Log.Message($"[RimTalk Reality Sync] Save time updated: {_lastSaveTime}");
        }

        // ==========================================
        // 3. Weather API
        // ==========================================
        public static string GetRealWeather() { EnsureWeatherUpdated(); return _cachedWeather.Condition; }
        public static string GetRealHumidity() { EnsureWeatherUpdated(); return $"{_cachedWeather.Humidity}%"; }
        public static string GetRealLocation() { EnsureWeatherUpdated(); return _cachedWeather.Location; }
        public static string GetWeatherSource() { EnsureWeatherUpdated(); return _cachedWeather.Source; }

        public static string GetRealTemperature()
        {
            EnsureWeatherUpdated();
            return RimTalkRealitySyncMod.Settings.UseCelsius
                ? $"{_cachedWeather.TemperatureC:F1}°C"
                : $"{_cachedWeather.TemperatureF:F1}°F";
        }

        private static void EnsureWeatherUpdated()
        {
            var settings = RimTalkRealitySyncMod.Settings;
            if (settings.WeatherApiProvider == "none")
            {
                _cachedWeather = GetSolarTermSimulation();
                return;
            }

            if (_isFetchingWeather) return;

            if ((DateTime.Now - _lastWeatherUpdateTime).TotalMinutes >= settings.UpdateIntervalMinutes)
            {
                _isFetchingWeather = true;

                Task.Run(() =>
                {
                    try
                    {
                        FetchWeatherFromApiSync(settings);
                    }
                    catch (Exception ex)
                    {
                        if (settings.DebugMode)
                            Log.Warning($"[RimTalk Reality Sync] Weather API failed: {ex.Message}. Falling back to simulation.");
                        _cachedWeather = GetSolarTermSimulation();
                    }
                    finally
                    {
                        _isFetchingWeather = false;
                        _lastWeatherUpdateTime = DateTime.Now;
                    }
                });
            }
        }

        private static void FetchWeatherFromApiSync(RealitySyncSettings settings)
        {
            // Enforce TLS 1.2 for modern web APIs to prevent SecureChannelFailure on RimWorld's Mono engine
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            if (settings.WeatherApiProvider == "openweathermap")
            {
                string city = Uri.EscapeDataString(settings.CustomCity);
                string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={settings.OpenWeatherApiKey}&units=metric&lang=en";

                using (WebClient client = new GZipWebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "RimTalkRealitySync/1.0");

                    try
                    {
                        string json = client.DownloadString(url);
                        ParseWeatherJson(json, settings.WeatherApiProvider, settings.CustomCity);
                    }
                    catch (WebException wex)
                    {
                        string detail = GetWebExceptionMessage(wex);
                        throw new Exception($"OpenWeather API Error: {wex.Message} | Detail: {detail}");
                    }
                }
            }
            else if (settings.WeatherApiProvider == "heweather")
            {
                string locationId = "";
                string host = settings.HeWeatherApiHost.Replace("https://", "").Replace("http://", "").TrimEnd('/');

                // Prevent routing errors by enforcing the user to input an API Host
                if (string.IsNullOrWhiteSpace(host))
                {
                    throw new Exception("RTRS_Error_EmptyHost".Translate());
                }

                // Smart Feature: If user entered purely numeric ID (e.g. 101280101), skip GeoAPI completely!
                if (int.TryParse(settings.CustomCity, out _))
                {
                    locationId = settings.CustomCity;
                }
                else
                {
                    string cityParam = Uri.EscapeDataString(settings.CustomCity);

                    // THE ULTIMATE ROUTING FIX FOR QWEATHER ARCHITECTURE:
                    // Since we removed free tier support, all GeoAPI queries must directly hit the user's dedicated host with the '/geo/' prefix.
                    string geoUrl = $"https://{host}/geo/v2/city/lookup?location={cityParam}";

                    using (WebClient geoClient = new GZipWebClient())
                    {
                        geoClient.Encoding = Encoding.UTF8;
                        geoClient.Headers.Add("User-Agent", "RimTalkRealitySync/1.0");
                        geoClient.Headers.Add("X-QW-Api-Key", settings.HeWeatherApiKey);

                        try
                        {
                            string geoJson = geoClient.DownloadString(geoUrl);
                            locationId = ExtractJsonString(geoJson, "id");

                            if (locationId == "Unknown" || string.IsNullOrEmpty(locationId))
                            {
                                throw new Exception($"HeWeather GeoAPI failed to find the Location ID for '{settings.CustomCity}'. Please check the city name.");
                            }
                        }
                        catch (WebException wex)
                        {
                            string detail = GetWebExceptionMessage(wex);
                            throw new Exception($"HeWeather GeoAPI Error [{geoUrl}]: {wex.Message} | Server Response: {detail}");
                        }
                    }
                }

                // Weather API: Use the selected host and put API Key in Header (X-QW-Api-Key)
                string url = $"https://{host}/v7/weather/now?location={locationId}&lang=en";

                using (WebClient client = new GZipWebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "RimTalkRealitySync/1.0");
                    client.Headers.Add("X-QW-Api-Key", settings.HeWeatherApiKey);

                    try
                    {
                        string json = client.DownloadString(url);
                        ParseWeatherJson(json, settings.WeatherApiProvider, settings.CustomCity);
                    }
                    catch (WebException wex)
                    {
                        string detail = GetWebExceptionMessage(wex);
                        throw new Exception($"HeWeather Data API Error [{url}]: {wex.Message} | Server Response: {detail}");
                    }
                }
            }
        }

        private static void ParseWeatherJson(string json, string provider, string defaultLocation)
        {
            _cachedWeather.Location = defaultLocation;
            _cachedWeather.Source = provider;

            try
            {
                if (provider == "openweathermap")
                {
                    _cachedWeather.TemperatureC = ExtractJsonFloat(json, "temp");
                    _cachedWeather.Humidity = (int)ExtractJsonFloat(json, "humidity");
                    _cachedWeather.Condition = ExtractJsonString(json, "description");
                }
                else if (provider == "heweather")
                {
                    _cachedWeather.TemperatureC = ExtractJsonFloat(json, "temp");
                    _cachedWeather.Humidity = (int)ExtractJsonFloat(json, "humidity");
                    _cachedWeather.Condition = ExtractJsonString(json, "text");
                }

                if (RimTalkRealitySyncMod.Settings.DebugMode)
                    Log.Message($"[RimTalk Reality Sync] Weather parsed: {_cachedWeather.Condition}, {_cachedWeather.TemperatureC}°C");
            }
            catch
            {
                throw new Exception("Failed to parse weather JSON");
            }
        }

        // --- Robust JSON extractors to handle unpredictable spacing and formatting ---

        private static float ExtractJsonFloat(string json, string key)
        {
            string searchKey = "\"" + key + "\"";
            int idx = json.IndexOf(searchKey);
            if (idx == -1) return 0f;

            idx = json.IndexOf(":", idx + searchKey.Length);
            if (idx == -1) return 0f;

            idx++;
            while (idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == '"'))
            {
                idx++;
            }

            int endIdx = idx;
            while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '.' || json[endIdx] == '-'))
            {
                endIdx++;
            }

            if (endIdx > idx)
            {
                string val = json.Substring(idx, endIdx - idx);
                float.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float result);
                return result;
            }
            return 0f;
        }

        private static string ExtractJsonString(string json, string key)
        {
            string searchKey = "\"" + key + "\"";
            int idx = json.IndexOf(searchKey);
            if (idx == -1) return "Unknown";

            idx = json.IndexOf(":", idx + searchKey.Length);
            if (idx == -1) return "Unknown";

            idx = json.IndexOf("\"", idx);
            if (idx == -1) return "Unknown";

            idx++;
            int endIdx = json.IndexOf("\"", idx);
            if (endIdx == -1) return "Unknown";

            return json.Substring(idx, endIdx - idx);
        }

        // ==========================================
        // 3.5 API Testing (Added for UI Button)
        // ==========================================
        public static void TestApiConnectionSync()
        {
            var settings = RimTalkRealitySyncMod.Settings;

            try
            {
                FetchWeatherFromApiSync(settings);

                string currentTemp = GetRealTemperature();
                string condition = _cachedWeather.Condition;

                string successMsg = $"{"RTRS_TestAPI_Success".Translate()} [{condition}, {currentTemp}]";
                Messages.Message(successMsg, MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                if (settings.DebugMode)
                    Log.Error($"[RimTalk Reality Sync] API Test Failed: {ex.Message}");

                Messages.Message("RTRS_TestAPI_Fail".Translate(), MessageTypeDefOf.NegativeEvent, false);
            }
        }

        // ==========================================
        // 4. Solar Term Simulation (Fallback)
        // ==========================================
        public static string GetSolarTermString() => GetSolarTermSimulation().Condition;

        private static WeatherData GetSolarTermSimulation()
        {
            var now = DateTime.Now;
            int month = now.Month;

            WeatherData data = new WeatherData
            {
                Source = "Seasonal Simulation",
                Location = RimTalkRealitySyncMod.Settings.CustomCity
            };

            if (month >= 3 && month <= 5) { data.Condition = "Spring Breeze"; data.TemperatureC = 15f; data.Humidity = 60; }
            else if (month >= 6 && month <= 8) { data.Condition = "Summer Heat"; data.TemperatureC = 30f; data.Humidity = 70; }
            else if (month >= 9 && month <= 11) { data.Condition = "Autumn Cool"; data.TemperatureC = 18f; data.Humidity = 50; }
            else { data.Condition = "Winter Cold"; data.TemperatureC = 2f; data.Humidity = 40; }

            return data;
        }
    }

    // ==========================================
    // 5. Minimal Harmony Patches for Save Tracking
    // ==========================================
    [HarmonyPatch(typeof(GameDataSaveLoader), "SaveGame")]
    public static class SaveGamePatch
    {
        public static void Postfix()
        {
            RealWorldProvider.UpdateSaveTime(DateTime.Now);
        }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), "LoadGame", typeof(string))]
    public static class LoadGamePatch
    {
        public static void Postfix(string saveFileName)
        {
            try
            {
                string savesFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves");
                string path = Path.Combine(savesFolder, saveFileName + ".rws");
                if (!File.Exists(path)) path = Path.Combine(savesFolder, saveFileName);

                if (File.Exists(path))
                {
                    RealWorldProvider.UpdateSaveTime(new FileInfo(path).LastWriteTime);
                }
            }
            catch (Exception ex)
            {
                if (RimTalkRealitySyncMod.Settings.DebugMode)
                    Log.Warning($"[RimTalk Reality Sync] Failed to get load time: {ex.Message}");
            }
        }
    }
}