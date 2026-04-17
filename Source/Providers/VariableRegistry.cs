using HarmonyLib;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalkRealitySync;
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
                // 1. WEATHER & REAL-WORLD VARIABLES
                // =====================================================================
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

                // =====================================================================
                // 2. MULTIMODAL & OBSERVER VARIABLES (Omni-Platform)
                // =====================================================================
                ContextHookRegistry.RegisterContextVariable("rs_IsExternalUser", modId, new Func<PromptContext, string>(ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    // Detect the new refined beacon
                    return ctx.TalkRequest.RawPrompt.Contains("[高维通讯]") ? "true" : "";
                }), "", 100);

                ContextHookRegistry.RegisterContextVariable("rs_SenderName", modId, new Func<PromptContext, string>(ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "玩家";
                    // Extract name from: [高维通讯] 'Name' 传来了话语...
                    var match = System.Text.RegularExpressions.Regex.Match(ctx.TalkRequest.RawPrompt, @"\[高维通讯\]\s'([^']+)'");

                    return match.Success ? match.Groups[1].Value.Replace("{", "(").Replace("}", ")") : "玩家";
                }), "", 100);

                ContextHookRegistry.RegisterContextVariable("rs_HasImage", modId, new Func<PromptContext, string>(ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    return System.Text.RegularExpressions.Regex.IsMatch(ctx.TalkRequest.RawPrompt, @"<RIMPHONE_(?:URL|LOCAL_IMG|IMG)[^>]+>") ? "true" : "";
                }), "", 100);

                ContextHookRegistry.RegisterContextVariable("rs_ImageTag", modId, new Func<PromptContext, string>(ctx =>
                {
                    if (ctx.TalkRequest == null || string.IsNullOrEmpty(ctx.TalkRequest.RawPrompt)) return "";
                    var match = System.Text.RegularExpressions.Regex.Match(ctx.TalkRequest.RawPrompt, @"<RIMPHONE_(?:URL|LOCAL_IMG|IMG)[^>]+>");
                    return match.Success ? match.Value : "";
                }), "", 100);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk Reality Sync] Registration failed: {ex.Message}");
            }
        }
    }

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

            foreach (var category in __result.Values)
            {
                category.RemoveAll(tuple => tuple.Item2 != null &&
                                            (tuple.Item2.Contains("(from RimTalk.RealitySync)") ||
                                             tuple.Item2.Contains("(from rimtalk.realitysync)")));
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

                // =====================================================================
                // UI Name updated to reflect multi-platform support
                // =====================================================================
                ("rs_IsExternalUser", GetUIString("RTRS_Desc_IsExternal", "Returns 'true' if the sender is an external observer (Discord/KOOK/QQ)")),
                ("rs_SenderName", GetUIString("RTRS_Desc_SenderName", "Name of the message sender (Player or Observer)")),
                ("rs_HasImage", GetUIString("RTRS_Desc_HasImage", "Returns 'true' if an image is attached to the request")),
                ("rs_ImageTag", GetUIString("RTRS_Desc_ImageTag", "The raw image code block for LLM parsing"))
            };

            string categoryName = GetUIString("RTRS_Category_Name", "Reality Sync Variables");
            __result[categoryName] = uiList;
        }
    }
}