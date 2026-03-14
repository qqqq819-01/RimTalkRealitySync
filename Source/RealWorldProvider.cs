using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
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

            if (_isFetchingWeather) return; // Prevent multiple simultaneous requests

            if ((DateTime.Now - _lastWeatherUpdateTime).TotalMinutes >= settings.UpdateIntervalMinutes)
            {
                _isFetchingWeather = true;

                // Fetch weather asynchronously to prevent game freezing!
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
            string url = "";
            string city = Uri.EscapeDataString(settings.CustomCity);

            if (settings.WeatherApiProvider == "openweathermap")
            {
                url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={settings.WeatherApiKey}&units=metric&lang=en";
            }
            else if (settings.WeatherApiProvider == "heweather")
            {
                url = $"https://devapi.qweather.com/v7/weather/now?location={city}&key={settings.WeatherApiKey}&lang=en";
            }

            if (string.IsNullOrEmpty(url)) return;

            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers.Add("User-Agent", "RimTalkRealitySync/1.0");
                string json = client.DownloadString(url);
                ParseWeatherJson(json, settings.WeatherApiProvider, settings.CustomCity);
            }
        }

        private static void ParseWeatherJson(string json, string provider, string defaultLocation)
        {
            // A simple string manipulation parser to avoid heavy JSON libraries dependencies
            _cachedWeather.Location = defaultLocation;
            _cachedWeather.Source = provider;

            try
            {
                if (provider == "openweathermap")
                {
                    _cachedWeather.TemperatureC = ExtractFloat(json, "\"temp\":");
                    _cachedWeather.Humidity = (int)ExtractFloat(json, "\"humidity\":");
                    _cachedWeather.Condition = ExtractString(json, "\"description\":\"", "\"");
                }
                else if (provider == "heweather")
                {
                    _cachedWeather.TemperatureC = ExtractFloat(json, "\"temp\":\"");
                    _cachedWeather.Humidity = (int)ExtractFloat(json, "\"humidity\":\"");
                    _cachedWeather.Condition = ExtractString(json, "\"text\":\"", "\"");
                }

                if (RimTalkRealitySyncMod.Settings.DebugMode)
                    Log.Message($"[RimTalk Reality Sync] Weather parsed: {_cachedWeather.Condition}, {_cachedWeather.TemperatureC}°C");
            }
            catch
            {
                throw new Exception("Failed to parse weather JSON");
            }
        }

        // --- Simple JSON extractors ---
        private static float ExtractFloat(string json, string key)
        {
            int idx = json.IndexOf(key);
            if (idx == -1) return 0f;
            idx += key.Length;
            int endIdx = json.IndexOfAny(new char[] { ',', '}', '"' }, idx);
            if (endIdx == -1) return 0f;
            string val = json.Substring(idx, endIdx - idx).Trim('"', ' ');
            float.TryParse(val, out float result);
            return result;
        }

        private static string ExtractString(string json, string key, string endDelimiter)
        {
            int idx = json.IndexOf(key);
            if (idx == -1) return "Unknown";
            idx += key.Length;
            int endIdx = json.IndexOf(endDelimiter, idx);
            if (endIdx == -1) return "Unknown";
            return json.Substring(idx, endIdx - idx);
        }

        // ==========================================
        // 3.5 API Testing (Added for UI Button)
        // ==========================================
        /// <summary>
        /// Manually triggers a synchronous API fetch to test the connection and settings.
        /// Called from the mod settings menu. 
        /// It runs synchronously because RimWorld UI messages must be called on the main thread.
        /// </summary>
        public static void TestApiConnectionSync()
        {
            var settings = RimTalkRealitySyncMod.Settings;

            try
            {
                // Force a manual synchronous fetch
                FetchWeatherFromApiSync(settings);

                // Fetch the formatted temperature (respecting Celsius/Fahrenheit settings)
                string currentTemp = GetRealTemperature();
                string condition = _cachedWeather.Condition; // 获取天气描述

                // Connection successful, show a positive message with the actual fetched data
                // Combine the translated success message with the actual temperature and condition
                string successMsg = $"{"RTRS_TestAPI_Success".Translate()} [{condition}, {currentTemp}]";

                Messages.Message(successMsg, MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                if (settings.DebugMode)
                    Log.Error($"[RimTalk Reality Sync] API Test Failed: {ex.Message}");

                // Connection failed, show a negative message
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

            // Extremely simplified seasonal simulation
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