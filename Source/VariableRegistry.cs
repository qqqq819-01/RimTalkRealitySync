using HarmonyLib;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Prompt;
using System;
using System.Collections.Generic;
using Verse;

namespace RimTalkRealitySync
{
    [StaticConstructorOnStartup]
    public static class VariableRegistry
    {
        static VariableRegistry()
        {
            LongEventHandler.ExecuteWhenFinished(RegisterAllVariables);
        }

        private static void RegisterAllVariables()
        {
            string modId = "RimTalk.RealitySync";
            try
            {
                // Backend variables (Empty descriptions to prevent RimTalk's messy default UI)
                ContextHookRegistry.RegisterContextVariable("rs_RealTime", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealTime()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealDate", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealDate()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_LastSaveTime", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetLastSaveTime()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealSaveDiff", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealSaveDiff()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealWeather", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealWeather()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealTemperature", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealTemperature()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealHumidity", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealHumidity()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_RealLocation", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetRealLocation()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_WeatherSource", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetWeatherSource()), "", 100);
                ContextHookRegistry.RegisterContextVariable("rs_SolarTerm", modId, new Func<PromptContext, string>(ctx => RealWorldProvider.GetSolarTermString()), "", 100);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk Reality Sync] Registration failed: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // Frontend UI Hijack Patch
    // =========================================================================
    [HarmonyPatch]
    public static class UIVariableInjectionPatch
    {
        public static bool Prepare()
        {
            return AccessTools.TypeByName("RimTalk.Prompt.VariableDefinitions") != null;
        }

        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("RimTalk.Prompt.VariableDefinitions"), "GetScribanVariables");
        }

        // The Ultimate Safe Translator: PURE ENGLISH FALLBACKS ONLY
        private static string GetUIString(string key, string fallbackEnglish)
        {
            string trans = key.Translate();
            if (string.IsNullOrWhiteSpace(trans) || trans.Contains(key))
            {
                return fallbackEnglish;
            }
            return trans;
        }

        public static void Postfix(ref Dictionary<string, List<(string, string)>> __result)
        {
            if (__result == null) return;

            // Step A: Assassinate RimTalk's auto-generated messy entries
            foreach (var category in __result.Values)
            {
                category.RemoveAll(tuple => tuple.Item2 != null &&
                                            (tuple.Item2.Contains("(from RimTalk.RealitySync)") ||
                                             tuple.Item2.Contains("(from rimtalk.realitysync)")));
            }

            // Step B: Inject our clean UI entries with PURE ENGLISH fallbacks!
            var uiList = new List<(string, string)>
            {
                ("rs_RealTime", GetUIString("RTRS_Desc_Time", "Current real-world time (HH:mm)")),
                ("rs_RealDate", GetUIString("RTRS_Desc_Date", "Current real-world date (yyyy-MM-dd)")),
                ("rs_LastSaveTime", GetUIString("RTRS_Desc_LastSave", "Real-world time of last save")),
                ("rs_RealSaveDiff", GetUIString("RTRS_Desc_SaveDiff", "Time elapsed since last save")),
                ("rs_RealWeather", GetUIString("RTRS_Desc_Weather", "Real-world weather condition")),
                ("rs_RealTemperature", GetUIString("RTRS_Desc_Temp", "Real-world temperature")),
                ("rs_RealHumidity", GetUIString("RTRS_Desc_Humidity", "Real-world humidity percentage")),
                ("rs_RealLocation", GetUIString("RTRS_Desc_Location", "Real-world geographical location (City)")),
                ("rs_WeatherSource", GetUIString("RTRS_Desc_Source", "Weather data source")),
                ("rs_SolarTerm", GetUIString("RTRS_Desc_SolarTerm", "Current solar term or season"))
            };

            // Step C: Create a dedicated category (Pure English fallback)
            string categoryName = GetUIString("RTRS_Category_Name", "Reality Sync Variables");
            __result[categoryName] = uiList;
        }
    }
}