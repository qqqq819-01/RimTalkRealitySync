using HarmonyLib;
using RimTalk.API;
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
                // =====================================================================
                // 1. BACKEND REGISTRATION (Silent Mode)
                // We use the official API for backend Scriban stability, but pass "" 
                // for descriptions so we can handle the UI purely through our own patch.
                // =====================================================================

                // =====================================================================
                // FIXED: Evade the "Null Map" Silencer
                // Changed from RegisterEnvironmentVariable to RegisterContextVariable.
                // ContextVariables bypass RimTalk's strict requirement for a valid "Map", 
                // ensuring weather/time data renders even for external Discord observers!
                // =====================================================================
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealTime", ctx => RealWorldProvider.GetRealTime(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealDate", ctx => RealWorldProvider.GetRealDate(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_LastSaveTime", ctx => RealWorldProvider.GetLastSaveTime(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealSaveDiff", ctx => RealWorldProvider.GetRealSaveDiff(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealWeather", ctx => RealWorldProvider.GetRealWeather(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealTemperature", ctx => RealWorldProvider.GetRealTemperature(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealHumidity", ctx => RealWorldProvider.GetRealHumidity(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_RealLocation", ctx => RealWorldProvider.GetRealLocation(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_WeatherSource", ctx => RealWorldProvider.GetWeatherSource(), "", 100);
                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_SolarTerm", ctx => RealWorldProvider.GetSolarTermString(), "", 100);

                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_IsExternalUser", ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    return ctx.TalkRequest.RawPrompt.Contains("[高维通讯]") ? "true" : "";
                }, "", 100);

                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_SenderName", ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "玩家";
                    var match = System.Text.RegularExpressions.Regex.Match(ctx.TalkRequest.RawPrompt, @"\[高维通讯\]\s'([^']+)'");
                    return match.Success ? match.Groups[1].Value.Replace("{", "(").Replace("}", ")") : "玩家";
                }, "", 100);

                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_HasImage", ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    return System.Text.RegularExpressions.Regex.IsMatch(ctx.TalkRequest.RawPrompt, @"<RIMPHONE_(?:URL|LOCAL_IMG|IMG)[^>]+>") ? "true" : "";
                }, "", 100);

                RimTalkPromptAPI.RegisterContextVariable(modId, "rs_ImageTag", ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    var match = System.Text.RegularExpressions.Regex.Match(ctx.TalkRequest.RawPrompt, @"<RIMPHONE_(?:URL|LOCAL_IMG|IMG)[^>]+>");
                    return match.Success ? match.Value : "";
                }, "", 100);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimPhone] Official API Registration failed: {ex.Message}");
            }
        }
    }

    // =====================================================================
    // 2. FRONTEND UI HIJACK (The Beauty Shield)
    // Intercepts RimTalk's UI dictionary to remove auto-generated ugly tags 
    // and reinstates our beautiful, fully-translated custom category.
    // =====================================================================
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

            // =====================================================================
            // FIXED: Ultimate Duplicate Assassin
            // RimTalk's API secretly strips punctuation from ModIDs (e.g. rimtalkrealitysync).
            // To be bulletproof against any future API string changes, we target the 
            // variable name prefix "rs_" directly to kill the auto-generated duplicates.
            // =====================================================================
            foreach (var category in __result.Values)
            {
                category.RemoveAll(tuple => tuple.Item1 != null && tuple.Item1.StartsWith("rs_"));
            }

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
                ("rs_SolarTerm", GetUIString("RTRS_Desc_SolarTerm", "Current solar term or season")),
                ("rs_IsExternalUser", GetUIString("RTRS_Desc_IsExternal", "Returns 'true' if the sender is an external observer")),
                ("rs_SenderName", GetUIString("RTRS_Desc_SenderName", "Name of the message sender")),
                ("rs_HasImage", GetUIString("RTRS_Desc_HasImage", "Returns 'true' if an image is attached")),
                ("rs_ImageTag", GetUIString("RTRS_Desc_ImageTag", "The raw image code block for LLM parsing"))
            };

            string categoryName = GetUIString("RTRS_Category_Name", "Reality Sync Variables");
            __result[categoryName] = uiList;
        }
    }
}