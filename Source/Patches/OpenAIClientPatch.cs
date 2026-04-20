using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
using RimTalk.Service;
using RimTalk.Source.Data;
using Verse;

namespace RimTalkRealitySync.Patches
{
    [HarmonyPatch(typeof(OpenAIClient), "BuildRequestJson")]
    public static class OpenAIClient_BuildRequestJson_Patch
    {
        private static readonly Regex ImgTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)(?:<|\\u003C|\\u003c)RIMPHONE_(?:LOCAL_)?IMG:(data:[^;]+;base64[,，].+?)(?:>|\\u003E|\\u003e)((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex UrlTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)(?:<|\\u003C|\\u003c)RIMPHONE_URL:(.+?)(?:>|\\u003E|\\u003e)((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            if (!__result.Contains("RIMPHONE_")) return;

            try
            {
                bool matched = false;

                if (__result.Contains("RIMPHONE_URL") || __result.Contains("rimphone_url"))
                {
                    __result = UrlTagRegex.Replace(__result, match =>
                    {
                        matched = true;
                        string beforeText = match.Groups[1].Value;
                        string imageUrl = match.Groups[2].Value.Replace("\\/", "/");
                        string afterText = match.Groups[3].Value;
                        string combinedText = beforeText + afterText;
                        return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{imageUrl}\"}}}}]";
                    });
                }
                else
                {
                    __result = ImgTagRegex.Replace(__result, match =>
                    {
                        matched = true;
                        string beforeText = match.Groups[1].Value;
                        string base64Url = match.Groups[2].Value.Replace("\\/", "/");
                        string afterText = match.Groups[3].Value;
                        string combinedText = beforeText + afterText;
                        return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{base64Url}\"}}}}]";
                    });
                }

                if (matched)
                {
                    // =====================================================================
                    // CRITICAL FIX: Role Hijack
                    // Vision APIs strictly forbid images inside the "system" role.
                    // We must convert "system" to "user" if an image is attached.
                    // =====================================================================
                    __result = Regex.Replace(__result, @"\""role\""\s*:\s*\""system\""", "\"role\":\"user\"", RegexOptions.IgnoreCase);
                    Verse.Log.Message("[RimPhone] Successfully intercepted JSON and converted system role to user role for Vision API.");
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[RimPhone] Error during OpenAIClient json interception: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(TalkService), "AddResponsesToHistory")]
    public static class TalkService_AddResponsesToHistory_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return;
            string localizedTag = "RTRS_Tag_AttachedImage".Translate();
            prompt = Regex.Replace(prompt, @"<RIMPHONE_URL:[^>]+>", localizedTag);
            prompt = Regex.Replace(prompt, @"<RIMPHONE_IMG:[^>]+>", localizedTag);
            prompt = Regex.Replace(prompt, @"<RIMPHONE_LOCAL_IMG:[^>]+>", localizedTag);
        }
    }

    [HarmonyPatch(typeof(Player2Client), "BuildRequestJson")]
    public static class Player2Client_BuildRequestJson_Patch
    {
        // Regex for matching local Base64 images
        private static readonly Regex Player2ImgTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)(?:<|\\u003C|\\u003c)RIMPHONE_(?:LOCAL_)?IMG:(data:[^;]+;base64[,，].+?)(?:>|\\u003E|\\u003e)((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // NEW: Regex for matching network URL images
        private static readonly Regex UrlTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)(?:<|\\u003C|\\u003c)RIMPHONE_URL:(.+?)(?:>|\\u003E|\\u003e)((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            if (!__result.Contains("RIMPHONE_")) return;

            try
            {
                bool matched = false;

                // 1. Check and parse network URL images first
                if (__result.Contains("RIMPHONE_URL") || __result.Contains("rimphone_url"))
                {
                    __result = UrlTagRegex.Replace(__result, match =>
                    {
                        matched = true;
                        string beforeText = match.Groups[1].Value;
                        string imageUrl = match.Groups[2].Value.Replace("\\/", "/"); // Unescape JSON slashes
                        string afterText = match.Groups[3].Value;
                        string combinedText = beforeText + afterText;

                        // Reconstruct into OpenAI standard vision request format
                        return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{imageUrl}\"}}}}]";
                    });
                }
                // 2. Fallback to parse local Base64 images
                else
                {
                    __result = Player2ImgTagRegex.Replace(__result, match =>
                    {
                        matched = true;
                        string beforeText = match.Groups[1].Value;
                        string base64Url = match.Groups[2].Value.Replace("\\/", "/"); // Unescape JSON slashes
                        string afterText = match.Groups[3].Value;
                        string combinedText = beforeText + afterText;

                        return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{base64Url}\"}}}}]";
                    });
                }

                if (matched)
                {
                    // Role Hijack for Player2: Force "system" role to "user" role to prevent Vision API crash
                    __result = Regex.Replace(__result, @"\""role\""\s*:\s*\""system\""", "\"role\":\"user\"", RegexOptions.IgnoreCase);
                    Verse.Log.Message("[RimPhone] Successfully intercepted and formatted multimodal JSON (including URL) for Player2.");
                }
                else
                {
                    // Log a warning if the tag exists but regex fails to catch it
                    Verse.Log.Warning("[RimPhone] Found RIMPHONE tag in Player2 JSON but Regex failed to match!");
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[RimPhone] Error during Player2 json interception: {ex.Message}");
            }
        }
    }
}